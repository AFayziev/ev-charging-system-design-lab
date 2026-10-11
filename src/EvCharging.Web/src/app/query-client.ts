import {
  hashKey,
  MutationCache,
  QueryCache,
  QueryClient,
} from "@tanstack/react-query";
import { HttpStatusCode } from "axios";
import { authKeys } from "@/features/auth/api";
import { getHttpStatus } from "@/lib/http-errors";

export function createAppQueryClient() {
  const recheckSession = (error: unknown) => {
    if (getHttpStatus(error) === HttpStatusCode.Unauthorized) {
      void client.invalidateQueries(
        { queryKey: authKeys.session, exact: true },
        { cancelRefetch: false },
      );
    }
  };
  const client = new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (query.queryHash !== hashKey(authKeys.session))
          recheckSession(error);
      },
    }),
    mutationCache: new MutationCache({ onError: recheckSession }),
  });

  return client;
}
