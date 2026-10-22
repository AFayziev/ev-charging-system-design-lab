import { TestProviders, createTestQueryClient } from "@/test/providers";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  createMemoryRouter,
  RouterProvider,
  type Location,
} from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { BookingForm } from "./BookingForm";
import { reservationKeys, type BookingPolicy, type Reservation } from "../api";
import { ReservationsPage } from "./ReservationsPage";

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get, post } }));
const station = {
  id: "station",
  name: "North Station",
  address: "Synthetic",
  latitude: 0,
  longitude: 0,
  timeZoneId: "Europe/London",
  accessInstructions: "",
  chargers: [
    {
      id: "charger",
      name: "Charger",
      providerName: "WattHarbor",
      connectors: [
        {
          id: "connector",
          number: 1,
          connectorType: "Type2",
          powerSupply: "AC",
          ratedPowerKW: 22,
        },
      ],
    },
  ],
};
const failure = (status: number, data?: unknown) =>
  Object.assign(new Error("private details"), {
    isAxiosError: true,
    response: { status, data },
  });

const policy: BookingPolicy = {
  maximumOutstandingReservationsPerDriver: 3,
  maximumBookingDurationSeconds: 2 * 60 * 60,
  maximumAdvanceBookingHorizonSeconds: 14 * 24 * 60 * 60,
};
const session = {
  signedIn: true,
  antiforgeryToken: "csrf",
  user: { roles: ["driver"] },
};

function respond(path: string) {
  if (path === "/auth/session") return { data: session };
  if (path === "/api/reservations/booking-policy") return { data: policy };
  return { data: { reservations: [], nextAfterId: null } };
}

function setup(initialEntry: string | Partial<Location> = "/") {
  const client = createTestQueryClient();
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <BookingForm station={station} connectorId="connector" />,
      },
      { path: "/reservations", element: <ReservationsPage /> },
    ],
    { initialEntries: [initialEntry] },
  );
  const view = render(
    <TestProviders client={client} env="test">
      <RouterProvider router={router} />
    </TestProviders>,
  );
  return { router, client, ...view };
}

async function fill() {
  for (const [label, hours] of [
    ["Start", "10"],
    ["End", "11"],
  ]) {
    await userEvent.click(
      await screen.findByRole("button", { name: `${label} time` }),
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "10 January 2029" }),
    );
    fireEvent.change(
      screen.getByRole("spinbutton", { name: `${label} hours` }),
      {
        target: { value: hours },
      },
    );
    fireEvent.change(
      screen.getByRole("spinbutton", { name: `${label} minutes` }),
      {
        target: { value: "00" },
      },
    );
    await userEvent.click(
      screen.getByRole("button", {
        name: `Confirm ${label.toLowerCase()} time`,
      }),
    );
  }
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2029, 0, 2, 10, 0));
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  get.mockReset();
  post.mockReset();
  get.mockImplementation(async (path: string) => respond(path));
});

afterEach(() => vi.useRealTimers());

it("bounds picker choices and restores the selected date and time", async () => {
  setup();
  await userEvent.click(
    await screen.findByRole("button", { name: "Start time" }),
  );
  expect(
    await screen.findByRole("button", { name: "1 January 2029" }),
  ).toBeDisabled();
  expect(
    screen.getByRole("button", { name: "2 January 2029" }),
  ).toHaveAttribute("data-selected", "true");
  await userEvent.click(
    screen.getByRole("spinbutton", { name: "Start hours" }),
  );
  expect(await screen.findByRole("button", { name: "10:01" })).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "09:45" }),
  ).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "10:15" }));
  await userEvent.click(
    screen.getByRole("button", { name: "10 January 2029" }),
  );
  await userEvent.click(
    screen.getByRole("button", { name: "Confirm start time" }),
  );
  await userEvent.click(screen.getByRole("button", { name: "Start time" }));
  expect(
    await screen.findByRole("button", { name: "10 January 2029" }),
  ).toHaveAttribute("data-selected", "true");
  expect(screen.getByRole("spinbutton", { name: "Start hours" })).toHaveValue(
    "10",
  );
  expect(screen.getByRole("spinbutton", { name: "Start minutes" })).toHaveValue(
    "15",
  );
});

