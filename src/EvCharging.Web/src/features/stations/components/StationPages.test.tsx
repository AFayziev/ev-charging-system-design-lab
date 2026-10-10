import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import { HttpStatusCode } from "axios";
import { StationListPage } from "./StationListPage";
import { StationDetailPage } from "./StationDetailPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));

function renderScreen(path = "/stations") {
  return render(
    <MantineProvider>
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/stations" element={<StationListPage />} />
            <Route
              path="/stations/:stationId"
              element={<StationDetailPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

beforeEach(() => {
  get.mockReset();
});

it("shows loading then an empty catalog", async () => {
  let finish!: (value: unknown) => void;
  get.mockReturnValue(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  renderScreen();
  expect(screen.getByRole("status")).toHaveTextContent(
    "Loading station information",
  );
  finish({ data: { stations: [], nextAfterId: null } });
  expect(
    await screen.findByRole("heading", { name: "No stations to display" }),
  ).toBeInTheDocument();
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

it("distinguishes a missing station from transport failure", async () => {
  const missing = Object.assign(new Error("missing"), {
    isAxiosError: true,
    response: { status: HttpStatusCode.NotFound },
  });
  get.mockRejectedValue(missing);
  renderScreen("/stations/709af89a-8fda-4724-a188-1e38aac99dd3");
  expect(
    await screen.findByRole("heading", { name: "Station not found" }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Try again" }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "All stations" })).toHaveAttribute(
    "href",
    "/stations",
  );
});

it("shows connector capability, maximum rated power and equipment counts", async () => {
  get.mockResolvedValue({
    data: {
      stations: [
        {
          id: "709af89a-8fda-4724-a188-1e38aac99dd3",
          name: "Demo Yard",
          address: "14 Mock Lane",
          chargerCount: 2,
          evseCount: 4,
          connectorCount: 4,
          capabilities: [
            { connectorType: "CCS2", powerSupply: "DC", ratedPowerKW: 150 },
          ],
        },
      ],
      nextAfterId: null,
    },
  });
  renderScreen();
  expect(
    await screen.findByRole("heading", { name: "Demo Yard" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Up to 150 kW rated power")).toBeInTheDocument();
  expect(screen.getByText("CCS2 · DC · 150 kW")).toBeInTheDocument();
  expect(
    screen.getByText("2 chargers · 4 EVSEs · 4 connectors"),
  ).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "View Demo Yard" })).toHaveAttribute(
    "href",
    "/stations/709af89a-8fda-4724-a188-1e38aac99dd3",
  );
});

it("uses coordinates for maps and scoped equipment numbers for labels", async () => {
  get.mockResolvedValue({
    data: {
      id: "709af89a-8fda-4724-a188-1e38aac99dd3",
      name: "Demo Yard",
      address: "14 Mock Lane",
      latitude: 51.5168,
      longitude: -0.1284,
      timeZoneId: "Europe/London",
      accessInstructions: "Fictional site.",
      chargers: [
        {
          id: "charger-public-id",
          name: "Charger 1",
          evses: [
            {
              id: "evse-public-id",
              number: 2,
              connectors: [
                {
                  id: "connector-public-id",
                  number: 1,
                  connectorType: "Type2",
                  powerSupply: "AC",
                  ratedPowerKW: 11,
                },
              ],
            },
          ],
        },
      ],
    },
  });
  renderScreen("/stations/709af89a-8fda-4724-a188-1e38aac99dd3");
  const maps = await screen.findByRole("link", { name: "Open in maps" });
  expect(maps).toHaveAttribute(
    "href",
    "https://www.google.com/maps/search/?api=1&query=51.5168%2C-0.1284",
  );
  expect(maps).toHaveAttribute("rel", "noopener noreferrer");
  expect(screen.getByText("EVSE 2 · Connector 1")).toBeInTheDocument();
  expect(screen.queryByText("evse-public-id")).not.toBeInTheDocument();
});
