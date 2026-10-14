import dayjs from "dayjs";
import calendar from "dayjs/plugin/calendar";

dayjs.extend(calendar);

export function getBrowserTimeZone() {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

export function formatReservationSlot(startsAt: string, endsAt: string) {
  const start = dayjs(startsAt);
  const end = dayjs(endsAt);
  const now = dayjs();
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

export function formatReservationTransitionTime(instant: string) {
  const time = dayjs(instant);
  const now = dayjs();
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
export function formatStartDeadline(deadline: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(new Date(deadline));
}
