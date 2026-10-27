import { TestProviders, createTestQueryClient } from "@/test/providers";
import type { QueryClient } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, HttpStatusCode } from "axios";
import { createMemoryRouter, MemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { stationKeys } from "../api";
import { StationListPage } from "./StationListPage";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));

const searchPath = "/api/search/stations";
const optionsPath = "/api/search/stations/options";

function station(name: string, distanceKm: number | null = null) {
  return {
    id: name.toLowerCase().replaceAll(" ", "-"),
    name,
    address: "1 Synthetic Avenue",
    chargerCount: 1,
    connectorCount: 1,
    capabilities: [
      { connectorType: "Type2", powerSupply: "AC", ratedPowerKW: 22 },
    ],
    distanceKm,
  };
}

const browseStation = { ...station("Browse Station"), distanceKm: undefined };

function problem(status: number, code?: string) {
  return new AxiosError("failed", "ERR_BAD_RESPONSE", undefined, undefined, {
    status,
    data: { code },
    statusText: "",
    headers: {},
    config: {} as never,
  });
}

function searchCalls() {
  return get.mock.calls.filter(([path]) => path === searchPath);
}

function lastSearchParams() {
  return searchCalls().at(-1)?.[1]?.params;
}

const availabilityPath = "/api/stations/availability";

// Each station asked about has one connector per flag, available or not.
const okAvailability = (
  ids: readonly string[],
  connectorFlags: readonly boolean[] = [true],
) => ({
  data: {
    stations: ids.map((id) => ({
      id,
      connectors: connectorFlags.map((available, index) => ({
        id: `${id}-connector-${index}`,
        available,
      })),
    })),
  },
});

// Resolves once an availability read has failed, so what is asserted next is what stays after the failure.
async function availabilityFailed(client: QueryClient) {
  await waitFor(() =>
    expect(
      client
        .getQueryCache()
        .findAll({ queryKey: stationKeys.availabilityLists })
        .some((read) => read.state.status === "error"),
    ).toBe(true),
  );
}

function availabilityCalls() {
  return get.mock.calls.filter(([path]) => path === availabilityPath);
}

function respond(handlers: Partial<Record<string, () => unknown>>) {
  get.mockImplementation(
    async (path: string, config?: { params?: { ids?: string[] } }) => {
      const handler =
        handlers[path] ??
        (path === availabilityPath
          ? () => okAvailability(config?.params?.ids ?? [])
          : undefined);
      if (!handler) throw new Error(`Unexpected request ${path}`);
      return handler();
    },
  );
}

const okBrowse = () => ({
  data: { stations: [browseStation], nextAfterId: null },
});
const okOptions = () => ({
  data: {
    connectorTypes: ["CCS2", "CHAdeMO", "Type2"],
    powerSupplies: ["AC", "DC"],
    ratedPowersKw: [7.4, 22, 150],
  },
});
const okSearch =
  (...stations: ReturnType<typeof station>[]) =>
  () => ({
    data: {
      stations,
      nextCursor: null,
      total: stations.length,
      totalIsExact: true,
    },
  });

function renderCatalog(path = "/stations") {
  const client = createTestQueryClient();
  render(
    <TestProviders client={client} env="test">
      <MemoryRouter initialEntries={[path]}>
        <StationListPage />
      </MemoryRouter>
    </TestProviders>,
  );
  return client;
}

function stubGeolocation(
  outcome: { latitude: number; longitude: number } | { code: number },
) {
  vi.stubGlobal("navigator", {
    ...navigator,
    geolocation: {
      getCurrentPosition: (
        success: (position: unknown) => void,
        failure: (error: unknown) => void,
      ) =>
        "code" in outcome
          ? failure({ code: outcome.code, PERMISSION_DENIED: 1 })
          : success({ coords: outcome }),
    },
  });
}

beforeEach(() => {
  get.mockReset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

it("browses without searching until something is searched", async () => {
  respond({ "/api/stations": okBrowse });
  renderCatalog();

  expect(await screen.findByText("Browse Station")).toBeInTheDocument();
  expect(searchCalls()).toHaveLength(0);
});

it("searches the settled text, not every keystroke", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("North Station")),
  });
  renderCatalog();
  await screen.findByText("Browse Station");

  await userEvent.type(
    screen.getByRole("searchbox", { name: "Search stations" }),
    "north",
  );
  expect(searchCalls()).toHaveLength(0);

  expect(await screen.findByText("North Station")).toBeInTheDocument();
  expect(searchCalls()).toHaveLength(1);
  expect(lastSearchParams()).toMatchObject({ q: "north" });
  expect(
    screen.getByText("1 station · best matches first"),
  ).toBeInTheDocument();
});

