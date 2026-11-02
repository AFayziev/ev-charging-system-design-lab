import { StationPhoto } from "./StationPhoto";
import { useState } from "react";
import classes from "./Stations.module.css";

/**
 * A station's published instructional video with the browser's own controls. The first frame stands in for a
 * poster until it is played. A video that cannot be loaded gives way to a placeholder.
 */
export function StationVideo({
  url,
  className = "",
}: {
  url: string;
  className?: string;
}) {
  // The gallery keys this by video, so a different video starts over.
  const [failed, setFailed] = useState(false);
  if (failed) {
    return <StationPhoto label="Video unavailable" className={className} />;
  }
  return (
    <figure className={`${classes.photo} ${className}`}>
      <video
        className={classes.galleryVideo}
        src={`${url}#t=0.1`}
        controls
        playsInline
        preload="metadata"
        aria-label="Station video"
        onError={() => setFailed(true)}
      />
    </figure>
  );
}
