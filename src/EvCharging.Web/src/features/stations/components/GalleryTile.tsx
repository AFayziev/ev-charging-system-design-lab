import {
  ActionIcon,
  Badge,
  Group,
  Loader,
  Progress,
  Text,
  Tooltip,
} from "@mantine/core";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  IconCheck,
  IconChevronLeft,
  IconChevronRight,
  IconGripVertical,
  IconReplace,
  IconTrash,
  IconUpload,
  IconX,
} from "@tabler/icons-react";
import type { GalleryItem, GalleryReplacement } from "../api";
import { getPhotoFailureMessage } from "../errors";
import classes from "./GalleryEditor.module.css";

/** What this browser knows about a photo it is sending: the file preview and how far the transfer is. */
export interface PhotoTransfer {
  readonly preview: string;
  readonly progress: number;
}

export interface GalleryTileProps {
  item: GalleryItem;
  index: number;
  count: number;
  transfer: PhotoTransfer | undefined;
  replacementTransfer: PhotoTransfer | undefined;
  confirmingRemoval: boolean;
  onMove: (to: number) => void;
  onReplace: () => void;
  onCancelUpload: () => void;
  onAskRemove: () => void;
  onKeep: () => void;
  onRemove: () => void;
}

const percent = (transfer: PhotoTransfer | undefined) =>
  Math.round((transfer?.progress ?? 0) * 100);

export function GalleryTile({
  item,
  index,
  count,
  transfer,
  replacementTransfer,
  confirmingRemoval,
  onMove,
  onReplace,
  onCancelUpload,
  onAskRemove,
  onKeep,
  onRemove,
}: GalleryTileProps) {
  const sortable = useSortable({ id: item.id });
  const failed = item.state === "failed";
  const background = item.thumbnailUrl ?? transfer?.preview;
  const replacement = item.replacement;
  return (
    <article
      ref={sortable.setNodeRef}
      className={`${classes.tile} ${failed ? classes.tileFailed : ""}`}
      style={{
        transform: CSS.Transform.toString(sortable.transform),
        transition: sortable.transition,
        opacity: sortable.isDragging ? 0.5 : 1,
      }}
      aria-label={`Photo ${index + 1}`}
    >
      <div
        className={classes.thumb}
        style={background ? { backgroundImage: `url('${background}')` } : {}}
      >
        <Badge
          className={classes.position}
          color={index === 0 ? "teal" : "gray"}
          variant="light"
        >
          {index === 0 ? "Cover" : index + 1}
        </Badge>
        <button
          type="button"
          className={classes.handle}
          aria-label={`Drag photo ${index + 1} to reorder`}
          ref={sortable.setActivatorNodeRef}
          {...sortable.attributes}
          {...sortable.listeners}
        >
          <IconGripVertical size={16} aria-hidden="true" />
        </button>
        {item.state === "uploading" && (
          <div className={classes.overlay}>
            <span>
              {transfer ? `Uploading ${percent(transfer)}%` : "Uploading…"}
            </span>
            <Progress
              className={classes.progress}
              value={transfer ? percent(transfer) : 100}
              animated={!transfer}
              size="sm"
              aria-label="Upload progress"
            />
            <Tooltip label="Cancel upload">
              <ActionIcon
                variant="default"
                size={32}
                aria-label={`Cancel upload of photo ${index + 1}`}
                onClick={onCancelUpload}
              >
                <IconX size={16} aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
          </div>
        )}
        {item.state === "processing" && (
          <div className={classes.overlay} role="status">
            <Loader size="sm" />
            <span>Optimizing…</span>
          </div>
        )}
        {failed && (
          <div className={`${classes.overlay} ${classes.overlayFailed}`}>
            <strong>Upload failed</strong>
            <Text size="xs" fw={500} c="inherit">
              {getPhotoFailureMessage(item.failureCode)}
            </Text>
          </div>
        )}
        {item.state === "published" && replacement && (
          <ReplacementRibbon
            replacement={replacement}
            transfer={replacementTransfer}
          />
        )}
      </div>
      <div className={classes.foot}>
        <div className={classes.moves}>
          <ActionIcon
            variant="default"
            size={32}
            disabled={index === 0}
            aria-label={`Move photo ${index + 1} earlier`}
            onClick={() => onMove(index - 1)}
          >
            <IconChevronLeft size={16} aria-hidden="true" />
          </ActionIcon>
          <ActionIcon
            variant="default"
            size={32}
            disabled={index === count - 1}
            aria-label={`Move photo ${index + 1} later`}
            onClick={() => onMove(index + 1)}
          >
            <IconChevronRight size={16} aria-hidden="true" />
          </ActionIcon>
        </div>
        <div className={classes.moves}>
          {(item.state === "published" || failed) && (
            <Tooltip label={failed ? "Upload again" : "Replace photo"}>
              <ActionIcon
                variant="default"
                size={32}
                aria-label={
                  failed
                    ? `Upload photo ${index + 1} again`
                    : `Replace photo ${index + 1}`
                }
                onClick={onReplace}
              >
                {failed ? (
                  <IconUpload size={16} aria-hidden="true" />
                ) : (
                  <IconReplace size={16} aria-hidden="true" />
                )}
              </ActionIcon>
            </Tooltip>
          )}
          <Tooltip label="Remove photo">
            <ActionIcon
              variant="light"
              color="red"
              size={32}
              aria-label={`Remove photo ${index + 1}`}
              onClick={onAskRemove}
            >
              <IconTrash size={16} aria-hidden="true" />
            </ActionIcon>
          </Tooltip>
        </div>
      </div>
      {confirmingRemoval && (
        <div className={classes.confirm}>
          Remove this photo?
          <Group gap={6} mt={8}>
            <Tooltip label="Confirm removal">
              <ActionIcon
                color="red"
                size={32}
                aria-label={`Confirm removal of photo ${index + 1}`}
                onClick={onRemove}
              >
                <IconCheck size={16} aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Keep photo">
              <ActionIcon
                variant="default"
                size={32}
                aria-label={`Keep photo ${index + 1}`}
                onClick={onKeep}
              >
                <IconX size={16} aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
          </Group>
        </div>
      )}
    </article>
  );
}

/** How the photo waiting to replace a published one is getting on. */
function ReplacementRibbon({
  replacement,
  transfer,
}: {
  replacement: GalleryReplacement;
  transfer: PhotoTransfer | undefined;
}) {
  switch (replacement.state) {
    case "uploading":
      return (
        <div className={classes.ribbon}>
          Uploading {percent(transfer)}%
          <Progress
            value={percent(transfer)}
            size="sm"
            flex={1}
            aria-label="Replacement upload progress"
          />
        </div>
      );
    case "processing":
      return (
        <div className={classes.ribbon} role="status">
          <Loader size={14} /> Optimizing…
        </div>
      );
    case "failed": {
      const message = getPhotoFailureMessage(replacement.failureCode);
      return (
        <div
          className={`${classes.ribbon} ${classes.ribbonFailed}`}
          title={message}
        >
          Replacement failed: {message}
        </div>
      );
    }
    default:
      return null;
  }
}