it("shows the created booking once and clears feedback on reload", async () => {
  const saved: Reservation = {
    id: "saved",
    stationId: station.id,
    stationName: "Authoritative name",
    stationAddress: "1 Synthetic Avenue",
    timeZoneId: station.timeZoneId,
    connectorId: "connector",
    chargerName: "Charger",
    connectorNumber: 1,
    connectorType: "Type2",
    powerSupply: "AC",
    ratedPowerKW: 22,
    startsAt: "2029-01-10T05:00:00Z",
    endsAt: "2029-01-10T06:00:00Z",
    state: "active",
    createdAt: "2029-01-02T10:00:00Z",
    cancelledAt: null,
    expiredAt: null,
    arrivalDeadline: "2029-01-10T05:10:00Z",
    startOperation: null,
    stopOperation: null,
  };
  get.mockImplementation(async (path: string) => ({
    data:
      path === "/api/reservations"
        ? { reservations: [], nextAfterId: "older-booking" }
        : respond(path).data,
  }));
  post.mockResolvedValue({ data: saved });
  const view = setup();
  await fill();
  expect(screen.getByText("Your time · Asia/Tashkent")).toBeVisible();
  await waitFor(() =>
    expect(
      screen.getByRole("button", { name: "Reserve connector" }),
    ).toBeEnabled(),
  );
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(await screen.findByText("Reservation created.")).toHaveTextContent(
    "Reservation created.",
  );
  expect(
    await screen.findByRole("article", {
      name: "Authoritative name reservation",
    }),
  ).toHaveAttribute("data-highlighted", "true");
  expect(
    screen.queryByRole("heading", { name: "No reservations yet" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("link", { name: "Authoritative name" }),
  ).toHaveAttribute("href", "/stations/station");
  expect(screen.getByText("10 Jan 2029 10:00 - 11:00")).toBeVisible();
  expect(screen.getByText("Your time · Asia/Tashkent")).toBeVisible();
  expect(post).toHaveBeenCalledWith(
    "/api/reservations",
    {
      connectorId: "connector",
      startsAt: new Date("2029-01-10T10:00").toISOString(),
      endsAt: new Date("2029-01-10T11:00").toISOString(),
    },
    expect.objectContaining({ headers: { "X-CSRF-TOKEN": "csrf" } }),
  );
  get.mockResolvedValue({ data: { reservations: [saved], nextAfterId: null } });
  const currentEntry = view.router.state.location;
  view.unmount();
  setup(currentEntry);
  expect(
    await screen.findByRole("article", {
      name: "Authoritative name reservation",
    }),
  ).not.toHaveAttribute("data-highlighted", "true");
  expect(screen.queryByText("Reservation created.")).not.toBeInTheDocument();
});

it("places server validation errors on fields", async () => {
  post.mockRejectedValue(
    failure(400, { errors: { EndsAt: ["The slot has ended."] } }),
  );
  setup();
  await waitFor(() =>
    expect(
      screen.getByRole("button", { name: "Reserve connector" }),
    ).toBeEnabled(),
  );
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(screen.getByText("Choose a start time.")).toBeVisible();
  expect(screen.getByText("Choose an end time.")).toBeVisible();
  expect(post).not.toHaveBeenCalled();
  await fill();
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(await screen.findByText("The slot has ended.")).toBeVisible();
  expect(screen.getByLabelText("End time", { exact: false })).toHaveTextContent(
    "10 Jan 2029, 11:00",
  );
});

it("displays backend validation for an elapsed booking draft", async () => {
  post.mockRejectedValue(
    failure(400, {
      errors: { StartsAt: ["The start must not be in the past."] },
    }),
  );
  setup();
  await fill();
  vi.setSystemTime(new Date("2029-01-11T00:00:00Z"));
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(
    await screen.findByText("The start must not be in the past."),
  ).toBeVisible();
  expect(post).toHaveBeenCalledWith(
    "/api/reservations",
    {
      connectorId: "connector",
      startsAt: new Date("2029-01-10T10:00").toISOString(),
      endsAt: new Date("2029-01-10T11:00").toISOString(),
    },
    expect.objectContaining({ headers: { "X-CSRF-TOKEN": "csrf" } }),
  );
  expect(screen.getByRole("button", { name: "Start time" })).toHaveTextContent(
    "10 Jan 2029, 10:00",
  );
});

it("applies refreshed policy limits without losing the booking draft", async () => {
  const { client } = setup();
  await fill();
  expect(
    screen.getByRole("region", { name: "Booking limits" }),
  ).toHaveTextContent("Maximum slot duration: 2 hours");
  get.mockImplementation(async (path: string) =>
    path === "/api/reservations/booking-policy"
      ? { data: { ...policy, maximumBookingDurationSeconds: 30 * 60 } }
      : respond(path),
  );
  await client.invalidateQueries({ queryKey: reservationKeys.bookingPolicy });
  await screen.findByText(/30 minutes/);
  expect(screen.getByRole("button", { name: "End time" })).toHaveTextContent(
    "10 Jan 2029, 11:00",
  );
  expect(post).not.toHaveBeenCalled();

  get.mockImplementation(async (path: string) =>
    path === "/api/reservations/booking-policy"
      ? {
          data: {
            ...policy,
            maximumAdvanceBookingHorizonSeconds: 24 * 60 * 60,
          },
        }
      : respond(path),
  );
  await client.invalidateQueries({ queryKey: reservationKeys.bookingPolicy });
  await screen.findByText(/up to 1 day/);
  await userEvent.click(screen.getByRole("button", { name: "Start time" }));
  expect(
    await screen.findByRole("button", { name: "10 January 2029" }),
  ).toBeDisabled();
  expect(post).not.toHaveBeenCalled();
});

it("retains the draft while policy loading fails and recovers on retry", async () => {
  const { client } = setup();
  await fill();
  get.mockImplementation(async (path: string) => {
    if (path === "/api/reservations/booking-policy") throw failure(503);
    return respond(path);
  });
  await client.invalidateQueries({ queryKey: reservationKeys.bookingPolicy });
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Information unavailable",
  );
  expect(
    screen.getByRole("button", { name: "Reserve connector" }),
  ).toBeDisabled();
  expect(screen.getByRole("button", { name: "End time" })).toHaveTextContent(
    "10 Jan 2029, 11:00",
  );
  get.mockImplementation(async (path: string) => respond(path));
  await userEvent.click(screen.getByRole("button", { name: "Try again" }));
  await waitFor(() =>
    expect(screen.queryByRole("alert")).not.toBeInTheDocument(),
  );
  expect(
    screen.getByRole("button", { name: "Reserve connector" }),
  ).toBeEnabled();
  expect(screen.getByRole("button", { name: "End time" })).toHaveTextContent(
    "10 Jan 2029, 11:00",
  );
});

it("shows the admission conflict reason returned by the backend", async () => {
  post.mockRejectedValue(
    failure(409, {
      title: "Booking limit reached",
      detail: "You can have at most 3 outstanding reservations.",
    }),
  );
  setup();
  await fill();
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "You can have at most 3 outstanding reservations.",
  );
});

it("keeps an unconfirmed booking draft and does not display diagnostic response details", async () => {
  post.mockRejectedValue(
    failure(500, {
      title: "private framework details",
      detail: "private database details",
      errors: { StartsAt: ["private validation details"] },
    }),
  );
  setup();
  await fill();
  await userEvent.click(
    screen.getByRole("button", { name: "Reserve connector" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Changes not confirmed",
  );
  expect(screen.getByRole("link", { name: "My reservations" })).toBeVisible();
  expect(screen.getByRole("button", { name: "Start time" })).toHaveTextContent(
    "10 Jan 2029, 10:00",
  );
  expect(screen.queryByText(/private .* details/)).not.toBeInTheDocument();
  expect(post).toHaveBeenCalledTimes(1);
});
