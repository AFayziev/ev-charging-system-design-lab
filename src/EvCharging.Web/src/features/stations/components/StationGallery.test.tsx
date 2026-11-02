import { TestProviders, createTestQueryClient } from "@/test/providers";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it } from "vitest";
import type { StationMedia } from "../api";
import { StationGallery } from "./StationGallery";

const displayUrl = "https://media.test/photo/display.webp";
const videoUrl = "https://media.test/video/video.mp4";
const photo: StationMedia = {
  id: "photo",
  kind: "photo",
  thumbnailUrl: "https://media.test/photo/thumbnail.webp",
  displayUrl,
};
const video: StationMedia = { id: "video", kind: "video", videoUrl };

const renderGallery = (media: readonly StationMedia[]) =>
  render(
    <TestProviders client={createTestQueryClient()}>
      <StationGallery media={media} />
    </TestProviders>,
  );

it("shows a placeholder for a station without media", () => {
  renderGallery([]);

  expect(screen.getByRole("img", { name: "No photos yet" })).toBeVisible();
});

it("opens on the first photo and plays the video once it is chosen", async () => {
  renderGallery([photo, video]);

  // The large view comes first; the strip below has a thumbnail of the photo too.
  expect(screen.getAllByRole("img", { name: "Station" })[0]).toHaveAttribute(
    "src",
    displayUrl,
  );
  expect(screen.queryByLabelText("Station video")).not.toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: "Show video 2" }));

  const player = screen.getByLabelText("Station video");
  expect(player).toHaveAttribute(
    "src",
    `${video.kind === "video" ? video.videoUrl : ""}#t=0.1`,
  );
  expect(player).toHaveAttribute("controls");
  expect(player).toHaveAttribute("preload", "metadata");
});

it("steps through photos and the video with the arrows", async () => {
  renderGallery([photo, video]);

  await userEvent.click(screen.getByRole("button", { name: "Next item" }));

  expect(screen.getByLabelText("Station video")).toBeInTheDocument();
  expect(screen.getByText("2 / 2")).toBeInTheDocument();
});

it("replaces a video that cannot be loaded with a placeholder", async () => {
  renderGallery([video]);

  fireEvent.error(screen.getByLabelText("Station video"));

  expect(screen.getByRole("img", { name: "Video unavailable" })).toBeVisible();
  expect(screen.queryByLabelText("Station video")).not.toBeInTheDocument();
});
