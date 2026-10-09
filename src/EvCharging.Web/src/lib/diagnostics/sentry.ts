import * as Sentry from "@sentry/react";
import { reactRouterBrowserTracingIntegration } from "@sentry/react/react-router";

export interface DiagnosticsSettings {
  enabled: boolean;
  dsn: string | null;
  environment: string | null;
  release: string | null;
  errorSampleRate: number;
  traceSampleRate: number;
  logLevels: Sentry.LogSeverityLevel[];
  replayEnabled: boolean;
}

export function initializeDiagnostics(settings: DiagnosticsSettings) {
  if (!settings.enabled) return;
  Sentry.init({
    dsn: settings.dsn ?? undefined,
    environment: settings.environment ?? undefined,
    release: settings.release ?? undefined,
    tunnel: "/diagnostics/sentry",
    sampleRate: settings.errorSampleRate,
    tracesSampleRate: settings.traceSampleRate,
    dataCollection: {
      userInfo: false,
      cookies: false,
      httpHeaders: false,
      httpBodies: [],
      urlQueryParams: false,
      stackFrameVariables: false,
    },
    integrations: [
      reactRouterBrowserTracingIntegration(),
      ...(settings.replayEnabled
        ? [Sentry.replayIntegration({ maskAllText: true, blockAllMedia: true })]
        : []),
    ],
    replaysSessionSampleRate: settings.replayEnabled ? 0.1 : 0,
    replaysOnErrorSampleRate: settings.replayEnabled ? 1 : 0,
    beforeBreadcrumb(breadcrumb) {
      for (const key of ["url", "from", "to"]) {
        const value = breadcrumb.data?.[key];
        if (typeof value === "string")
          breadcrumb.data![key] = withoutUrlSecrets(value);
      }
      return breadcrumb;
    },
    beforeSend(event) {
      delete event.user;
      delete event.extra;
      event.request = {
        url: window.location.origin + window.location.pathname,
      };
      for (const exception of event.exception?.values ?? []) {
        for (const frame of exception.stacktrace?.frames ?? []) {
          if (frame.filename)
            frame.filename = withoutUrlSecrets(frame.filename);
        }
      }
      return event;
    },
    propagateTraceparent: true,
    beforeSendLog: (log) =>
      settings.logLevels.includes(log.level) ? log : null,
    beforeSendSpan(span) {
      const url = span.attributes["url.full"];
      if (typeof url === "string")
        span.attributes["url.full"] = withoutUrlSecrets(url);
      return span;
    },
    tracePropagationTargets: [/^\/api\//],
  });
}

function withoutUrlSecrets(url: string) {
  return url.split(/[?#]/)[0];
}
