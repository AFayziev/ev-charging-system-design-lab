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
  it.each(["network", "http", "json", "initialization"])(
    "starts the application after optional diagnostics %s failure",
    async (failure) => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockImplementation(async () => {
          if (failure === "network") throw new TypeError("Offline");
          return {
            ok: failure !== "http",
            status: failure === "http" ? 503 : 200,
            json: async () => {
              if (failure === "json") throw new SyntaxError("Invalid config");
              return { enabled: true };
            },
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
  it("initializes diagnostics before application modules are evaluated", async () => {
    const settings = { enabled: true };
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ ok: true, json: async () => settings }),
    );
    await import("./main");
    expect(initializeDiagnostics).toHaveBeenCalledWith(settings);
    expect(initializeDiagnostics.mock.invocationCallOrder[0]).toBeLessThan(
      bootstrap.mock.invocationCallOrder[0]!,
    );
    expect(console.warn).not.toHaveBeenCalled();
  });
});
