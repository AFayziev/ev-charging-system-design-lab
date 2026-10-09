import * as Sentry from "@sentry/react";
import { afterEach, expect, it, vi } from "vitest";
import { initializeDiagnostics } from "./sentry";
afterEach(async () => {
  await Sentry.close(100);
  vi.unstubAllGlobals();
});
it("excludes URL secrets and explicitly supplied private data from error reports", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response("", { status: 200 }));
  vi.stubGlobal("fetch", fetch);
  initializeDiagnostics({
    enabled: true,
    dsn: "https://public@o0.ingest.sentry.io/1",
    environment: "test",
    release: "test-build",
    errorSampleRate: 1,
    traceSampleRate: 0,
    logLevels: [],
    replayEnabled: false,
  });
  Sentry.addBreadcrumb({
    category: "fetch",
    data: {
      url: "/api/example?token=query-secret#fragment-secret",
      status_code: 503,
    },
  });
  Sentry.addBreadcrumb({
    category: "navigation",
    data: { from: "/?token=query-secret", to: "/driver#fragment-secret" },
  });
  Sentry.captureException(new Error("Synthetic failure"), {
    extra: { token: "private-value" },
    user: { email: "private@example.test" },
  });
  await Sentry.flush(1000);
  const body = fetch.mock.calls.map((call) => String(call[1]?.body)).join("\n");
  expect(body).toContain("Synthetic failure");
  expect(body).toContain("/api/example");
  expect(body).toContain("/driver");
  expect(body).not.toContain("query-secret");
  expect(body).not.toContain("fragment-secret");
  expect(body).not.toContain("private-value");
  expect(body).not.toContain("private@example.test");
});
