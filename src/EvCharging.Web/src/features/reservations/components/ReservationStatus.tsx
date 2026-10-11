import { Badge } from "@mantine/core";
import type { Reservation } from "../api";

const statuses = {
  active: { label: "Active reservation", color: "teal" },
  consumed: { label: "Consumed", color: "blue" },
  cancelled: { label: "Cancelled", color: "red" },
  expired: { label: "Expired", color: "gray" },
} satisfies Record<Reservation["state"], { label: string; color: string }>;

export function ReservationStatus({ state }: { state: Reservation["state"] }) {
  const status = statuses[state];

  return (
    <Badge
      variant="light"
      color={status.color}
      w="fit-content"
      styles={{ label: { textTransform: "none" } }}
    >
      {status.label}
    </Badge>
  );
}
