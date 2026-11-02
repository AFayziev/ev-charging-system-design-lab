import { ActionIcon } from "@mantine/core";
import { IconChevronLeft, IconChevronRight } from "@tabler/icons-react";
import { useState } from "react";
import type { StationPhoto as Photo } from "../api";
import { StationPhoto } from "./StationPhoto";
import classes from "./Stations.module.css";

/** The published photos of a station: one large view, with the others as thumbnails to choose from. */
export function StationGallery({ photos }: { photos: readonly Photo[] }) {
  const [selected, setSelected] = useState(0);
  if (photos.length === 0) {
    return (
      <StationPhoto label="No photos yet" className={classes.galleryHero} />
    );
  }
  const index = Math.min(selected, photos.length - 1);
  const current = photos[index];
  const step = (by: number) =>
    setSelected((index + by + photos.length) % photos.length);
  return (
    <section aria-label="Station photos" className={classes.gallery}>
      <div className={classes.galleryFrame}>
        <StationPhoto
          key={current.id}
          photo={current}
          variant="display"
          label="Photo unavailable"
          className={classes.galleryHero}
        />
        {photos.length > 1 && (
          <>
            <ActionIcon
              className={classes.galleryPrevious}
              variant="white"
              radius="xl"
              size={40}
              aria-label="Previous photo"
              onClick={() => step(-1)}
            >
              <IconChevronLeft size={20} aria-hidden="true" />
            </ActionIcon>
            <ActionIcon
              className={classes.galleryNext}
              variant="white"
              radius="xl"
              size={40}
              aria-label="Next photo"
              onClick={() => step(1)}
            >
              <IconChevronRight size={20} aria-hidden="true" />
            </ActionIcon>
            <span className={classes.galleryCount}>
              {index + 1} / {photos.length}
            </span>
          </>
        )}
      </div>
      {photos.length > 1 && (
        <div className={classes.galleryStrip}>
          {photos.map((photo, position) => (
            <button
              key={photo.id}
              type="button"
              aria-label={`Show photo ${position + 1}`}
              aria-current={position === index}
              onClick={() => setSelected(position)}
            >
              <StationPhoto photo={photo} className={classes.stripPhoto} />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
