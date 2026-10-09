import react from "@vitejs/plugin-react";
import { defineConfig, type ProxyOptions } from "vite";

export default defineConfig(({ command, isPreview }) => {
  const plugins = [react()];
  if (command !== "serve" || isPreview) {
    return { plugins };
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
        "^/(api|auth)(/|$|\\?)": proxy,
      },
    },
  };
});
