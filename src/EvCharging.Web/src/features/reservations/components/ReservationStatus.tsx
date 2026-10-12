import { Badge, Stack, Text } from "@mantine/core";
import type { Reservation } from "../api";
import { formatReservationTransitionTime } from "../time-formatting";

const statuses = {
  active: { label: "Active reservation", color: "teal" },
  consumed: { label: "Consumed", color: "blue" },
  cancelled: { label: "Cancelled", color: "red" },
  expired: { label: "Expired", color: "gray" },
} satisfies Record<Reservation["state"], { label: string; color: string }>;

export function ReservationStatus({
  reservation,
}: {
  reservation: Pick<
    Reservation,
    "state" | "cancelledAt" | "expiredAt" | "timeZoneId"
  >;
}) {
  const { state, cancelledAt, expiredAt, timeZoneId } = reservation;
  const status = statuses[state];
  const recordedAt = state === "cancelled" ? cancelledAt : expiredAt;

  return (
    <Stack gap={4}>
      <Badge
        variant="light"
        color={status.color}
        w="fit-content"
        styles={{ label: { textTransform: "none" } }}
      >
        {status.label}
      </Badge>
      {recordedAt && (
        <Text size="xs" c="dimmed">
          {status.label}{" "}
          {formatReservationTransitionTime(recordedAt, timeZoneId)}
        </Text>
      )}
    </Stack>
  );
}
