import { queryOptions } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { type BrowserSession } from "./BrowserSession";

export function getSignInUrl(returnUrl: string): string {
  return `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export async function getSession(
  signal?: AbortSignal,
): Promise<BrowserSession> {
  const response = await api.get<BrowserSession>("/auth/session", { signal });
  return response.data;
}

export const authKeys = {
  session: ["auth", "session"] as const,
};

export const sessionQuery = queryOptions({
  queryKey: authKeys.session,
  queryFn: ({ signal }) => getSession(signal),
  retry: false,
});
