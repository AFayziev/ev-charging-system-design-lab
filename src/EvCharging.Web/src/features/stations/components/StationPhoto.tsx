import { IconBolt } from "@tabler/icons-react";
import { useEffect, useState } from "react";
import type { StationPhoto as Photo } from "../api";
import classes from "./Stations.module.css";

// Storage that accepts a request but never answers produces no error event, so a photo that has not
// arrived by now is treated like one that failed. Images load eagerly so the limit measures the
// request, not how far down the page the photo sits.
const photoLoadLimitMs = 10_000;

/** Whether an image should still be shown: it has not failed, or run out of time to arrive. */
function usePhotoLoad(url: string | null) {
  // The image that failed or never arrived, so a different photo gets its own chance to load.
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const [loadedUrl, setLoadedUrl] = useState<string | null>(null);

  useEffect(() => {
    if (url === null || loadedUrl === url) return;
    const timer = setTimeout(() => setFailedUrl(url), photoLoadLimitMs);
    return () => clearTimeout(timer);
  }, [url, loadedUrl]);

  return {
    show: url !== null && failedUrl !== url,
    onLoad: () => setLoadedUrl(url),
    onError: () => setFailedUrl(url),
  };
}

/** A published photo, or a placeholder when the station has none or the image cannot be loaded. */
export function StationPhoto({
  photo,
  variant = "thumbnail",
  label,
  className = "",
}: {
  photo?: Photo | null;
  variant?: "thumbnail" | "display";
  /** What the placeholder says when there is nothing to show. */
  label?: string;
  className?: string;
}) {
  const url = photo
    ? variant === "display"
      ? photo.displayUrl
      : photo.thumbnailUrl
    : null;
  const load = usePhotoLoad(url);
  return (
    <figure className={`${classes.photo} ${className}`}>
      {load.show && url ? (
        <img
          src={url}
          alt="Station"
          onLoad={load.onLoad}
          onError={load.onError}
        />
      ) : (
        <div className={classes.placeholder} role="img" aria-label={label}>
          <IconBolt size={40} aria-hidden="true" />
          {label && <span>{label}</span>}
        </div>
      )}
    </figure>
  );
}
