import {
  initializeDiagnostics,
  type DiagnosticsSettings,
} from "@/lib/diagnostics/sentry";

try {
  const response = await fetch("/diagnostics/config", {
    credentials: "same-origin",
    signal: AbortSignal.timeout(10000),
  });
  if (response.ok) {
    initializeDiagnostics((await response.json()) as DiagnosticsSettings);
  } else {
    console.warn(
      "Frontend diagnostics configuration unavailable.",
      response.status,
    );
  }
} catch (error) {
  // Optional diagnostics must not prevent application startup.
  console.warn("Frontend diagnostics could not be initialized.", error);
}

await import("@/app/bootstrap");
