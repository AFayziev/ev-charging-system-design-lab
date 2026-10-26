import { Text } from "@mantine/core";

const sortLabels = {
  distance: "nearest first",
  relevance: "best matches first",
  name: "sorted by name",
} as const;

const countFormat = new Intl.NumberFormat();

/** How many stations matched, within what distance when the search is local, and how they are ordered. */
export function StationSearchCaption({
  total,
  totalIsExact,
  radiusKm,
  sort,
}: {
  total: number;
  totalIsExact: boolean;
  radiusKm: number | null;
  sort: keyof typeof sortLabels;
}) {
  const count = `${countFormat.format(total)}${totalIsExact ? "" : "+"}`;
  const noun = total === 1 && totalIsExact ? "station" : "stations";
  const within = radiusKm === null ? "" : ` within ${radiusKm} km`;
  return (
    <Text c="dimmed" size="sm" role="status">
      {`${count} ${noun}${within} · ${sortLabels[sort]}`}
    </Text>
  );
}
