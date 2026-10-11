import { Image } from "@mantine/core";
import park from "../assets/charging-park.png";
import classes from "./Stations.module.css";

export function StationPhoto({ className = "" }: { className?: string }) {
  return (
    <figure className={`${classes.photo} ${className}`}>
      <Image
        src={park}
        alt="Illustrative charging station"
        width={1536}
        height={1024}
      />
      <figcaption>Illustrative photo</figcaption>
    </figure>
  );
}
