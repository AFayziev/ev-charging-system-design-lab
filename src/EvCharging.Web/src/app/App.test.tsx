import { PlatformRole } from "../features/auth/PlatformRole";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("../lib/api", () => ({ api: { get } }));

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
  it("renders the shared driver and operator home", () => {
    renderApp("/");

    expect(
      screen.getByRole("heading", { name: "Welcome to EV Charging" }),
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
    expect(screen.getByRole("link", { name: "Driver area" })).toHaveAttribute(
      "href",
      "/driver",
    );
    expect(
      screen.queryByRole("link", { name: "Operator area" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign out" })).toHaveAttribute(
      "type",
      "submit",
    );
    const logout = screen
      .getByRole("button", { name: "Sign out" })
      .closest("form");
    expect(logout).toHaveAttribute("method", "post");
    expect(logout).toHaveAttribute("action", "/auth/logout?returnUrl=/");
    expect(logout).toHaveFormValues({ __RequestVerificationToken: "test" });
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
      screen.getByRole("heading", { name: "Page not found" }),
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
