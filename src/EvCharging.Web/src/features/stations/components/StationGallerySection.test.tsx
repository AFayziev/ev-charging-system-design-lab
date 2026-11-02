import { TestProviders, createTestQueryClient } from "@/test/providers";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { HttpStatusCode } from "axios";
import { StationGallerySection } from "./StationGallerySection";

const { get, post, put, del } = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  del: vi.fn(),
}));
vi.mock("@/lib/api", () => ({ api: { get, post, put, delete: del } }));
const { readDuration } = vi.hoisted(() => ({ readDuration: vi.fn() }));
vi.mock("../videoDuration", () => ({ readVideoDuration: readDuration }));
// The transfer to storage is not what these tests are about; it never finishes.
vi.mock("../api", async (original) => ({
  ...(await original<typeof import("../api")>()),
  uploadToStorage: () => new Promise(() => {}),
}));

const gallery = (galleryVersion: number) => ({
  stationId: "station",
  galleryVersion,
  items: [],
  limits: {
    maximumPhotosPerStation: 5,
    maximumPhotoBytes: 8 * 1024 * 1024,
    maximumPhotoPixels: 24_000_000,
    photoContentTypes: ["image/jpeg"],
    maximumVideosPerStation: 1,
    maximumVideoBytes: 50 * 1024 * 1024,
    maximumVideoDurationSeconds: 60,
    videoContentTypes: ["video/mp4"],
    uploadUrlLifetimeSeconds: 900,
  },
});
const staleVersion = Object.assign(new Error("conflict"), {
  isAxiosError: true,
  response: {
    status: HttpStatusCode.Conflict,
    data: { code: "concurrency_conflict" },
  },
});

let galleryReads = 0;

beforeEach(() => {
  galleryReads = 0;
  readDuration.mockReset();
  readDuration.mockResolvedValue(30);
  get.mockReset();
  post.mockReset();
  put.mockReset();
  del.mockReset();
  get.mockImplementation((url: string) =>
    Promise.resolve({
      data: url.endsWith("/edit")
        ? gallery(1 + galleryReads++)
        : { signedIn: true, antiforgeryToken: "csrf" },
    }),
  );
});

async function addPhoto() {
  render(
    <TestProviders client={createTestQueryClient()}>
      <StationGallerySection stationId="station" />
    </TestProviders>,
  );
  await screen.findByText("0 / 5 photos");
  const input = document.querySelector<HTMLInputElement>(
    "input[type=file]:not([data-testid])",
  );
  await userEvent.upload(
    input!,
    new File(["bytes"], "entrance.jpg", { type: "image/jpeg" }),
  );
}

