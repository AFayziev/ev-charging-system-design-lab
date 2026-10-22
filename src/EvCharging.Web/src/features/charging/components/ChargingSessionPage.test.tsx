import { TestProviders, createTestQueryClient } from "@/test/providers";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import {
  chargingKeys,
  type ChargingPower,
  type ChargingProgress,
  type ChargingReading,
} from "../api";
import { ChargingSessionPage } from "./ChargingSessionPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));
// The chart measures its container, which jsdom cannot; the data it receives is what matters here.
vi.mock("@mantine/charts", () => ({
  AreaChart: ({ data }: { data: readonly unknown[] }) => (
    <div role="img" aria-label="Power chart">{`${data.length} points`}</div>
  ),
}));

const sessionId = "6b5b8a56-3f8d-4a58-8e0c-0e7e0f4f5a11";
const now = new Date("2026-10-21T09:10:00Z");
const reading: ChargingReading = {
  state: "charging",
  powerKw: 7.25,
  energyKWh: 1.5,
  isFinalEnergy: false,
  occurredAt: "2026-10-21T09:09:58Z",
};
const progress = (
  overrides: Partial<ChargingProgress> = {},
): ChargingProgress => ({
  sessionId,
  reservationId: "9d3f2e57-4c56-4f0e-8a5b-7f0d5c9a1b22",
  lifecycle: "active",
  startedAt: "2026-10-21T09:00:00Z",
  reading,
  isStale: false,
  isSilentPastLimit: false,
  elapsedSeconds: 600,
  ...overrides,
});
const power = (...starts: string[]): ChargingPower => ({
  buckets: starts.map((startsAt) => ({
    startsAt,
    averagePowerKw: 7,
    maximumPowerKw: 8,
    lastEnergyKWh: 1,
  })),
});

function serve(current: ChargingProgress, buckets: ChargingPower = power()) {
  get.mockImplementation((path: string) => {
    if (path.endsWith("/progress")) return Promise.resolve({ data: current });
    if (path.endsWith("/power")) return Promise.resolve({ data: buckets });
    return Promise.resolve({
      data: {
        stationName: "North Station",
        chargerName: "Charger 1",
        connectorNumber: 2,
      },
    });
  });
}

function setup() {
  const client = createTestQueryClient();
  render(
    <TestProviders client={client}>
      <MemoryRouter initialEntries={[`/charging-sessions/${sessionId}`]}>
        <Routes>
          <Route
            path="/charging-sessions/:sessionId"
            element={<ChargingSessionPage />}
          />
        </Routes>
      </MemoryRouter>
    </TestProviders>,
  );
  return client;
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(now);
  get.mockReset();
});

it("shows energy, power and elapsed time of a fresh session", async () => {
  serve(progress());
  setup();

  expect(await screen.findByText("1.50 kWh")).toBeInTheDocument();
  expect(screen.getByText("7.3 kW")).toBeInTheDocument();
  expect(screen.getByText("0:10:00")).toBeInTheDocument();
  expect(screen.getByText("Charging")).toBeInTheDocument();
  expect(
    await screen.findByText("North Station · Charger 1, connector 2"),
  ).toBeInTheDocument();
});

it("shows the last report with its source time when the reading is stale", async () => {
  serve(progress({ isStale: true }));
  setup();

  expect(await screen.findByText("Last reported charging")).toBeInTheDocument();
  expect(screen.getByText(/Last update \d\d:\d\d:\d\d/)).toBeInTheDocument();
  expect(screen.queryByText(/stopped|offline/i)).not.toBeInTheDocument();
});

it("waits for the first reading of a session that has none", async () => {
  serve(progress({ reading: null }));
  setup();

  expect(
    await screen.findByText("Waiting for charger data"),
  ).toBeInTheDocument();
  expect(screen.getAllByText("–")).toHaveLength(2);
});

it("keeps the last reading visible while the session is stopping", async () => {
  serve(progress({ lifecycle: "stopping" }));
  setup();

  expect(await screen.findByText("Stopping charging")).toBeInTheDocument();
  expect(screen.getByText("1.50 kWh")).toBeInTheDocument();
});

it("pauses updates of a session silent for too long and offers a refresh", async () => {
  serve(progress({ isStale: true, isSilentPastLimit: true }));
  setup();

  expect(await screen.findByText("Updates paused")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Refresh" })).toBeInTheDocument();
});

it("shows the elapsed time the server reports, not one taken from the browser clock", async () => {
  serve(progress({ startedAt: "2020-01-01T00:00:00Z", elapsedSeconds: 125 }));
  setup();

  expect(await screen.findByText("0:02:05")).toBeInTheDocument();
});

it("shows no elapsed time when the server does not know it", async () => {
  serve(progress({ lifecycle: "failed", reading: null, elapsedSeconds: null }));
  setup();

  expect(await screen.findAllByText("–")).toHaveLength(3);
});

it("asks for the power history from the last bucket it holds", async () => {
  serve(progress(), power("2026-10-21T09:00:00Z", "2026-10-21T09:01:00Z"));
  const client = setup();
  expect(await screen.findByText("2 points")).toBeInTheDocument();

  await client.refetchQueries({ queryKey: chargingKeys.power(sessionId) });

  const requests = get.mock.calls.filter(([path]) =>
    String(path).endsWith("/power"),
  );
  expect(requests[0][1].params).toBeUndefined();
  expect(requests[1][1].params).toEqual({ from: "2026-10-21T09:01:00Z" });
});

it("reports a missing session", async () => {
  get.mockRejectedValue(
    Object.assign(new Error("private details"), {
      isAxiosError: true,
      response: { status: 404 },
    }),
  );
  setup();

  expect(
    await screen.findByText("Charging session not found"),
  ).toBeInTheDocument();
});
