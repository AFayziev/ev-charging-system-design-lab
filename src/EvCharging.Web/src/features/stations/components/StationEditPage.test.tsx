import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import { CanceledError, HttpStatusCode } from "axios";
import { StationEditPage } from "./StationEditPage";
import { stationKeys } from "../api";

const { get, put } = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get, put } }));
const station = {
  id: "709af89a-8fda-4724-a188-1e38aac99dd3",
  version: 123,
  name: "North Station",
  address: "1 Synthetic Avenue",
  accessInstructions: "North entrance",
};
const failure = (status: number, data?: unknown) =>
  Object.assign(new Error("private details"), {
    isAxiosError: true,
    response: { status, data },
  });

function renderEditor(cached?: typeof station) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  if (cached) client.setQueryData(stationKeys.editor(station.id), cached);
  render(
    <MantineProvider>
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[`/stations/${station.id}/edit`]}>
          <Routes>
            <Route
              path="/stations/:stationId/edit"
              element={<StationEditPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return client;
}

beforeEach(() => {
  get.mockReset();
  put.mockReset();
  get.mockImplementation((url: string) =>
    Promise.resolve({
      data:
        url === "/auth/session"
          ? {
              signedIn: true,
              antiforgeryToken: "csrf",
              user: { roles: ["operator"] },
            }
          : station,
    }),
  );
});

it("waits for the writer fetch when revisiting a cached editor", async () => {
  let complete!: (value: unknown) => void;
  get.mockImplementation((url: string) =>
    url === "/auth/session"
      ? Promise.resolve({ data: { signedIn: true, antiforgeryToken: "csrf" } })
      : new Promise((resolve) => {
          complete = resolve;
        }),
  );
  renderEditor(station);
  expect(
    screen.queryByLabelText("Station name", { exact: false }),
  ).not.toBeInTheDocument();
  complete({ data: { ...station, version: 999, name: "Latest station" } });
  expect(
    await screen.findByLabelText("Station name", { exact: false }),
  ).toHaveValue("Latest station");
  put.mockResolvedValue({
    data: { ...station, version: 1000, name: "Latest station" },
  });
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() =>
    expect(put).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ version: 999 }),
      expect.any(Object),
    ),
  );
});

it.each([
  {
    status: HttpStatusCode.Unauthorized,
    title: "Authentication failed",
    blocksSave: true,
  },
  {
    status: HttpStatusCode.Forbidden,
    title: "Access denied",
    blocksSave: true,
  },
  {
    status: HttpStatusCode.NotFound,
    title: "Station not found",
    blocksSave: true,
  },
  {
    status: HttpStatusCode.InternalServerError,
    title: "Station information unavailable",
    blocksSave: false,
  },
])(
  "retains a draft after a $status refresh failure",
  async ({ status, title, blocksSave }) => {
    put.mockRejectedValue(failure(HttpStatusCode.Conflict));
    renderEditor();
    const name = await screen.findByLabelText("Station name", { exact: false });
    await userEvent.clear(name);
    await userEvent.type(name, "My draft");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Station changed",
    );
    get.mockRejectedValue(failure(status));
    await userEvent.click(
      screen.getByRole("button", { name: "Reload current station" }),
    );
    expect(
      await screen.findByText("Your draft has been retained."),
    ).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(title);
    expect(screen.getByLabelText("Station name", { exact: false })).toHaveValue(
      "My draft",
    );
    expect(screen.queryByText("Station changed")).not.toBeInTheDocument();
    expect(screen.queryByText("Changes saved.")).not.toBeInTheDocument();
    await waitFor(() => {
      const save = screen.getByRole("button", { name: "Save changes" });
      if (blocksSave) expect(save).toBeDisabled();
      else expect(save).not.toBeDisabled();
    });
    if (blocksSave) {
      await userEvent.click(
        screen.getByRole("button", { name: "Save changes" }),
      );
      expect(put).toHaveBeenCalledTimes(1);
    }
    expect(
      screen.queryByRole("link", { name: "Sign in" }),
    ).not.toBeInTheDocument();
  },
);

