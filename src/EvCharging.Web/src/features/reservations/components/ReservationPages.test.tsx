import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpStatusCode } from "axios";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authKeys } from "@/features/auth/api";
import type { Reservation, ReservationList } from "../api";
import { ReservationsPage } from "./ReservationsPage";

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get, post } }));
const failure = (status: number) =>
  Object.assign(new Error("private details"), {
    isAxiosError: true,
    response: { status },
  });

function setup(
  client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  }),
  createdReservation?: Reservation,
  initialPath = "/reservations",
) {
  const view = render(
    <MantineProvider>
      <QueryClientProvider client={client}>
        <MemoryRouter
          initialEntries={[
            createdReservation
              ? { pathname: "/reservations", state: { createdReservation } }
              : initialPath,
          ]}
        >
          <ReservationsPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { client, ...view };
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-10T22:30:00Z"));
  get.mockReset();
  post.mockReset();
});

afterEach(() => vi.useRealTimers());

it("offers the first page for an invalid owner cursor", async () => {
  get.mockRejectedValue(failure(HttpStatusCode.BadRequest));
  setup(undefined, undefined, "/reservations?afterId=missing");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Request rejected",
  );
  expect(screen.getByRole("link", { name: "First page" })).toHaveAttribute(
    "href",
    "/reservations",
  );
});

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
  cancelledAt: null,
  expiredAt: null,
};

