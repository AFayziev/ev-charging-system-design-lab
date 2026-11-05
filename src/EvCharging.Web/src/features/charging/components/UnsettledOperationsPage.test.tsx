import { TestProviders, createTestQueryClient } from "@/test/providers";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import type { UnsettledOperation } from "../api";
import { UnsettledOperationsPage } from "./UnsettledOperationsPage";

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock("@/lib/api", () => ({ api: { get, post } }));

const uncertainStart: UnsettledOperation = {
  id: "0b0c4b4e-2a4e-4bc5-a2a6-1c6b0e7d1a11",
  kind: "start",
  outcome: "uncertain",
  acceptedAt: "2026-10-11T12:31:00Z",
  sessionId: "9880d294-e32c-4d8d-9c03-05bb7a9b8327",
  equipment: {
    stationName: "North Station",
    chargerName: "Charger 1",
    connectorNumber: 2,
  },
  lastRequestedAt: "2026-10-11T12:31:01Z",
  hasObservations: false,
  releasableAt: "2026-10-11T12:41:01Z",
  canRelease: true,
  attempts: [
    {
      number: 1,
      authorizedAt: "2026-10-11T12:31:01Z",
      finishedAt: "2026-10-11T12:31:04Z",
      outcome: "uncertain",
      failureCode: "provider_transport_failure",
      responseStatusCode: null,
      integrationKey: "wattharbor-primary",
      providerStationId: "wh-north",
      providerChargerId: "wh-north-1",
      providerConnectorId: "wh-north-1-2",
      providerCommandId: null,
      providerSessionId: null,
    },
  ],
};

let operations: UnsettledOperation[] = [];

beforeEach(() => {
  get.mockReset();
  post.mockReset();
  operations = [uncertainStart];
  get.mockImplementation(async (url: string) => ({
    data:
      url === "/auth/session"
        ? { signedIn: true, antiforgeryToken: "csrf-token" }
        : { operations, nextAfterId: null },
  }));
});

function setup() {
  return render(
    <TestProviders client={createTestQueryClient()}>
      <MemoryRouter>
        <UnsettledOperationsPage />
      </MemoryRouter>
    </TestProviders>,
  );
}

const releaseLabel =
  "Release connector at North Station · Charger 1 · Connector 2";

it("shows each attempt with its provider identifiers", async () => {
  setup();

  const card = await screen.findByRole("region", {
    name: "start at North Station · Charger 1 · Connector 2",
  });
  const row = within(card).getAllByRole("row")[1];
  expect(row).toHaveTextContent("wattharbor-primary");
  expect(row).toHaveTextContent("wh-north / wh-north-1 / wh-north-1-2");
  expect(row).toHaveTextContent("uncertain · provider_transport_failure");
});

it("releases a releasable start after confirmation and reads the list again", async () => {
  const user = userEvent.setup();
  post.mockImplementation(async () => {
    operations = [];
    return {
      data: { id: uncertainStart.id, releasedAt: "2026-10-11T12:42:00Z" },
    };
  });
  setup();

  await user.click(await screen.findByRole("button", { name: releaseLabel }));
  const dialog = await screen.findByRole("dialog", {
    name: "Release connector?",
  });
  await user.click(
    within(dialog).getByRole("button", { name: "Release connector" }),
  );

  expect(post).toHaveBeenCalledExactlyOnceWith(
    `/api/charging-operations/${uncertainStart.id}/release`,
    undefined,
    { headers: { "X-CSRF-TOKEN": "csrf-token" } },
  );
  expect(await screen.findByText("No unsettled operations")).toBeVisible();
  await waitFor(() =>
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
  );
});

it("shows why a release was refused and reads the list again", async () => {
  const user = userEvent.setup();
  post.mockImplementation(async () => {
    operations = [
      { ...uncertainStart, hasObservations: true, canRelease: false },
    ];
    throw Object.assign(new Error("conflict"), {
      isAxiosError: true,
      response: {
        status: 409,
        data: {
          title: "Charging start release refused",
          detail:
            "The provider has sent observations for this session, so the start is not released.",
        },
      },
    });
  });
  setup();

  await user.click(await screen.findByRole("button", { name: releaseLabel }));
  const dialog = await screen.findByRole("dialog", {
    name: "Release connector?",
  });
  await user.click(
    within(dialog).getByRole("button", { name: "Release connector" }),
  );

  expect(await within(dialog).findByRole("alert")).toHaveTextContent(
    "The provider has sent observations for this session",
  );
  await user.click(
    within(dialog).getByRole("button", { name: "Keep waiting" }),
  );
  expect(
    await screen.findByText(/has sent readings for this session/),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: releaseLabel }),
  ).not.toBeInTheDocument();
});

it("offers no release before the release window ends", async () => {
  operations = [{ ...uncertainStart, canRelease: false }];
  setup();

  expect(
    await screen.findByText(/The connector can be released from/),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: releaseLabel }),
  ).not.toBeInTheDocument();
});
