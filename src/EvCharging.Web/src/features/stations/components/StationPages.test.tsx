import { TestProviders, createTestQueryClient } from "@/test/providers";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, MemoryRouter, RouterProvider } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import { stationKeys } from "../api";
import { StationListPage } from "./StationListPage";
import { StationDetailPage } from "./StationDetailPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));

function renderScreen() {
  return render(
    <TestProviders client={createTestQueryClient()}>
      <MemoryRouter initialEntries={["/stations"]}>
        <StationListPage />
      </MemoryRouter>
    </TestProviders>,
  );
}

beforeEach(() => {
  get.mockReset();
});

it("shows a safe failure and retries the query", async () => {
  get
    .mockRejectedValueOnce(new Error("private database details"))
    .mockResolvedValue({ data: { stations: [], nextAfterId: null } });
  renderScreen();
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Information unavailable",
  );
  expect(
    screen.queryByText("private database details"),
  ).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(
    await screen.findByRole("heading", { name: "No stations to display" }),
  ).toBeInTheDocument();
});

const detailStation = {
  id: "station",
  name: "North Station",
  address: "1 Synthetic Avenue",
  latitude: 51.51,
  longitude: -0.12,
  timeZoneId: "Europe/London",
  accessInstructions: "North entrance",
  chargers: [
    {
      id: "charger",
      name: "Charger 1",
      providerName: "WattHarbor",
      connectors: [1, 2].map((number) => ({
        id: `connector-${number}`,
        number,
        connectorType: "Type2",
        powerSupply: "AC",
        ratedPowerKW: 22,
      })),
    },
  ],
};

function renderDetail() {
  const client = createTestQueryClient();
  const router = createMemoryRouter(
    [{ path: "/stations/:stationId", element: <StationDetailPage /> }],
    { initialEntries: ["/stations/station"] },
  );
  render(
    <TestProviders client={client} env="test">
      <RouterProvider router={router} />
    </TestProviders>,
  );
  return client;
}

it("marks each connector available or unavailable", async () => {
  get.mockImplementation(async (path: string) => {
    if (path === "/api/stations/availability")
      return {
        data: {
          stations: [
            {
              id: "station",
              connectors: [
                { id: "connector-1", available: true },
                { id: "connector-2", available: false },
              ],
            },
          ],
        },
      };
    if (path === "/auth/session") return { data: { signedIn: false } };
    return { data: detailStation };
  });
  renderDetail();

  const first = await screen.findByRole("radio", { name: /Connector 1/ });
  const second = screen.getByRole("radio", { name: /Connector 2/ });
  await waitFor(() => expect(first).toHaveTextContent("Available"));
  expect(second).toHaveTextContent("Unavailable");
  expect(get).toHaveBeenCalledWith(
    "/api/stations/availability",
    expect.objectContaining({ params: { ids: ["station"] } }),
  );
});

it("shows the station without availability when it cannot be read", async () => {
  get.mockImplementation(async (path: string) => {
    if (path === "/api/stations/availability") throw new Error("unavailable");
    if (path === "/auth/session") return { data: { signedIn: false } };
    return { data: detailStation };
  });
  const client = renderDetail();

  expect(
    await screen.findByRole("radio", { name: /Connector 1/ }),
  ).toBeInTheDocument();
  // Only once the read has failed does an absent badge or alert say anything.
  await waitFor(() =>
    expect(
      client.getQueryState(stationKeys.availability(["station"]))?.status,
    ).toBe("error"),
  );
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.queryByText("Unavailable")).not.toBeInTheDocument();
});

it("keeps connector selection synchronized with the URL and booking form", async () => {
  const station = {
    id: "station",
    name: "North Station",
    address: "1 Synthetic Avenue",
    latitude: 51.51,
    longitude: -0.12,
    timeZoneId: "Europe/London",
    accessInstructions: "North entrance",
    chargers: [
      {
        id: "charger",
        name: "Charger 1",
        providerName: "WattHarbor",
        connectors: [1, 2].map((number) => ({
          id: `connector-${number}`,
          number,
          connectorType: "Type2",
          powerSupply: "AC",
          ratedPowerKW: 22,
        })),
      },
    ],
  };
  get.mockImplementation(async (path: string) => {
    if (path === "/auth/session")
      return {
        data: {
          signedIn: true,
          user: { roles: ["driver"] },
          antiforgeryToken: "csrf",
        },
      };
    if (path === "/api/reservations/booking-policy")
      return {
        data: {
          maximumOutstandingReservationsPerDriver: 2,
          maximumBookingDurationSeconds: 3600,
          maximumAdvanceBookingHorizonSeconds: 86400,
        },
      };
    return { data: station };
  });
  const router = createMemoryRouter(
    [{ path: "/stations/:stationId", element: <StationDetailPage /> }],
    {
      initialEntries: [
        "/stations/station?source=catalog&connectorId=connector-2",
      ],
    },
  );
  render(
    <TestProviders client={createTestQueryClient()} env="test">
      <RouterProvider router={router} />
    </TestProviders>,
  );

  const second = await screen.findByRole("radio", { name: /Connector 2/ });
  expect(second).toBeChecked();
  expect(
    await screen.findByRole("combobox", { name: "Connector" }),
  ).toHaveValue("Connector 2 (Type 2, 22 kW)");
  await userEvent.click(screen.getByRole("radio", { name: /Connector 1/ }));
  expect(screen.getByRole("combobox", { name: "Connector" })).toHaveValue(
    "Connector 1 (Type 2, 22 kW)",
  );
  expect(router.state.location.search).toBe(
    "?source=catalog&connectorId=connector-1",
  );

  await userEvent.keyboard("{ArrowDown}");
  expect(second).toBeChecked();
  expect(screen.getByRole("combobox", { name: "Connector" })).toHaveValue(
    "Connector 2 (Type 2, 22 kW)",
  );
  expect(router.state.location.search).toBe(
    "?source=catalog&connectorId=connector-2",
  );
});
