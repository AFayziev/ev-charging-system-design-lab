import { sentryVitePlugin } from "@sentry/vite-plugin";
import react from "@vitejs/plugin-react";
import { defineConfig, type ProxyOptions } from "vite";

export default defineConfig(({ command, isPreview }) => {
  const upload = process.env.SENTRY_UPLOAD === "true";
  if (
    upload &&
    (!process.env.SENTRY_AUTH_TOKEN ||
      !process.env.SENTRY_ORG ||
      !process.env.SENTRY_PROJECT ||
      !process.env.SENTRY_RELEASE)
  )
    throw new Error("Sentry upload requires build credentials and a release.");
  const plugins = [
    react(),
    ...(upload
      ? [
          sentryVitePlugin({
            org: process.env.SENTRY_ORG,
            project: process.env.SENTRY_PROJECT,
            authToken: process.env.SENTRY_AUTH_TOKEN,
            release: { name: process.env.SENTRY_RELEASE },
            sourcemaps: { filesToDeleteAfterUpload: ["dist/**/*.map"] },
          }),
        ]
      : []),
  ];
  if (command !== "serve" || isPreview) {
    return { plugins, build: { sourcemap: upload ? "hidden" : false } };
  }

  const gateway = process.env.services__gateway__http__0;
  if (!gateway) {
    throw new Error(
      "Start the frontend through Aspire to discover the gateway.",
    );
  }

  const proxy: ProxyOptions = {
    target: gateway,
  };

  return {
    plugins,
    server: {
      proxy: {
        "^/(api|auth|diagnostics)(/|$|\\?)": proxy,
      },
    },
  };
});