it("offers exactly the filters the index reports and searches with the chosen ones", async () => {
  respond({
    "/api/stations": okBrowse,
    [optionsPath]: okOptions,
    [searchPath]: okSearch(station("Chademo Point")),
  });
  renderCatalog();
  await screen.findByText("Browse Station");

  await userEvent.click(screen.getByRole("button", { name: "Filters" }));
  await userEvent.click(
    await screen.findByRole("checkbox", { name: "CHAdeMO" }),
  );

  expect(await screen.findByText("Chademo Point")).toBeInTheDocument();
  expect(lastSearchParams()).toMatchObject({ connectorType: ["CHAdeMO"] });
  expect(
    screen.getByRole("button", { name: "Filters, 1 active" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("checkbox", { name: "DC" })).toBeInTheDocument();
});

it("searches near the driver with the default radius and shows distances", async () => {
  stubGeolocation({ latitude: 51.5, longitude: -0.12 });
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("Near Station", 1.24)),
  });
  renderCatalog();
  await screen.findByText("Browse Station");

  await userEvent.click(screen.getByRole("button", { name: "Near me" }));

  expect(await screen.findByText("Near Station")).toBeInTheDocument();
  expect(lastSearchParams()).toMatchObject({
    lat: 51.5,
    lon: -0.12,
    radiusKm: 10,
  });
  expect(screen.getByText("1.2 km")).toBeInTheDocument();
  expect(
    screen.getByText("1 station within 10 km · nearest first"),
  ).toBeInTheDocument();
});

it("explains blocked location and lists stations by name instead", async () => {
  stubGeolocation({ code: 1 });
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("Alpha Station")),
  });
  renderCatalog();
  await screen.findByText("Browse Station");

  await userEvent.click(screen.getByRole("button", { name: "Near me" }));

  expect(await screen.findByText("Alpha Station")).toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent("Location is blocked");
  expect(lastSearchParams().lat).toBeUndefined();
  expect(screen.getByText("1 station · sorted by name")).toBeInTheDocument();
});

it("shows no matches and clears the search", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(),
  });
  renderCatalog("/stations?q=nothing");

  expect(
    await screen.findByRole("heading", { name: "No stations match" }),
  ).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Clear search" }));

  expect(await screen.findByText("Browse Station")).toBeInTheDocument();
  expect(
    screen.getByRole("searchbox", { name: "Search stations" }),
  ).toHaveValue("");
});

it.each([
  [HttpStatusCode.ServiceUnavailable, "search-unavailable"],
  [HttpStatusCode.BadGateway, undefined],
  [HttpStatusCode.GatewayTimeout, undefined],
])(
  "falls back to the full catalog when search answers %s",
  async (status, code) => {
    respond({
      "/api/stations": okBrowse,
      [searchPath]: () => {
        throw problem(status, code);
      },
    });
    renderCatalog("/stations?q=north");

    expect(await screen.findByText("Browse Station")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Search unavailable");

    await userEvent.click(screen.getByRole("button", { name: "Retry search" }));
    await waitFor(() => expect(searchCalls()).toHaveLength(2));
  },
);

it("shows other search failures as errors without falling back", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: () => {
      throw problem(HttpStatusCode.InternalServerError);
    },
  });
  renderCatalog("/stations?q=north");

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Information unavailable",
  );
  expect(screen.queryByText("Browse Station")).not.toBeInTheDocument();
});

it("shows a notice instead of filters when the options are unavailable", async () => {
  respond({
    "/api/stations": okBrowse,
    [optionsPath]: () => {
      throw problem(HttpStatusCode.ServiceUnavailable, "search-unavailable");
    },
  });
  renderCatalog();
  await screen.findByText("Browse Station");

  await userEvent.click(screen.getByRole("button", { name: "Filters" }));

  expect(await screen.findByText("Filters unavailable")).toBeInTheDocument();
  expect(screen.getByText("Browse Station")).toBeInTheDocument();
});

it("continues a search with its cursor alone", async () => {
  get.mockImplementation(
    async (path: string, config?: { params?: object & { ids?: string[] } }) => {
      if (path === availabilityPath)
        return okAvailability(config?.params?.ids ?? []);
      if (path === "/api/stations") return okBrowse();
      return config?.params && "cursor" in config.params
        ? {
            data: {
              stations: [station("Second Page")],
              nextCursor: null,
              total: 2,
              totalIsExact: true,
            },
          }
        : {
            data: {
              stations: [station("First Page Station")],
              nextCursor: "opaque-cursor",
              total: 2,
              totalIsExact: true,
            },
          };
    },
  );
  renderCatalog("/stations?q=north&type=Type2");

  await userEvent.click(
    await screen.findByRole("button", { name: "Next stations" }),
  );

  expect(await screen.findByText("Second Page")).toBeInTheDocument();
  expect(lastSearchParams()).toEqual({ cursor: "opaque-cursor" });
  await userEvent.click(screen.getByRole("button", { name: "First page" }));
  expect(await screen.findByText("First Page Station")).toBeInTheDocument();
});

