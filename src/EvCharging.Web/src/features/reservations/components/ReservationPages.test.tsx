import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import { HttpStatusCode } from "axios";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { Reservation } from "../api";
import { ReservationsPage } from "./ReservationsPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));
const failure = (status: number) =>
  Object.assign(new Error("private details"), {
    isAxiosError: true,
    response: { status },
  });

function setup() {
  render(
    <MantineProvider>
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <MemoryRouter initialEntries={["/reservations?afterId=missing"]}>
          <ReservationsPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-10T22:30:00Z"));
  get.mockReset();
});

afterEach(() => vi.useRealTimers());

it("offers the first page for an invalid owner cursor", async () => {
  get.mockRejectedValue(failure(HttpStatusCode.BadRequest));
  setup();
  expect(await screen.findByRole("alert")).toHaveTextContent("first page");
  expect(screen.getByRole("link", { name: "First page" })).toHaveAttribute(
    "href",
    "/reservations",
  );
});

it("groups bookings by server state and links to their stations", async () => {
  const reservation: Reservation = {
    id: "eeb95a7c-6106-44ba-bbd5-2b8884e29623",
    stationId: "5891ade1-a6c1-46b5-8ea9-0c4792220f92",
    stationName: "North Station",
    stationAddress: "1 Synthetic Avenue",
    timeZoneId: "Europe/London",
    connectorId: "751e5f76-830a-42b8-b21a-cc2b8a9071a5",
    chargerName: "Charger 1",
    evseNumber: 1,
    connectorNumber: 1,
    connectorType: "Type2",
    powerSupply: "AC",
    ratedPowerKW: 22,
    startsAt: "2026-10-11T12:30:00Z",
    endsAt: "2026-10-11T13:30:00Z",
    state: "active",
    createdAt: "2024-12-31T12:00:00Z",
  };
  get.mockResolvedValue({
    data: {
      reservations: [
        reservation,
        {
          ...reservation,
          id: "39f5e9a0-ead6-4243-b25d-9426434d4ab1",
          stationName: "South Station",
          state: "cancelled",
          startsAt: "2026-10-13T12:30:00Z",
          endsAt: "2026-10-13T13:30:00Z",
        },
        {
          ...reservation,
          id: "e5db47f0-7a78-472b-9307-fd00d3e11cf8",
          stationName: "West Station",
          state: "expired",
          startsAt: "2026-10-10T22:45:00Z",
          endsAt: "2026-10-11T00:30:00Z",
        },
        {
          ...reservation,
          id: "2c3f8083-76a4-4694-bbdf-f165f4255f12",
          stationName: "East Station",
          state: "consumed",
          startsAt: "2026-10-10T19:00:00Z",
          endsAt: "2026-10-10T20:00:00Z",
        },
      ],
      nextAfterId: null,
    },
  });
  setup();

  const active = within(
    await screen.findByRole("region", { name: "Active reservations" }),
  );
  expect(active.getByRole("link", { name: "North Station" })).toHaveAttribute(
    "href",
    `/stations/${reservation.stationId}`,
  );
  expect(active.getByText("Active reservation")).toBeVisible();
  expect(active.getByText("1 Synthetic Avenue")).toBeVisible();
  expect(active.getByText("Charger 1")).toBeVisible();
  expect(active.getByText("EVSE 1 · Connector 1")).toBeVisible();
  expect(active.getByText("Type 2 · AC · 22 kW")).toBeVisible();
  expect(active.getByText("Tomorrow 13:30 - 14:30")).toBeVisible();
  expect(active.getByText("Station time · Europe/London")).toBeVisible();
  expect(active.queryByText("Expired")).not.toBeInTheDocument();

  const past = within(
    screen.getByRole("region", { name: "Past reservations" }),
  );
  expect(past.getByText("Cancelled")).toBeVisible();
  expect(past.getByText("Expired")).toBeVisible();
  expect(past.getByText("Consumed")).toBeVisible();
  expect(past.getByText("Today 23:45 - Tomorrow 01:30")).toBeVisible();
  expect(past.getByText("Today 20:00 - 21:00")).toBeVisible();
  expect(past.getByText("13 Oct 2026 13:30 - 14:30")).toBeVisible();
  expect(
    past.queryByRole("link", { name: "North Station" }),
  ).not.toBeInTheDocument();
  expect(past.getByRole("link", { name: "South Station" })).toHaveAttribute(
    "href",
    `/stations/${reservation.stationId}`,
  );
});
