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

export function formatReservationTransitionTime(
  instant: string,
  timeZone: string,
) {
  const time = dayjs(instant).tz(timeZone);
  const now = dayjs().tz(timeZone);
  const datedTime = "D MMM YYYY [at] HH:mm";
  return time.calendar(now, {
    sameDay: "[today at] HH:mm",
    lastDay: "[yesterday at] HH:mm",
    nextDay: "[tomorrow at] HH:mm",
    lastWeek: datedTime,
    nextWeek: datedTime,
    sameElse: datedTime,
  });
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