it("clears the search text with its button", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("North Station")),
  });
  renderCatalog("/stations?q=north");
  await screen.findByText("North Station");

  await userEvent.click(screen.getByRole("button", { name: "Clear text" }));

  expect(await screen.findByText("Browse Station")).toBeInTheDocument();
  expect(
    screen.getByRole("searchbox", { name: "Search stations" }),
  ).toHaveValue("");
  expect(
    screen.queryByRole("button", { name: "Clear text" }),
  ).not.toBeInTheDocument();
});

it("shows a lower-bound total and plural counts", async () => {
  // Any count the API reports as a lower bound; the page does not know the API's cap.
  const lowerBound = 12345;
  get.mockImplementation(
    async (path: string, config?: { params?: { ids?: string[] } }) =>
      path === availabilityPath
        ? okAvailability(config?.params?.ids ?? [])
        : path === "/api/stations"
          ? okBrowse()
          : {
              data: {
                stations: [station("North Station")],
                nextCursor: "next",
                total: lowerBound,
                totalIsExact: false,
              },
            },
  );
  renderCatalog("/stations?q=north");

  expect(
    await screen.findByText(
      `${new Intl.NumberFormat().format(lowerBound)}+ stations · best matches first`,
    ),
  ).toBeInTheDocument();
});

it("keeps the search when paging the fallback catalog", async () => {
  get.mockImplementation(
    async (path: string, config?: { params?: { ids?: string[] } }) => {
      if (path === availabilityPath)
        return okAvailability(config?.params?.ids ?? []);
      if (path === searchPath)
        throw problem(HttpStatusCode.ServiceUnavailable, "search-unavailable");
      return {
        data: { stations: [browseStation], nextAfterId: "after-1" },
      };
    },
  );
  renderCatalog("/stations?q=north&type=Type2");

  await userEvent.click(
    await screen.findByRole("button", { name: "Next stations" }),
  );

  expect(
    screen.getByRole("searchbox", { name: "Search stations" }),
  ).toHaveValue("north");
  await waitFor(() =>
    expect(get).toHaveBeenCalledWith(
      "/api/stations",
      expect.objectContaining({ params: { afterId: "after-1" } }),
    ),
  );
  expect(
    screen.getByRole("button", { name: "Filters, 1 active" }),
  ).toBeInTheDocument();
});

it("shows the text of a URL that changed from outside", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("North Station")),
  });
  const router = createMemoryRouter(
    [{ path: "/stations", element: <StationListPage /> }],
    { initialEntries: ["/stations?q=abc"] },
  );
  render(
    <TestProviders client={createTestQueryClient()} env="test">
      <RouterProvider router={router} />
    </TestProviders>,
  );
  await screen.findByText("North Station");

  await router.navigate("/stations?q=xyz");

  await waitFor(() =>
    expect(
      screen.getByRole("searchbox", { name: "Search stations" }),
    ).toHaveValue("xyz"),
  );
});

it("shows availability for browsed stations, asking for the listed ids", async () => {
  respond({ "/api/stations": okBrowse });
  renderCatalog();

  expect(await screen.findByText("1 of 1 available")).toBeInTheDocument();
  expect(availabilityCalls().at(-1)?.[1]).toMatchObject({
    params: { ids: ["browse-station"] },
  });
});

it("shows availability for search results", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: okSearch(station("North Station")),
  });
  renderCatalog("/stations?q=north");

  expect(await screen.findByText("1 of 1 available")).toBeInTheDocument();
  expect(availabilityCalls().at(-1)?.[1]).toMatchObject({
    params: { ids: ["north-station"] },
  });
});

it("still shows availability when search falls back to the catalog", async () => {
  respond({
    "/api/stations": okBrowse,
    [searchPath]: () => {
      throw problem(HttpStatusCode.ServiceUnavailable, "search-unavailable");
    },
  });
  renderCatalog("/stations?q=north");

  expect(await screen.findByText("1 of 1 available")).toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent("Search unavailable");
});

it("lists the stations without counts when availability fails", async () => {
  respond({
    "/api/stations": okBrowse,
    [availabilityPath]: () => {
      throw problem(HttpStatusCode.InternalServerError);
    },
  });
  const client = renderCatalog();

  expect(await screen.findByText("Browse Station")).toBeInTheDocument();
  await availabilityFailed(client);
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.queryByText(/\d+ of \d+ available/)).not.toBeInTheDocument();
});

it.each([
  [[true, false], "1 of 2 available"],
  [[false], "0 of 1 available"],
])(
  "counts the available connectors of a station: %j",
  async (connectorFlags, text) => {
    respond({
      "/api/stations": okBrowse,
      [availabilityPath]: () =>
        okAvailability([browseStation.id], connectorFlags),
    });
    renderCatalog();

    expect(await screen.findByText(text)).toBeInTheDocument();
  },
);
