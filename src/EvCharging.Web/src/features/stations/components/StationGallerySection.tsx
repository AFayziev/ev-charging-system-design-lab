import {
  Button,
  Card,
  Group,
  Loader,
  Progress,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { Dropzone, type FileRejection } from "@mantine/dropzone";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  arrayMove,
  rectSortingStrategy,
  SortableContext,
  sortableKeyboardCoordinates,
} from "@dnd-kit/sortable";
import { IconPhotoUp } from "@tabler/icons-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { FailureAlert, StatusAlert } from "@/components/StatusAlert";
import { useUserSession } from "@/features/auth/useUserSession";
import { getHttpFailure, getHttpProblemCode } from "@/lib/http-errors";
import {
  completeMediaUpload,
  removeGalleryItem,
  reorderGallery,
  startMediaUpload,
  stationGalleryQuery,
  stationKeys,
  uploadToStorage,
  type GalleryEditor,
  type MediaKind,
  type MediaUpload,
} from "../api";
import { isConcurrencyConflict, isGalleryFull } from "../errors";
import { readVideoDuration } from "../videoDuration";
import { GalleryTile, type PhotoTransfer } from "./GalleryTile";
import classes from "./GalleryEditor.module.css";

interface Notice {
  readonly tone: "error" | "warning";
  readonly title: string;
  readonly message: string;
}

const storageUnavailable: Notice = {
  tone: "warning",
  title: "Photo storage is unavailable",
  message: "The photo could not be sent. Try again shortly.",
};

const megabytes = (bytes: number) => `${Math.round(bytes / 1024 / 1024)} MB`;

const kinds: readonly MediaKind[] = ["photo", "video"];

/** "image/jpeg" as "JPEG". */
const typeName = (contentType: string) =>
  contentType.slice(contentType.indexOf("/") + 1).toUpperCase();

/** The operator's gallery of photos and one video: upload, follow processing, reorder, replace and remove. */
export function StationGallerySection({ stationId }: { stationId: string }) {
  const query = useQuery(stationGalleryQuery(stationId));
  return (
    <Card withBorder padding="lg" maw={760} w="100%">
      <Stack>
        <Title order={2} size="h3">
          Photos
        </Title>
        {query.isPending && <Loader size="sm" aria-label="Loading photos" />}
        {query.isError && (
          <FailureAlert failure={getHttpFailure(query.error)}>
            <Button
              variant="white"
              size="sm"
              w="fit-content"
              loading={query.isFetching}
              onClick={() => void query.refetch()}
            >
              Reload photos
            </Button>
          </FailureAlert>
        )}
        {query.data && (
          <GalleryEditorPanel stationId={stationId} editor={query.data} />
        )}
      </Stack>
    </Card>
  );
}

