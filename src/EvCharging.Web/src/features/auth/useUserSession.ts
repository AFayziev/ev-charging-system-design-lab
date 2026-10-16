import { useQuery } from "@tanstack/react-query";
import { sessionQuery } from "./api";

export function useUserSession({ enabled = true }: { enabled?: boolean } = {}) {
  const query = useQuery({ ...sessionQuery, enabled });
  const current = query.isSuccess ? query.data : undefined;

  return {
    query,
    signedIn: current?.signedIn,
    user: current?.signedIn ? current.user : null,
    antiforgeryToken: current?.signedIn ? current.antiforgeryToken : null,
  };
}
