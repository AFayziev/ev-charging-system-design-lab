import { HttpStatusCode } from "axios";
import { useState } from "react";
import { getHttpStatus } from "@/lib/http-errors";

/**
 * Tracks the idempotency key of a command submission. After a failure that does
 * not show whether the server accepted the request, the same key is reused until
 * the server reports a newer operation, so a retry cannot create a second one.
 */
export function useSubmissionIdentity(currentOperationId: string | null) {
  const [requestKey, setRequestKey] = useState(() => crypto.randomUUID());
  const [unconfirmedAgainst, setUnconfirmedAgainst] = useState<string | null>();
  const unconfirmed =
    unconfirmedAgainst !== undefined &&
    unconfirmedAgainst === currentOperationId;
  return {
    requestKey,
    unconfirmed,
    // A definite failure, or an unconfirmed one the server has not yet superseded.
    showsFailure: unconfirmedAgainst === undefined || unconfirmed,
    prepare: () => {
      if (!unconfirmed) setRequestKey(crypto.randomUUID());
    },
    succeeded: () => setUnconfirmedAgainst(undefined),
    failed: (error: unknown) => {
      const status = getHttpStatus(error);
      setUnconfirmedAgainst(
        status === undefined || status >= HttpStatusCode.InternalServerError
          ? currentOperationId
          : undefined,
      );
    },
  };
}