function GalleryEditorPanel({
  stationId,
  editor,
}: {
  stationId: string;
  editor: GalleryEditor;
}) {
  const client = useQueryClient();
  const session = useUserSession();
  const token = session.antiforgeryToken ?? "";
  const key = stationKeys.gallery(stationId);
  const [transfers, setTransfers] = useState<Record<string, PhotoTransfer>>({});
  const controllers = useRef(new Map<string, AbortController>());
  const [notice, setNotice] = useState<Notice | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const replaceInput = useRef<HTMLInputElement>(null);
  const replaceTarget = useRef<string | null>(null);
  // Uploads start one at a time: each start moves the gallery version the next one must carry.
  const startQueue = useRef<Promise<void>>(Promise.resolve());
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const { limits, items } = editor;
  const kindOf = (file: File): MediaKind =>
    limits.videoContentTypes.includes(file.type) ? "video" : "photo";
  const capacity: Record<MediaKind, number> = {
    photo: limits.maximumPhotosPerStation,
    video: limits.maximumVideosPerStation,
  };
  const maximumBytes: Record<MediaKind, number> = {
    photo: limits.maximumPhotoBytes,
    video: limits.maximumVideoBytes,
  };
  const acceptedTypes: Record<MediaKind, readonly string[]> = {
    photo: limits.photoContentTypes,
    video: limits.videoContentTypes,
  };
  // Failed items keep their place but no longer count toward their limit. Each kind has its own limit.
  const occupied: Record<MediaKind, number> = {
    photo: items.filter(
      (item) => item.kind !== "video" && item.state !== "failed",
    ).length,
    video: items.filter(
      (item) => item.kind === "video" && item.state !== "failed",
    ).length,
  };
  const free: Record<MediaKind, number> = {
    photo: Math.max(0, capacity.photo - occupied.photo),
    video: Math.max(0, capacity.video - occupied.video),
  };
  const full = kinds.every((kind) => free[kind] === 0);
  const refresh = () => client.invalidateQueries({ queryKey: key });
  const currentVersion = () =>
    client.getQueryData<GalleryEditor>(key)?.galleryVersion ??
    editor.galleryVersion;

  // A change made elsewhere moves the version: reload the gallery and try once more.
  const withVersion = async <T,>(action: (version: number) => Promise<T>) => {
    try {
      return await action(currentVersion());
    } catch (error) {
      if (!isConcurrencyConflict(error)) throw error;
      await client.refetchQueries({ queryKey: key });
      return await action(currentVersion());
    }
  };

  const failNotice = (error: unknown): Notice => {
    if (isGalleryFull(error))
      return {
        tone: "error",
        title: "Gallery is full",
        message: "Remove an item to add another.",
      };
    if (isConcurrencyConflict(error))
      return {
        tone: "warning",
        title: "Gallery changed elsewhere",
        message: "Reloaded. Try again.",
      };
    const failure = getHttpFailure(error, "write");
    return { tone: "error", ...failure };
  };

  const clearTransfer = (itemId: string) => {
    controllers.current.delete(itemId);
    setTransfers((current) => {
      const { [itemId]: finished, ...rest } = current;
      if (finished) URL.revokeObjectURL(finished.preview);
      return rest;
    });
  };

  // Drops a photo that cannot finish. If the removal itself fails, the upload deadline fails it instead.
  const discard = async (itemId: string) => {
    try {
      await removeGalleryItem(stationId, itemId, token);
    } catch {
      // The photo stays as an unfinished upload and expires at its deadline; it can also be removed by hand.
    }
    clearTransfer(itemId);
    await refresh();
  };

  const transfer = async (
    upload: MediaUpload,
    file: File,
    controller: AbortController,
  ) => {
    const { itemId } = upload;
    try {
      await uploadToStorage(
        upload.uploadUrl,
        file,
        // Tiles show whole percents, so finer progress events leave the gallery as it is.
        (progress) =>
          setTransfers((current) => {
            const entry = current[itemId];
            return entry &&
              Math.round(entry.progress * 100) !== Math.round(progress * 100)
              ? { ...current, [itemId]: { ...entry, progress } }
              : current;
          }),
        controller.signal,
      );
    } catch {
      // Cancelling is the operator's choice; anything else, including a stalled transfer, is storage failing.
      if (!controller.signal.aborted) setNotice(storageUnavailable);
      await discard(itemId);
      return;
    }
    try {
      await completeMediaUpload(stationId, itemId, token);
    } catch (error) {
      // A rejected upload is shown on its tile; anything else is a failure to report.
      if (getHttpProblemCode(error) !== "upload_failed")
        setNotice(failNotice(error));
    } finally {
      clearTransfer(itemId);
      await refresh();
    }
  };

  const begin = async (
    file: File,
    replacesItemId?: string,
    position?: number,
  ) => {
    const kind = kindOf(file);
    if (kind === "video") {
      // A clip this browser can read and finds too long is turned away before any of it is sent.
      const seconds = await readVideoDuration(file);
      if (seconds !== null && seconds > limits.maximumVideoDurationSeconds) {
        setNotice({
          tone: "error",
          title: "Video is too long",
          message: `${file.name}: videos can be at most ${limits.maximumVideoDurationSeconds} seconds.`,
        });
        return;
      }
    }
    let upload: MediaUpload;
    try {
      upload = await withVersion((galleryVersion) =>
        startMediaUpload(
          stationId,
          {
            galleryVersion,
            kind,
            contentType: file.type,
            byteLength: file.size,
            replacesItemId,
          },
          token,
        ),
      );
    } catch (error) {
      setNotice(failNotice(error));
      return;
    }
    client.setQueryData<GalleryEditor>(
      key,
      (previous) =>
        previous && { ...previous, galleryVersion: upload.galleryVersion },
    );
    const controller = new AbortController();
    controllers.current.set(upload.itemId, controller);
    const preview = URL.createObjectURL(file);
    setTransfers((current) => ({
      ...current,
      [upload.itemId]: { preview, progress: 0 },
    }));
    void transfer(upload, file, controller);
    await refresh();
    // A retried photo returns to the place of the one it replaces, which may be the cover.
    if (position !== undefined) await move(upload.itemId, position);
  };

  const queue = (start: () => Promise<void>) => {
    startQueue.current = startQueue.current.then(start);
  };

  const rejectFiles = (names: readonly string[], alsoFull?: string) =>
    setNotice({
      tone: "error",
      title: "Some files were skipped",
      message: `${names.join(", ")}: photos can be ${limits.photoContentTypes.map(typeName).join(", ")} up to ${megabytes(limits.maximumPhotoBytes)}, and a video can be MP4 up to ${megabytes(limits.maximumVideoBytes)}.${alsoFull ? ` ${alsoFull}` : ""}`,
    });

  const enqueue = (files: readonly File[], replacesItemId?: string) => {
    // Each kind fills its own slots; a replacement takes no new slot.
    const remaining = { ...free };
    const oversized: File[] = [];
    const accepted: File[] = [];
    const skipped: File[] = [];
    for (const file of files) {
      const kind = kindOf(file);
      if (file.size > maximumBytes[kind]) oversized.push(file);
      else if (replacesItemId || remaining[kind] > 0) {
        remaining[kind] -= 1;
        accepted.push(file);
      } else skipped.push(file);
    }
    const fullNotice =
      skipped.length === 0
        ? undefined
        : skipped.every((file) => kindOf(file) === "video")
          ? `Only ${capacity.video} video fits. ${skipped.length} not added.`
          : `Only ${capacity.photo} photos fit. ${skipped.length} not added.`;
    if (oversized.length > 0)
      rejectFiles(
        oversized.map((file) => file.name),
        fullNotice,
      );
    else if (fullNotice)
      setNotice({
        tone: "warning",
        title: "Gallery is full",
        message: fullNotice,
      });
    else setNotice(null);
    for (const file of accepted) queue(() => begin(file, replacesItemId));
  };

  const reject = (rejections: readonly FileRejection[]) =>
    rejectFiles(rejections.map((rejection) => rejection.file.name));

  const move = async (itemId: string, to: number) => {
    // The gallery as it is now: a photo just added is not in the props this render started from.
    const current = client.getQueryData<GalleryEditor>(key)?.items ?? items;
    const from = current.findIndex((item) => item.id === itemId);
    if (from < 0 || to < 0 || to >= current.length || to === from) return;
    const ordered = arrayMove([...current], from, to);
    client.setQueryData<GalleryEditor>(
      key,
      (previous) =>
        previous && {
          ...previous,
          items: ordered.map((item, position) => ({ ...item, position })),
        },
    );
    try {
      // Not retried on a stale version: the order was built from a gallery that has since changed, and
      // sending it again would undo the other editor's change. The reload below shows the current order.
      await reorderGallery(
        stationId,
        currentVersion(),
        ordered.map((item) => item.id),
        token,
      );
    } catch (error) {
      setNotice(failNotice(error));
    }
    await refresh();
  };

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    void move(
      String(active.id),
      items.findIndex((item) => item.id === over.id),
    );
  };

  const remove = async (itemId: string) => {
    setConfirming(null);
    // The photo disappears at once; drivers stop seeing it as soon as the request lands.
    client.setQueryData<GalleryEditor>(
      key,
      (previous) =>
        previous && {
          ...previous,
          items: previous.items.filter((item) => item.id !== itemId),
        },
    );
    try {
      await removeGalleryItem(stationId, itemId, token);
    } catch (error) {
      setNotice(failNotice(error));
    }
    await refresh();
  };

  const replace = (files: FileList | null) => {
    const file = files?.[0];
    const target = items.find((item) => item.id === replaceTarget.current);
    replaceTarget.current = null;
    if (!file || !target) return;
    if (kindOf(file) !== target.kind) {
      setNotice({
        tone: "error",
        title: "Different kind of file",
        message:
          target.kind === "video"
            ? "Replace a video with another video."
            : "Replace a photo with another photo.",
      });
      return;
    }
    if (target.state === "failed") {
      // A failed slot is retried by removing it and sending the file as a new photo in the same place.
      setNotice(null);
      void removeGalleryItem(stationId, target.id, token)
        .then(refresh)
        .then(() => queue(() => begin(file, undefined, target.position)))
        .catch((error: unknown) => setNotice(failNotice(error)));
      return;
    }
    enqueue([file], target.id);
  };

  const cancelUpload = (itemId: string) => {
    const controller = controllers.current.get(itemId);
    if (controller) controller.abort();
    // After a reload there is no transfer to stop, only the unfinished photo to drop.
    else void discard(itemId);
  };

  return (
    <Stack>
      <Group justify="space-between" align="flex-end">
        <Text size="sm" c="dimmed" maw={420}>
          The first photo is the cover.
        </Text>
        <Stack gap={4} className={classes.meter}>
          <Text fw={700} size="sm">
            {occupied.photo} / {capacity.photo} photos
          </Text>
          <Progress
            value={(occupied.photo / capacity.photo) * 100}
            size="sm"
            aria-label="Gallery occupancy"
          />
          <Text size="xs" c="dimmed">
            {occupied.video} / {capacity.video} video
          </Text>
        </Stack>
      </Group>
      {notice && (
        <StatusAlert
          tone={notice.tone}
          title={notice.title}
          withCloseButton
          onClose={() => setNotice(null)}
        >
          {notice.message}
        </StatusAlert>
      )}
      <Dropzone
        onDrop={(files) => enqueue(files)}
        onReject={reject}
        accept={[...limits.photoContentTypes, ...limits.videoContentTypes]}
        maxSize={Math.max(limits.maximumPhotoBytes, limits.maximumVideoBytes)}
        disabled={full}
        aria-label="Add photos or a video"
      >
        <Stack align="center" gap={4} py="sm">
          <IconPhotoUp size={30} aria-hidden="true" />
          <Text fw={600}>Drag photos or a video here, or click to add</Text>
          <Text size="xs" c="dimmed">
            {full
              ? "The gallery is full."
              : [
                  free.photo > 0 &&
                    `${limits.photoContentTypes.map(typeName).join(", ")} · up to ${megabytes(limits.maximumPhotoBytes)} · ${free.photo} slot${free.photo === 1 ? "" : "s"} left`,
                  free.video > 0 &&
                    `MP4 video · up to ${megabytes(limits.maximumVideoBytes)} and ${limits.maximumVideoDurationSeconds} s`,
                ]
                  .filter(Boolean)
                  .join(" · ")}
          </Text>
        </Stack>
      </Dropzone>
      <input
        ref={replaceInput}
        type="file"
        hidden
        data-testid="replace-photo-input"
        accept={limits.photoContentTypes.join(",")}
        onChange={(event) => {
          replace(event.target.files);
          event.target.value = "";
        }}
      />
      {items.length === 0 ? (
        <Text c="dimmed" ta="center" py="md">
          No photos yet.
        </Text>
      ) : (
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={onDragEnd}
        >
          <SortableContext
            items={items.map((item) => item.id)}
            strategy={rectSortingStrategy}
          >
            <div className={classes.grid}>
              {items.map((item, index) => (
                <GalleryTile
                  key={item.id}
                  item={item}
                  index={index}
                  count={items.length}
                  transfer={transfers[item.id]}
                  replacementTransfer={
                    item.replacement
                      ? transfers[item.replacement.id]
                      : undefined
                  }
                  confirmingRemoval={confirming === item.id}
                  onMove={(to) => void move(item.id, to)}
                  onReplace={() => {
                    replaceTarget.current = item.id;
                    if (replaceInput.current) {
                      // A replacement is a file of the same kind as the item it replaces.
                      replaceInput.current.accept =
                        acceptedTypes[item.kind].join(",");
                      replaceInput.current.click();
                    }
                  }}
                  onCancelUpload={() => cancelUpload(item.id)}
                  onAskRemove={() => setConfirming(item.id)}
                  onKeep={() => setConfirming(null)}
                  onRemove={() => void remove(item.id)}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
    </Stack>
  );
}
