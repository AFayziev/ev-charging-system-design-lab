import { Image } from "@mantine/core";
import park from "../assets/charging-park.png";
import classes from "./Stations.module.css";

export function StationPhoto() {
  return (
    <figure className={classes.photo}>
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
