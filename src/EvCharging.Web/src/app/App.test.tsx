import { PlatformRole } from "@/features/auth/PlatformRole";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get } }));

function renderApp(path: string) {
  return render(
    <MantineProvider>
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe("application shell", () => {
  beforeEach(() => {
    get.mockReset().mockResolvedValue({
      data: { signedIn: false, user: null, antiforgeryToken: "test" },
    });
  });
  it("renders the shared driver and operator home", async () => {
    renderApp("/");

    expect(
      await screen.findByRole("heading", { name: "Welcome to EV Charging" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("A shared workspace for drivers and operators."),
    ).toBeInTheDocument();
  });

  it("shows role navigation and denies the other area", async () => {
    get.mockResolvedValue({
      data: {
        signedIn: true,
        user: { name: "Driver", roles: [PlatformRole.Driver] },
        antiforgeryToken: "test",
      },
    });
    renderApp("/operator");
    expect(
      await screen.findByRole("heading", { name: "Access denied" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Stations" })).toHaveAttribute(
      "href",
      "/stations",
    );
    expect(
      screen.queryByRole("link", { name: "Operator area" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("menuitem", { name: "Sign out" }),
    ).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Account menu" }));
    const signOut = await screen.findByRole("menuitem", { name: "Sign out" });
    expect(signOut).toHaveAttribute("type", "submit");
    const logout = signOut.closest("form");
    expect(logout).toHaveAttribute("method", "post");
    expect(logout).toHaveAttribute("action", "/auth/logout?returnUrl=/");
    expect(logout).toHaveFormValues({ __RequestVerificationToken: "test" });
  });

  it.each([
    { roles: [PlatformRole.Driver], heading: "Find a station" },
    { roles: [PlatformRole.Operator], heading: "Operator area" },
    {
      roles: [PlatformRole.Driver, PlatformRole.Operator],
      heading: "Find a station",
    },
  ])("opens the home workspace for $roles", async ({ roles, heading }) => {
    get.mockImplementation(async (path: string) => ({
      data:
        path === "/auth/session"
          ? {
              signedIn: true,
              user: { name: "User", roles },
              antiforgeryToken: "test",
            }
          : { stations: [], nextAfterId: null },
    }));
    renderApp("/");

    expect(
      await screen.findByRole("heading", { name: heading }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Welcome to EV Charging" }),
    ).not.toBeInTheDocument();
  });

  it("preserves the requested station URL when signing in", async () => {
    renderApp(
      "/stations/709af89a-8fda-4724-a188-1e38aac99dd3?source=catalog#equipment",
    );

    expect(
      await screen.findByRole("link", { name: "Sign in" }),
    ).toHaveAttribute(
      "href",
      "/auth/login?returnUrl=%2Fstations%2F709af89a-8fda-4724-a188-1e38aac99dd3%3Fsource%3Dcatalog%23equipment",
    );
  });

  it("retries failed session discovery without exposing transport details", async () => {
    get.mockRejectedValueOnce(new Error("private transport details"));
    const user = userEvent.setup();
    renderApp("/");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Unable to check your session.",
    );
    expect(
      screen.queryByRole("link", { name: "Sign in" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("private transport details"),
    ).not.toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(
      await screen.findByRole("link", { name: "Sign in" }),
    ).toHaveAttribute("href", "/auth/login?returnUrl=%2F");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("returns home from an unknown route", async () => {
    const user = userEvent.setup();
    renderApp("/missing-page");

    expect(
      await screen.findByRole("heading", { name: "Page not found" }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("link", { name: "Return home" }));

    expect(
      screen.getByRole("heading", { name: "Welcome to EV Charging" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Page not found" }),
    ).not.toBeInTheDocument();
  });
});
