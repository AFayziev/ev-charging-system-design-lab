import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, MemoryRouter, RouterProvider } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import { StationListPage } from "./StationListPage";
import { StationDetailPage } from "./StationDetailPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));

function renderScreen() {
  return render(
    <MantineProvider>
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/stations"]}>
          <StationListPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
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
    "Station information unavailable",
  );
  expect(
    screen.queryByText("private database details"),
  ).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(
    await screen.findByRole("heading", { name: "No stations to display" }),
  ).toBeInTheDocument();
});

it("keeps connector selection synchronized with the URL and booking form", async () => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
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
        evses: [1, 2].map((number) => ({
          id: `evse-${number}`,
          number,
          connectors: [
            {
              id: `connector-${number}`,
              number: 1,
              connectorType: "Type2",
              powerSupply: "AC",
              ratedPowerKW: 22,
            },
          ],
        })),
      },
    ],
  };
  get.mockImplementation(async (path: string) => ({
    data:
      path === "/auth/session"
        ? {
            signedIn: true,
            user: { roles: ["driver"] },
            antiforgeryToken: "csrf",
          }
        : station,
  }));
  const router = createMemoryRouter(
    [{ path: "/stations/:stationId", element: <StationDetailPage /> }],
    {
      initialEntries: [
        "/stations/station?source=catalog&connectorId=connector-2",
      ],
    },
  );
  render(
    <MantineProvider env="test">
      <QueryClientProvider client={new QueryClient()}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  const second = await screen.findByRole("radio", { name: /EVSE 2/ });
  expect(second).toBeChecked();
  expect(
    await screen.findByRole("combobox", { name: "Connector" }),
  ).toHaveValue("EVSE 2 · Connector 1 (Type 2, 22 kW)");
  await userEvent.click(screen.getByRole("radio", { name: /EVSE 1/ }));
  expect(screen.getByRole("combobox", { name: "Connector" })).toHaveValue(
    "EVSE 1 · Connector 1 (Type 2, 22 kW)",
  );
  expect(router.state.location.search).toBe(
    "?source=catalog&connectorId=connector-1",
  );

  await userEvent.keyboard("{ArrowDown}");
  expect(second).toBeChecked();
  expect(screen.getByRole("combobox", { name: "Connector" })).toHaveValue(
    "EVSE 2 · Connector 1 (Type 2, 22 kW)",
  );
  expect(router.state.location.search).toBe(
    "?source=catalog&connectorId=connector-2",
  );
});
