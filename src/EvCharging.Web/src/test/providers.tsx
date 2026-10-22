import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

/** A client that reports a failed request at once instead of retrying it. */
export function createTestQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

/** The providers every page needs; the router differs per test and is rendered inside. */
export function TestProviders({
  client,
  env,
  children,
}: {
  client: QueryClient;
  env?: "test";
  children: ReactNode;
}) {
  return (
    <MantineProvider env={env}>
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    </MantineProvider>
  );
}
