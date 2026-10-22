import { expect, it } from "vitest";
import { formatClockLabel, formatElapsed } from "./time-formatting";

it.each([
  [0, "0:00:00"],
  [61_000, "0:01:01"],
  [3_725_000, "1:02:05"],
  [-5_000, "0:00:00"],
])("formats %i ms as %s", (milliseconds, expected) => {
  expect(formatElapsed(milliseconds)).toBe(expected);
});

it.each([[""], [undefined], [null], ["not a time"]])(
  "shows the label %j as it is when it is not an instant",
  (label) => {
    expect(formatClockLabel(label)).toBe(label == null ? "" : String(label));
  },
);

it("shows an instant as a clock time", () => {
  expect(formatClockLabel("2026-10-21T09:05:00Z")).toMatch(/^\d\d:\d\d$/);
});
