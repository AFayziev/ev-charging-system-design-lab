import { Stack, Text } from "@mantine/core";
import { formatTime } from "../time-formatting";

export function ReservationTimes({
  startsAt,
  endsAt,
  timeZoneId,
  showUtc = false,
}: {
  startsAt: string;
  endsAt: string;
  timeZoneId: string;
  showUtc?: boolean;
}) {
  return (
    <Stack gap={4}>
      <Text size="sm" fw={600}>
        Station time · {timeZoneId}
      </Text>
      <Text size="sm">Start: {formatTime(startsAt, timeZoneId)}</Text>
      <Text size="sm">End: {formatTime(endsAt, timeZoneId)}</Text>
      {showUtc && (
        <Text size="sm" c="dimmed">
          UTC: {formatTime(startsAt, "UTC")} → {formatTime(endsAt, "UTC")}
        </Text>
      )}
    </Stack>
  );
}
