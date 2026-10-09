import { ErrorBoundary, reactErrorHandler } from "@sentry/react";
import "@mantine/core/styles.css";
import { MantineProvider, Text, Title, createTheme } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { App } from "./App";

const queryClient = new QueryClient();
const fontFamily = '"Segoe UI", -apple-system, BlinkMacSystemFont, sans-serif';
const theme = createTheme({
  primaryColor: "teal",
  primaryShade: 9,
  defaultRadius: "sm",
  fontFamily,
  headings: {
    fontFamily,
    fontWeight: "700",
    sizes: { h1: { fontSize: "1.875rem", lineHeight: "1.25" } },
  },
  components: {
    Text: Text.extend({ styles: { root: { overflowWrap: "anywhere" } } }),
    Title: Title.extend({
      styles: {
        root: { color: "var(--app-heading-color)", overflowWrap: "anywhere" },
      },
    }),
  },
});

createRoot(document.getElementById("root")!, {
  onUncaughtError: reactErrorHandler(),
  onRecoverableError: reactErrorHandler(),
}).render(
  <StrictMode>
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <ErrorBoundary
            fallback={
              <p role="alert">
                This page could not be displayed. Reload to try again.
              </p>
            }
          >
            <App />
          </ErrorBoundary>
        </BrowserRouter>
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
);
