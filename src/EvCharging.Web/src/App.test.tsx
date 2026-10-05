import { MantineProvider } from "@mantine/core";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { App } from "./App";

function renderApp(path: string) {
  return render(
    <MantineProvider>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe("application shell", () => {
  it("renders the shared driver and operator home", () => {
    renderApp("/");

    expect(
      screen.getByRole("heading", { name: "Welcome to EV Charging" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("A shared workspace for drivers and operators."),
    ).toBeInTheDocument();
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
