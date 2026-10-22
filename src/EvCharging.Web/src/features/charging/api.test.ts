import { expect, it } from "vitest";
import {
  mergePowerBuckets,
  shouldPoll,
  type ChargingProgress,
  type PowerBucket,
} from "./api";

const reading = {
  state: "charging",
  powerKw: 7,
  energyKWh: 2,
  isFinalEnergy: false,
  occurredAt: "2026-10-21T09:59:55Z",
} as const;
const progress = (overrides: Partial<ChargingProgress>): ChargingProgress => ({
  sessionId: "session",
  reservationId: "reservation",
  lifecycle: "active",
  startedAt: "2026-10-21T09:00:00Z",
  reading,
  isStale: false,
  isSilentPastLimit: false,
  elapsedSeconds: 600,
  ...overrides,
});
it.each([
  ["a session that is reporting", progress({}), true],
  ["a session that has not reported yet", progress({ reading: null }), true],
  ["a completed session", progress({ lifecycle: "completed" }), false],
  ["a failed session", progress({ lifecycle: "failed" }), false],
  [
    "a session that sent its final reading",
    progress({
      lifecycle: "stopping",
      reading: { ...reading, isFinalEnergy: true },
    }),
    false,
  ],
  [
    "a session the server reports silent for too long",
    progress({ isSilentPastLimit: true }),
    false,
  ],
])("polls %s: %s", (_, value, expected) => {
  expect(shouldPoll(value)).toBe(expected);
});

const bucket = (startsAt: string, averagePowerKw: number): PowerBucket => ({
  startsAt,
  averagePowerKw,
  maximumPowerKw: averagePowerKw,
  lastEnergyKWh: 1,
});

it("replaces the open bucket and appends newer ones in order", () => {
  const merged = mergePowerBuckets(
    [bucket("2026-10-21T09:00:00Z", 4), bucket("2026-10-21T09:01:00Z", 6)],
    [bucket("2026-10-21T09:01:00Z", 7), bucket("2026-10-21T09:02:00Z", 8)],
  );

  expect(merged.map((value) => value.averagePowerKw)).toEqual([4, 7, 8]);
});

it("keeps the held buckets when nothing newer is fetched", () => {
  const held = [bucket("2026-10-21T09:00:00Z", 4)];

  expect(mergePowerBuckets(held, [])).toEqual(held);
});
