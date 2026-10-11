import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
const { initializeDiagnostics, bootstrap } = vi.hoisted(() => ({
  initializeDiagnostics: vi.fn(),
  bootstrap: vi.fn(),
}));
vi.mock("@/lib/diagnostics/sentry", () => ({ initializeDiagnostics }));
beforeEach(() => {
  vi.spyOn(console, "warn").mockImplementation(() => {});
  vi.doMock("@/app/bootstrap", () => {
    bootstrap();
    return {};
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.resetModules();
  vi.clearAllMocks();
});
describe("application startup", () => {
  it.each(["network", "initialization"])(
    "starts the application after optional diagnostics %s failure",
    async (failure) => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockImplementation(async () => {
          if (failure === "network") throw new TypeError("Offline");
          return {
            ok: true,
            status: 200,
            json: async () => ({ enabled: true }),
          };
        }),
      );
      if (failure === "initialization")
        initializeDiagnostics.mockImplementationOnce(() => {
          throw new Error("SDK unavailable");
        });
      await import("./main");
      expect(bootstrap).toHaveBeenCalledOnce();
      expect(console.warn).toHaveBeenCalledOnce();
    },
  );
});
