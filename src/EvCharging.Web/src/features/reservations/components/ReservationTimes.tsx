import { Stack, Text } from "@mantine/core";
import dayjs from "dayjs";
import calendar from "dayjs/plugin/calendar";
import timezone from "dayjs/plugin/timezone";
import utc from "dayjs/plugin/utc";

dayjs.extend(utc);
dayjs.extend(timezone);
dayjs.extend(calendar);

export function formatReservationSlot(
  startsAt: string,
  endsAt: string,
  timeZone: string,
) {
  const start = dayjs(startsAt).tz(timeZone);
  const end = dayjs(endsAt).tz(timeZone);
  const now = dayjs().tz(timeZone);
  const datedTime = "D MMM YYYY HH:mm";
  const formats = {
    sameDay: "[Today] HH:mm",
    nextDay: "[Tomorrow] HH:mm",
    lastDay: datedTime,
    lastWeek: datedTime,
    nextWeek: datedTime,
    sameElse: datedTime,
  };
  const startLabel = start.calendar(now, formats);

  if (start.isSame(end, "day")) return `${startLabel} - ${end.format("HH:mm")}`;
  return `${startLabel} - ${end.calendar(now, formats)}`;
}

export function formatTime(instant: string, timeZone: string) {
  return new Intl.DateTimeFormat(undefined, {
    timeZone,
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "longOffset",
  }).format(new Date(instant));
}

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