it.each([
  { status: HttpStatusCode.Unauthorized, title: "Authentication failed" },
  { status: HttpStatusCode.Forbidden, title: "Access denied" },
  { status: HttpStatusCode.NotFound, title: "Station not found" },
  {
    status: HttpStatusCode.InternalServerError,
    title: "Station information unavailable",
  },
])(
  "shows a $status initial load failure without a form",
  async ({ status, title }) => {
    get.mockRejectedValue(failure(status));
    renderEditor();
    expect(await screen.findByRole("alert")).toHaveTextContent(title);
    expect(
      screen.queryByLabelText("Station name", { exact: false }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("Your draft has been retained."),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Assigned stations" }),
    ).toHaveAttribute("href", "/operator");
    expect(screen.queryByText("private details")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Sign in" }),
    ).not.toBeInTheDocument();
    get.mockResolvedValue({ data: station });
    await userEvent.click(
      screen.getByRole("button", { name: "Reload current station" }),
    );
    expect(
      await screen.findByLabelText("Station name", { exact: false }),
    ).toHaveValue(station.name);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  },
);

it("restores saving with the writer version after access recovers", async () => {
  put.mockRejectedValue(failure(HttpStatusCode.Conflict));
  renderEditor();
  const name = await screen.findByLabelText("Station name", { exact: false });
  await userEvent.clear(name);
  await userEvent.type(name, "Retained draft");
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await screen.findByText("Station changed");
  get.mockRejectedValue(failure(HttpStatusCode.Forbidden));
  await userEvent.click(
    screen.getByRole("button", { name: "Reload current station" }),
  );
  await screen.findByText("Your draft has been retained.");
  expect(name).toHaveValue("Retained draft");
  expect(screen.getByRole("button", { name: "Save changes" })).toBeDisabled();

  const current = { ...station, name: "Current station", version: 125 };
  get.mockResolvedValue({ data: current });
  put.mockResolvedValue({ data: { ...current, version: 126 } });
  await userEvent.click(
    screen.getByRole("button", { name: "Reload current station" }),
  );
  await waitFor(() => {
    expect(screen.getByLabelText("Station name", { exact: false })).toHaveValue(
      current.name,
    );
    expect(
      screen.getByRole("button", { name: "Save changes" }),
    ).not.toBeDisabled();
  });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await screen.findByText("Changes saved.");
  expect(put).toHaveBeenLastCalledWith(
    expect.any(String),
    expect.objectContaining({ version: current.version }),
    expect.any(Object),
  );
});

it("uses the confirmed writer response and disables inputs while saving", async () => {
  let complete!: (value: unknown) => void;
  put.mockImplementation(
    () =>
      new Promise((resolve) => {
        complete = resolve;
      }),
  );
  const client = renderEditor();
  const name = await screen.findByLabelText("Station name", { exact: false });
  await userEvent.clear(name);
  await userEvent.type(name, " Draft name ");
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() => expect(name).toBeDisabled());
  const saved = { ...station, name: "Draft name", version: 124 };
  complete({ data: saved });
  expect(await screen.findByText("Changes saved.")).toBeInTheDocument();
  const savedName = screen.getByLabelText("Station name", { exact: false });
  expect(savedName).toHaveValue("Draft name");
  expect(client.getQueryData(stationKeys.editor(station.id))).toEqual(saved);
  expect(put).toHaveBeenCalledWith(
    `/api/stations/${station.id}`,
    {
      version: 123,
      name: " Draft name ",
      address: station.address,
      accessInstructions: station.accessInstructions,
    },
    expect.objectContaining({ headers: { "X-CSRF-TOKEN": "csrf" } }),
  );
  expect(get.mock.calls.filter(([url]) => url.endsWith("/edit"))).toHaveLength(
    1,
  );
  await userEvent.type(savedName, " changed");
  expect(screen.queryByText("Changes saved.")).not.toBeInTheDocument();
});

it.each([
  {
    error: failure(HttpStatusCode.Unauthorized),
    title: "Authentication failed",
    recovery: "Reload current station",
  },
  {
    error: failure(HttpStatusCode.Forbidden),
    title: "Access denied",
    recovery: undefined,
  },
  {
    error: failure(HttpStatusCode.BadRequest),
    title: "Request rejected",
    recovery: undefined,
  },
  {
    error: failure(HttpStatusCode.NotFound),
    title: "Station not found",
    recovery: undefined,
  },
  {
    error: Object.assign(new Error("private network details"), {
      isAxiosError: true,
      code: "ERR_NETWORK",
    }),
    title: "Changes not confirmed",
    recovery: "Reload current station",
  },
])("retains the draft and shows $title", async ({ error, title, recovery }) => {
  put.mockRejectedValue(error);
  renderEditor();
  const name = await screen.findByLabelText("Station name", { exact: false });
  await userEvent.clear(name);
  await userEvent.type(name, "Retained draft");
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(title);
  expect(name).toHaveValue("Retained draft");
  expect(screen.queryByText(/private .* details/)).not.toBeInTheDocument();
  expect(screen.queryByText("Changes saved.")).not.toBeInTheDocument();
  expect(put).toHaveBeenCalledTimes(1);
  if (recovery) {
    expect(screen.getByRole("button", { name: recovery })).toBeInTheDocument();
  } else {
    expect(
      screen.queryByRole("button", { name: "Reload current station" }),
    ).not.toBeInTheDocument();
  }
});

it("shows returned field errors without discarding the draft", async () => {
  put.mockRejectedValue(
    failure(HttpStatusCode.BadRequest, {
      errors: { Name: ["Choose a different station name."] },
    }),
  );
  renderEditor();
  const name = await screen.findByLabelText("Station name", { exact: false });
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Review the fields",
  );
  expect(
    screen.getByText("Choose a different station name."),
  ).toBeInTheDocument();
  expect(name).toHaveValue(station.name);
  expect(name).toHaveAttribute("aria-invalid", "true");
  expect(
    screen.queryByRole("button", { name: "Reload current station" }),
  ).not.toBeInTheDocument();
});

it("keeps a canceled save quiet and retains the draft", async () => {
  put.mockRejectedValue(new CanceledError());
  renderEditor();
  const name = await screen.findByLabelText("Station name", { exact: false });
  await userEvent.clear(name);
  await userEvent.type(name, "Canceled draft");
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() =>
    expect(
      screen.getByRole("button", { name: "Save changes" }),
    ).not.toBeDisabled(),
  );
  expect(name).toHaveValue("Canceled draft");
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.queryByText("Changes saved.")).not.toBeInTheDocument();
  expect(put).toHaveBeenCalledTimes(1);
});
