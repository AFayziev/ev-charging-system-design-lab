const clock = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});
const clockWithSeconds = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hourCycle: "h23",
});

/** An instant as the user's local 24-hour clock reads it, to the minute. */
export function formatClock(instant: string) {
  return clock.format(new Date(instant));
}

/** A chart label as a clock time when it is an instant; anything else, such as an empty label, is shown as it is. */
export function formatClockLabel(label: unknown) {
  const text = String(label ?? "");
  return Number.isNaN(Date.parse(text)) ? text : formatClock(text);
}

/** An instant as the user's local 24-hour clock reads it, to the second. */
export function formatClockWithSeconds(instant: string) {
  return clockWithSeconds.format(new Date(instant));
}

/** A duration as H:MM:SS. */
export function formatElapsed(milliseconds: number) {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}