it("reloads the gallery and starts the upload again when its version is stale", async () => {
  post.mockRejectedValueOnce(staleVersion).mockResolvedValueOnce({
    data: {
      itemId: "item",
      uploadUrl: "https://storage.test/u",
      galleryVersion: 3,
    },
  });

  await addPhoto();

  await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
  const versions = post.mock.calls.map(([, body]) => body.galleryVersion);
  expect(versions[0]).toBeLessThan(versions[1]);
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("tells the operator when the gallery changed again after the reload", async () => {
  post.mockRejectedValue(staleVersion);

  await addPhoto();

  expect(
    await screen.findByText("Gallery changed elsewhere"),
  ).toBeInTheDocument();
  expect(post).toHaveBeenCalledTimes(2);
});

it("sends a retried photo back to the place of the failed one", async () => {
  const item = (id: string, position: number, state: string) => ({
    id,
    kind: "photo",
    position,
    state,
    failureCode: state === "failed" ? "undecodable" : null,
    thumbnailUrl: state === "published" ? `https://storage.test/${id}` : null,
    videoUrl: null,
    replacement: null,
  });
  // Each read of the gallery: as loaded, after the failed photo is removed, after the retry is added.
  const reads = [
    [item("failed", 0, "failed"), item("kept", 1, "published")],
    [item("kept", 0, "published")],
    [item("kept", 0, "published"), item("new", 1, "uploading")],
  ];
  let read = 0;
  get.mockImplementation((url: string) =>
    Promise.resolve({
      data: url.endsWith("/edit")
        ? { ...gallery(1), items: reads[Math.min(read++, reads.length - 1)] }
        : { signedIn: true, antiforgeryToken: "csrf" },
    }),
  );
  del.mockResolvedValue({});
  put.mockResolvedValue({});
  post.mockResolvedValue({
    data: {
      itemId: "new",
      uploadUrl: "https://storage.test/u",
      galleryVersion: 5,
    },
  });

  render(
    <TestProviders client={createTestQueryClient()}>
      <StationGallerySection stationId="station" />
    </TestProviders>,
  );
  await userEvent.click(
    await screen.findByRole("button", { name: "Upload photo 1 again" }),
  );
  await userEvent.upload(
    screen.getByTestId("replace-photo-input"),
    new File(["bytes"], "retry.jpg", { type: "image/jpeg" }),
  );

  await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
  expect(put.mock.calls[0][1].itemIds).toEqual(["new", "kept"]);
});

const publishedVideo = {
  id: "video",
  kind: "video",
  position: 0,
  state: "published",
  failureCode: null,
  thumbnailUrl: null,
  videoUrl: "https://storage.test/video/video.mp4",
  replacement: null,
};

async function openGallery(items: readonly object[] = []) {
  get.mockImplementation((url: string) =>
    Promise.resolve({
      data: url.endsWith("/edit")
        ? { ...gallery(1), items }
        : { signedIn: true, antiforgeryToken: "csrf" },
    }),
  );
  post.mockResolvedValue({
    data: {
      itemId: "new",
      uploadUrl: "https://storage.test/u",
      galleryVersion: 2,
    },
  });
  render(
    <TestProviders client={createTestQueryClient()}>
      <StationGallerySection stationId="station" />
    </TestProviders>,
  );
  await screen.findByText(/ \/ 5 photos$/);
  return document.querySelector<HTMLInputElement>(
    "input[type=file]:not([data-testid])",
  )!;
}

const clip = new File(["bytes"], "how-to-plug-in.mp4", { type: "video/mp4" });

it("starts a video upload as a video", async () => {
  const input = await openGallery();

  await userEvent.upload(input, clip);

  await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
  expect(post.mock.calls[0][0]).toMatch(/\/media\/uploads$/);
  expect(post.mock.calls[0][1]).toMatchObject({
    kind: "video",
    contentType: "video/mp4",
    byteLength: clip.size,
  });
});

it("turns a video that is too long away before sending any of it", async () => {
  readDuration.mockResolvedValue(90);
  const input = await openGallery();

  await userEvent.upload(input, clip);

  expect(await screen.findByText("Video is too long")).toBeInTheDocument();
  expect(post).not.toHaveBeenCalled();
});

it("leaves a video the browser cannot read to the server to judge", async () => {
  readDuration.mockResolvedValue(null);
  const input = await openGallery();

  await userEvent.upload(input, clip);

  await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
});

it("keeps the video slot separate from the photo slots", async () => {
  const input = await openGallery([publishedVideo]);
  expect(screen.getByText("1 / 1 video")).toBeInTheDocument();

  await userEvent.upload(input, clip);

  expect(await screen.findByText("Gallery is full")).toBeInTheDocument();
  expect(
    screen.getByText("Only 1 video fits. 1 not added."),
  ).toBeInTheDocument();
  expect(post).not.toHaveBeenCalled();

  await userEvent.upload(
    input,
    new File(["bytes"], "entrance.jpg", { type: "image/jpeg" }),
  );
  await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
  expect(post.mock.calls[0][1]).toMatchObject({ kind: "photo" });
});

it("shows the video as a video, with its own labels and no image", async () => {
  await openGallery([publishedVideo]);

  expect(screen.getByRole("article", { name: "Video 1" })).toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Replace video 1" }),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Remove video 1" }),
  ).toBeInTheDocument();
});

it("tells why a video was refused", async () => {
  await openGallery([
    {
      ...publishedVideo,
      state: "failed",
      failureCode: "unsupported_codec",
      videoUrl: null,
    },
  ]);

  expect(
    await screen.findByText(/Browsers can't play this video/),
  ).toBeInTheDocument();
});