it("groups bookings by server state and links to their stations", async () => {
  get.mockResolvedValue({
    data: {
      reservations: [
        reservation,
        {
          ...reservation,
          id: "39f5e9a0-ead6-4243-b25d-9426434d4ab1",
          stationName: "South Station",
          state: "cancelled",
          startsAt: "2026-10-10T22:45:00Z",
          endsAt: "2026-10-11T00:30:00Z",
          cancelledAt: "2026-10-10T22:15:00Z",
        },
        {
          ...reservation,
          id: "e5db47f0-7a78-472b-9307-fd00d3e11cf8",
          stationName: "West Station",
          state: "expired",
          startsAt: "2026-10-09T12:30:00Z",
          endsAt: "2026-10-09T13:30:00Z",
          expiredAt: "2026-10-09T17:00:00Z",
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
  expect(past.getByText("Cancelled today at 23:15")).toBeVisible();
  expect(past.getByText("Expired yesterday at 18:00")).toBeVisible();
  expect(past.getByText("Consumed")).toBeVisible();
  expect(past.getByText("Today 23:45 - Tomorrow 01:30")).toBeVisible();
  expect(past.getByText("Today 20:00 - 21:00")).toBeVisible();
  expect(past.getByText("9 Oct 2026 13:30 - 14:30")).toBeVisible();
  expect(
    past.queryByRole("link", { name: "North Station" }),
  ).not.toBeInTheDocument();
  expect(past.getByRole("link", { name: "South Station" })).toHaveAttribute(
    "href",
    `/stations/${reservation.stationId}`,
  );
});

it("retains the committed cancellation through stale refreshes and page remounts", async () => {
  const user = userEvent.setup();
  get.mockImplementation(async (url: string) => ({
    data:
      url === "/auth/session"
        ? { signedIn: true, antiforgeryToken: "csrf-token" }
        : { reservations: [reservation], nextAfterId: null },
  }));
  post.mockResolvedValue({
    data: {
      ...reservation,
      state: "cancelled",
      cancelledAt: "2026-10-10T22:30:00Z",
    },
  });
  const { client, unmount } = setup();
  await user.click(
    await screen.findByRole("button", {
      name: "Cancel reservation",
    }),
  );
  let dialog = within(
    await screen.findByRole("dialog", { name: "Cancel reservation?" }),
  );
  await user.click(dialog.getByRole("button", { name: "Keep reservation" }));
  expect(post).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Cancel reservation" }));
  dialog = within(
    await screen.findByRole("dialog", { name: "Cancel reservation?" }),
  );
  await user.click(
    dialog.getByRole("button", { name: "Confirm cancellation" }),
  );
  expect(await screen.findByText("Cancelled")).toBeVisible();
  expect(screen.getByText("Cancelled today at 23:30")).toBeVisible();
  expect(post).toHaveBeenCalledWith(
    `/api/reservations/${reservation.id}/cancel`,
    undefined,
    {
      headers: { "X-CSRF-TOKEN": "csrf-token" },
      signal: undefined,
    },
  );
  expect(screen.queryByText("Active reservation")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Cancel reservation" }),
  ).not.toBeInTheDocument();
  unmount();
  setup(client);
  expect(await screen.findByText("Cancelled")).toBeVisible();
  expect(screen.getByText("Cancelled today at 23:30")).toBeVisible();
  expect(screen.queryByText("Active reservation")).not.toBeInTheDocument();
});

it.each([HttpStatusCode.Conflict, HttpStatusCode.InternalServerError])(
  "keeps the reservation and shows a failure when cancellation returns %s",
  async (status) => {
    const user = userEvent.setup();
    get.mockImplementation(async (url: string) => ({
      data:
        url === "/auth/session"
          ? { signedIn: true, antiforgeryToken: "csrf-token" }
          : { reservations: [reservation], nextAfterId: null },
    }));
    const error = failure(status);
    if (status === HttpStatusCode.Conflict)
      Object.assign(error.response, {
        data: {
          status,
          title: "Reservation cancellation conflict",
          detail:
            "A reservation with a pending or uncertain start cannot be cancelled.",
        },
      });
    post.mockRejectedValue(error);
    setup();
    await user.click(
      await screen.findByRole("button", {
        name: "Cancel reservation",
      }),
    );
    const dialog = within(
      await screen.findByRole("dialog", { name: "Cancel reservation?" }),
    );
    await user.click(
      dialog.getByRole("button", { name: "Confirm cancellation" }),
    );
    expect(await dialog.findByRole("alert")).not.toHaveTextContent(
      "private details",
    );
    if (status === HttpStatusCode.Conflict)
      expect(dialog.getByRole("alert")).toHaveTextContent(
        "A reservation with a pending or uncertain start cannot be cancelled.",
      );
    expect(screen.getByText("Active reservation")).toBeVisible();
    expect(screen.queryByText("Cancelled")).not.toBeInTheDocument();
  },
);

it("preserves cancellation when the initial list response arrives after the mutation", async () => {
  const user = userEvent.setup();
  const saved: Reservation = {
    ...reservation,
    state: "cancelled",
    cancelledAt: "2026-10-10T22:30:00Z",
  };
  let finishInitialRead:
    ((response: { data: ReservationList }) => void) | undefined;
  const initialRead = new Promise<{ data: ReservationList }>((resolve) => {
    finishInitialRead = resolve;
  });
  let listRequests = 0;
  get.mockImplementation(async (url: string) => {
    if (url === "/auth/session")
      return { data: { signedIn: true, antiforgeryToken: "csrf-token" } };
    if (++listRequests === 1) return initialRead;
    return { data: { reservations: [saved], nextAfterId: null } };
  });
  post.mockResolvedValue({ data: saved });
  const { client, unmount } = setup(undefined, reservation);
  await user.click(
    await screen.findByRole("button", { name: "Cancel reservation" }),
  );
  const dialog = within(
    await screen.findByRole("dialog", { name: "Cancel reservation?" }),
  );
  await user.click(
    dialog.getByRole("button", { name: "Confirm cancellation" }),
  );
  expect(await screen.findByText("Cancelled")).toBeVisible();
  await act(async () => {
    finishInitialRead?.({
      data: { reservations: [reservation], nextAfterId: null },
    });
    await initialRead;
  });
  expect(screen.getByText("Cancelled today at 23:30")).toBeVisible();
  expect(screen.queryByText("Active reservation")).not.toBeInTheDocument();
  unmount();
  setup(client);
  expect(await screen.findByText("Cancelled today at 23:30")).toBeVisible();
  expect(screen.queryByText("Active reservation")).not.toBeInTheDocument();
});

it("blocks cancellation after a failed session refresh despite a cached token, and supports retry", async () => {
  const user = userEvent.setup();
  const session = { signedIn: true, antiforgeryToken: "cached-token" };
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  client.setQueryData(authKeys.session, session);
  get.mockImplementation(async (url: string) => {
    if (url === "/auth/session")
      throw failure(HttpStatusCode.ServiceUnavailable);
    return { data: { reservations: [reservation], nextAfterId: null } };
  });
  setup(client);
  await user.click(
    await screen.findByRole("button", { name: "Cancel reservation" }),
  );
  const dialog = within(
    await screen.findByRole("dialog", { name: "Cancel reservation?" }),
  );
  expect(await dialog.findByRole("alert")).toBeVisible();
  expect(
    dialog.getByRole("button", { name: "Confirm cancellation" }),
  ).toBeDisabled();
  expect(post).not.toHaveBeenCalled();

  get.mockResolvedValue({ data: session });
  await user.click(dialog.getByRole("button", { name: "Try again" }));
  expect(
    await dialog.findByRole("button", { name: "Confirm cancellation" }),
  ).toBeEnabled();
});
