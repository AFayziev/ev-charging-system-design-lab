import { expect, it } from "vitest";
import type { ChargingProgress, ChargingReading } from "./api";
import { getProgressPresentation } from "./progress-presentation";

const reading: ChargingReading = {
  state: "charging",
  powerKw: 7,
  energyKWh: 2,
  isFinalEnergy: false,
  occurredAt: "2026-10-21T09:00:00Z",
};
const progress = (overrides: Partial<ChargingProgress>): ChargingProgress => ({
  sessionId: "session",
  reservationId: "reservation",
  lifecycle: "active",
  startedAt: "2026-10-21T08:59:00Z",
  completedAt: null,
  finalEnergyKWh: null,
  reading,
  isStale: false,
  isSilentPastLimit: false,
  elapsedSeconds: 60,
  ...overrides,
});

it.each([
  ["a fresh reading", progress({}), "Charging"],
  ["a stale reading", progress({ isStale: true }), "Last reported charging"],
  ["no reading yet", progress({ reading: null }), "Waiting for charger data"],
  [
    "no reading for too long",
    progress({ reading: null, isStale: true }),
    "No charging data since start",
  ],
  [
    "a stopping session with its last reading",
    progress({ lifecycle: "stopping" }),
    "Stopping charging",
  ],
  [
    "a session that is starting",
    progress({ lifecycle: "starting", reading: null, startedAt: null }),
    "Confirming start",
  ],
])("titles %s", (_, value, title) => {
  expect(getProgressPresentation(value).title).toBe(title);
});

it("shows a completed session with its frozen final usage and end time", () => {
  const completed = getProgressPresentation(
    progress({
      lifecycle: "completed",
      completedAt: "2026-10-21T09:05:00Z",
      finalEnergyKWh: 7.256,
      reading: { ...reading, state: "stopped", isFinalEnergy: true },
    }),
  );

  expect(completed.title).toBe("Charging completed");
  expect(completed.tone).toBe("success");
  expect(completed.text).toMatch(/final usage 7\.26 kWh/i);
});

it("tells a stopping session with its final reading that the station stopped", () => {
  const stopped = getProgressPresentation(
    progress({
      lifecycle: "stopping",
      reading: { ...reading, state: "stopped", isFinalEnergy: true },
    }),
  );

  expect(stopped.title).toBe("Stopping charging");
  expect(stopped.text).toMatch(/station stopped charging/i);
});
