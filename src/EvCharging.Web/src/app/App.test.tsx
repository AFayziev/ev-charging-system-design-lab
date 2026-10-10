import { PlatformRole } from "@/features/auth/PlatformRole";
import { authKeys } from "@/features/auth/api";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));
const anonymous = { signedIn: false, user: null, antiforgeryToken: "test" };
const catalog = { stations: [], nextAfterId: null };
const detail = {
  id: "709af89a-8fda-4724-a188-1e38aac99dd3",
  name: "Demo Station",
  address: "1 Synthetic Avenue",
  latitude: 51.51,
  longitude: -0.12,
  timeZoneId: "Europe/London",
  accessInstructions: "Fictional lab site.",
  chargers: [],
};

function mockSession(session: unknown = anonymous) {
  get.mockImplementation(async (path: string) => ({
    data:
      path === "/auth/session"
        ? session
        : path.startsWith("/api/stations/")
          ? detail
          : catalog,
  }));
}

function renderApp(path: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <MantineProvider>
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return client;
}

describe("application shell", () => {
  beforeEach(() => {
    get.mockReset();
    mockSession();
  });

  it.each([
    { roles: [] },
    { roles: [PlatformRole.Driver] },
    { roles: [PlatformRole.Operator] },
    { roles: [PlatformRole.Driver, PlatformRole.Operator] },
  ])("opens the public catalogue from home for $roles", async ({ roles }) => {
    if (roles.length)
      mockSession({
        signedIn: true,
        user: { name: "User", roles },
        antiforgeryToken: "test",
      });
    renderApp("/");
    expect(
      await screen.findByRole("heading", { name: "Find a station" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Stations" })).toHaveAttribute(
      "href",
      "/stations",
    );
  });

  it("renders public browsing while session discovery is pending", async () => {
    get.mockImplementation((path: string) =>
      path === "/auth/session"
        ? new Promise(() => {})
        : Promise.resolve({ data: catalog }),
    );
    renderApp("/");
    expect(
      await screen.findByRole("heading", { name: "No stations to display" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("status", { name: "Loading account" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Account menu" }),
    ).not.toBeInTheDocument();
  });

  it("keeps public browsing usable when session discovery fails and retries", async () => {
    get.mockImplementation((path: string) =>
      path === "/auth/session"
        ? Promise.reject(new Error("private transport details"))
        : Promise.resolve({ data: catalog }),
    );
    renderApp("/stations");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Account unavailable",
    );
    expect(
      await screen.findByRole("heading", { name: "No stations to display" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Sign in" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("private transport details"),
    ).not.toBeInTheDocument();
    mockSession();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(
      await screen.findByRole("link", { name: "Sign in" }),
    ).toHaveAttribute("href", "/auth/login?returnUrl=%2Fstations");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("preserves pathname, query and hash when signing in", async () => {
    renderApp(
      "/stations/709af89a-8fda-4724-a188-1e38aac99dd3?source=catalog#equipment",
    );
    expect(
      await screen.findByRole("link", { name: "Sign in" }),
    ).toHaveAttribute(
      "href",
      "/auth/login?returnUrl=%2Fstations%2F709af89a-8fda-4724-a188-1e38aac99dd3%3Fsource%3Dcatalog%23equipment",
    );
    expect(screen.getAllByRole("link", { name: "Sign in" })).toHaveLength(1);
  });

  it("requires sign-in for the operator area", async () => {
    renderApp("/operator");
    expect(
      await screen.findByRole("heading", { name: "Sign in to continue" }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Sign in" })).toHaveLength(1);
    expect(
      screen.queryByRole("link", { name: "Operator area" }),
    ).not.toBeInTheDocument();
  });

  it("denies the driver access to the operator area", async () => {
    mockSession({
      signedIn: true,
      user: { name: "Driver", roles: [PlatformRole.Driver] },
      antiforgeryToken: "test",
    });
    renderApp("/operator");
    expect(
      await screen.findByRole("heading", { name: "Access denied" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Operator area" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Sign in" }),
    ).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Account menu" }));
    const form = (
      await screen.findByRole("menuitem", { name: "Sign out" })
    ).closest("form");
    expect(form).toHaveAttribute("method", "post");
    expect(form).toHaveAttribute(
      "action",
      "/auth/logout?returnUrl=%2Fstations",
    );
    expect(form).toHaveFormValues({ __RequestVerificationToken: "test" });
  });

  it.each([
    {
      path: "/stations/709af89a-8fda-4724-a188-1e38aac99dd3?source=catalog#equipment",
      heading: "Demo Station",
      action:
        "/auth/logout?returnUrl=%2Fstations%2F709af89a-8fda-4724-a188-1e38aac99dd3%3Fsource%3Dcatalog%23equipment",
    },
    {
      path: "/stations/?source=catalog#equipment",
      heading: "No stations to display",
      action:
        "/auth/logout?returnUrl=%2Fstations%2F%3Fsource%3Dcatalog%23equipment",
    },
  ])(
    "preserves public URL $path when signing out",
    async ({ path, action, heading }) => {
      mockSession({
        signedIn: true,
        user: { name: "Operator", roles: [PlatformRole.Operator] },
        antiforgeryToken: "test",
      });
      renderApp(path);
      expect(
        await screen.findByRole("heading", { name: heading }),
      ).toBeInTheDocument();
      await userEvent.click(
        await screen.findByRole("button", { name: "Account menu" }),
      );
      expect(
        (await screen.findByRole("menuitem", { name: "Sign out" })).closest(
          "form",
        ),
      ).toHaveAttribute("action", action);
    },
  );

  it("removes stale role access after session refetch fails", async () => {
    mockSession({
      signedIn: true,
      user: { name: "Operator", roles: [PlatformRole.Operator] },
      antiforgeryToken: "test",
    });
    const client = renderApp("/operator");
    expect(
      await screen.findByRole("heading", { name: "Operator area" }),
    ).toBeInTheDocument();
    get.mockRejectedValue(new Error("session unavailable"));
    await act(() => client.invalidateQueries({ queryKey: authKeys.session }));
    expect(
      await screen.findByText("Unable to check your session."),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Operator area" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Operator area" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Account menu" }),
    ).not.toBeInTheDocument();
  });

  it("returns to the public catalogue from an unknown route", async () => {
    renderApp("/missing-page");
    expect(
      await screen.findByRole("heading", { name: "Page not found" }),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole("link", { name: "Return home" }));
    expect(
      await screen.findByRole("heading", { name: "Find a station" }),
    ).toBeInTheDocument();
  });
});
