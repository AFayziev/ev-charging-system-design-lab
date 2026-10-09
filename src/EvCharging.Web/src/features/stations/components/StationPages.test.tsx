import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
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
    response: { status: 404 },
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
