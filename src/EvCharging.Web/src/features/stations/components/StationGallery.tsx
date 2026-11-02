import { ActionIcon } from "@mantine/core";
import {
  IconChevronLeft,
  IconChevronRight,
  IconPlayerPlay,
} from "@tabler/icons-react";
import { useState } from "react";
import type { StationMedia } from "../api";
import { StationPhoto } from "./StationPhoto";
import { StationVideo } from "./StationVideo";
import classes from "./Stations.module.css";

/** The published photos and video of a station: one large view, with the others as thumbnails to choose from. */
export function StationGallery({ media }: { media: readonly StationMedia[] }) {
  const [selected, setSelected] = useState(0);
  if (media.length === 0) {
    return (
      <StationPhoto label="No photos yet" className={classes.galleryHero} />
    );
  }
  const index = Math.min(selected, media.length - 1);
  const current = media[index];
  const step = (by: number) =>
    setSelected((index + by + media.length) % media.length);
  return (
    <section aria-label="Station photos" className={classes.gallery}>
      <div className={classes.galleryFrame}>
        {current.kind === "video" ? (
          <StationVideo
            key={current.id}
            url={current.videoUrl}
            className={classes.galleryHero}
          />
        ) : (
          <StationPhoto
            key={current.id}
            photo={current}
            variant="display"
            label="Photo unavailable"
            className={classes.galleryHero}
          />
        )}
        {media.length > 1 && (
          <>
            <ActionIcon
              className={classes.galleryPrevious}
              variant="white"
              radius="xl"
              size={40}
              aria-label="Previous item"
              onClick={() => step(-1)}
            >
              <IconChevronLeft size={20} aria-hidden="true" />
            </ActionIcon>
            <ActionIcon
              className={classes.galleryNext}
              variant="white"
              radius="xl"
              size={40}
              aria-label="Next item"
              onClick={() => step(1)}
            >
              <IconChevronRight size={20} aria-hidden="true" />
            </ActionIcon>
            <span className={classes.galleryCount}>
              {index + 1} / {media.length}
            </span>
          </>
        )}
      </div>
      {media.length > 1 && (
        <div className={classes.galleryStrip}>
          {media.map((item, position) => (
            <button
              key={item.id}
              type="button"
              aria-label={`Show ${item.kind} ${position + 1}`}
              aria-current={position === index}
              onClick={() => setSelected(position)}
            >
              {item.kind === "video" ? (
                <span className={classes.stripVideo}>
                  <IconPlayerPlay size={20} aria-hidden="true" />
                </span>
              ) : (
                <StationPhoto photo={item} className={classes.stripPhoto} />
              )}
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
