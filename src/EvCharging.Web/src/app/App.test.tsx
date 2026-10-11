import { PlatformRole } from "@/features/auth/PlatformRole";
import { authKeys } from "@/features/auth/api";
import { QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { act, render, screen, waitFor } from "@testing-library/react";
import { HttpStatusCode } from "axios";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { createAppQueryClient } from "./query-client";

const { get, put } = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get, put } }));
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
        : path === "/api/stations/assigned"
          ? catalog
          : path.startsWith("/api/stations/")
            ? detail
            : catalog,
  }));
}

function renderApp(path: string) {
  const client = createAppQueryClient();
  client.setDefaultOptions({ queries: { retry: false } });
  render(
    <MantineProvider env="test">
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
    put.mockReset();
    mockSession();
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

  it.each(["signed-out", "signed-in", "unavailable"])(
    "rechecks the session after a protected query returns 401: %s",
    async (outcome) => {
      const driver = {
        signedIn: true,
        user: { name: "Driver", roles: [PlatformRole.Driver] },
        antiforgeryToken: "test",
      };
      const unauthorized = Object.assign(new Error("Unauthorized"), {
        isAxiosError: true,
        response: { status: HttpStatusCode.Unauthorized },
      });
      let sessionRequests = 0;
      get.mockImplementation(async (path: string) => {
        if (path !== "/auth/session") throw unauthorized;
        sessionRequests++;
        if (sessionRequests === 1) return { data: driver };
        if (outcome === "unavailable") throw unauthorized;
        return { data: outcome === "signed-out" ? anonymous : driver };
      });
      renderApp("/reservations");

      if (outcome === "signed-out") {
        expect(
          await screen.findByRole("heading", { name: "Sign in to continue" }),
        ).toBeVisible();
        expect(screen.getByRole("link", { name: "Sign in" })).toBeVisible();
        expect(
          screen.queryByRole("button", { name: "Account menu" }),
        ).toBeNull();
      } else if (outcome === "signed-in") {
        expect(
          await screen.findByText("Sign in again to continue."),
        ).toBeVisible();
        expect(
          screen.getByRole("button", { name: "Account menu" }),
        ).toBeVisible();
      } else {
        expect(await screen.findByText("Account unavailable")).toBeVisible();
        expect(
          screen.queryByRole("button", { name: "Account menu" }),
        ).toBeNull();
      }

      await waitFor(() => expect(sessionRequests).toBe(2));
      expect(
        get.mock.calls.filter(([path]) => path === "/api/reservations"),
      ).toHaveLength(1);
    },
  );

  it("retains an editor draft when a failed save reveals a signed-out session", async () => {
    const operator = {
      signedIn: true,
      user: { name: "Operator", roles: [PlatformRole.Operator] },
      antiforgeryToken: "test",
    };
    let signedOut = false;
    get.mockImplementation(async (path: string) => ({
      data:
        path === "/auth/session"
          ? signedOut
            ? anonymous
            : operator
          : { ...detail, version: 1 },
    }));
    put.mockImplementation(async () => {
      signedOut = true;
      throw Object.assign(new Error("Unauthorized"), {
        isAxiosError: true,
        response: { status: HttpStatusCode.Unauthorized },
      });
    });
    const client = renderApp(`/stations/${detail.id}/edit`);
    const name = await screen.findByLabelText("Station name", { exact: false });
    await userEvent.clear(name);
    await userEvent.type(name, "Unsaved station name");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect(await screen.findByRole("link", { name: "Sign in" })).toBeVisible();
    expect(name).not.toBeVisible();
    expect(name).toHaveValue("Unsaved station name");
    expect(put).toHaveBeenCalledTimes(1);

    signedOut = false;
    await act(() => client.invalidateQueries({ queryKey: authKeys.session }));
    await waitFor(() => expect(name).toBeVisible());
    expect(name).toHaveValue("Unsaved station name");
    expect(put).toHaveBeenCalledTimes(1);
  });
});
