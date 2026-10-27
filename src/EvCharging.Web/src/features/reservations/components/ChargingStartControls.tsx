import { StatusAlert } from "@/components/StatusAlert";
import { formatClock } from "@/lib/time-formatting";
import { Button, Stack } from "@mantine/core";
import { IconBolt } from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { invalidateStationAvailability } from "@/features/stations/api";
import { useState } from "react";
import { useUserSession } from "@/features/auth/useUserSession";
import { getHttpFailure, getHttpProblemCode } from "@/lib/http-errors";
import {
  isUnresolved,
  checkChargingStart,
  reservationKeys,
  startCharging,
  type Reservation,
  type ReservationList,
  type StartOperation,
} from "../api";
import { getStartPresentation } from "../start-presentation";
import { useSubmissionIdentity } from "../useSubmissionIdentity";
import { connectorBusyCode } from "../errors";
import { formatReservationTransitionTime } from "../time-formatting";
import { ChargingStartDialog } from "./ChargingStartDialog";
import classes from "./Reservations.module.css";

export function ChargingStartControls({
  reservation,
  statusUnavailable,
  onRefresh,
  onCancel,
}: {
  reservation: Reservation;
  statusUnavailable: boolean;
  onRefresh: () => Promise<unknown>;
  onCancel?: (reservation: Reservation) => void;
}) {
  const client = useQueryClient();
  // Refresh on demand rather than fetch a session for every reservation card.
  const session = useUserSession({ enabled: false });
  const [opened, setOpened] = useState(false);
  const operation = reservation.startOperation;
  const submission = useSubmissionIdentity(operation?.id ?? null);
  const updateOperation = (saved: StartOperation) => {
    client.setQueriesData<ReservationList>(
      { queryKey: reservationKeys.lists },
      (previous) =>
        previous && {
          ...previous,
          reservations: previous.reservations.map((value) =>
            value.id === reservation.id
              ? { ...value, startOperation: saved }
              : value,
          ),
        },
    );
  };
  const statusCheck = useMutation({
    retry: false,
    onMutate: () => client.cancelQueries({ queryKey: reservationKeys.lists }),
    mutationFn: async () => {
      if (!isUnresolved(operation) || !operation) {
        return null;
      }
      const refreshed = await session.query.refetch({
        throwOnError: true,
        cancelRefetch: false,
      });
      const token =
        refreshed.isSuccess && refreshed.data.signedIn
          ? refreshed.data.antiforgeryToken
          : null;
      if (!token) return null;
      return checkChargingStart(reservation.id, operation.id, token);
    },
    onSuccess: (saved) => {
      if (saved) updateOperation(saved.operation);
      void onRefresh();
    },
  });
  const mutation = useMutation({
    mutationFn: (token: string) =>
      startCharging(reservation.id, submission.requestKey, token),
    retry: false,
    onSuccess: (operation) => {
      submission.succeeded();
      setOpened(false);
      void invalidateStationAvailability(client);
      updateOperation(operation);
      void onRefresh();
    },
    onError: (error) => {
      submission.failed(error);
      setOpened(false);
      void onRefresh();
    },
  });
  const blocked =
    isUnresolved(operation) ||
    (operation?.outcome === "confirmed" && operation.lifecycle !== "failed");
  const now = Date.now();
  const check =
    statusCheck.data?.operation.id === operation?.id
      ? statusCheck.data
      : undefined;
  const early = now < Date.parse(reservation.startsAt);
  const late = now >= Date.parse(reservation.arrivalDeadline);
  const active = reservation.state === "active";
  const canStart =
    active &&
    !early &&
    !late &&
    !blocked &&
    !statusUnavailable &&
    !statusCheck.isPending &&
    !mutation.isPending;
  const failure =
    mutation.isError && submission.showsFailure
      ? getHttpFailure(mutation.error, "write")
      : undefined;
  const connectorBusy =
    getHttpProblemCode(mutation.error) === connectorBusyCode;
  // A live stop supersedes the start result once the session is ending.
  const outcomeContent =
    reservation.stopOperation &&
    reservation.stopOperation.outcome !== "rejected"
      ? undefined
      : getStartPresentation(
          operation,
          active && !late,
          reservation.arrivalDeadline,
        );

  return (
    <>
      <div className={classes.actions}>
        {active && !blocked && (
          <Button
            leftSection={<IconBolt size={16} aria-hidden />}
            fullWidth
            disabled={!canStart}
            loading={mutation.isPending}
            onClick={() => {
              submission.prepare();
              setOpened(true);
            }}
          >
            {submission.unconfirmed
              ? "Retry start request"
              : operation?.outcome === "rejected" || mutation.isError
                ? "Try starting again"
                : "Start charging"}
          </Button>
        )}
        {isUnresolved(operation) && (
          <Button
            color="blue"
            variant="light"
            fullWidth
            loading={statusCheck.isPending}
            onClick={() => statusCheck.mutate()}
          >
            Check status
          </Button>
        )}
        {active &&
          !blocked &&
          !submission.unconfirmed &&
          !statusUnavailable &&
          onCancel && (
            <Button
              variant="outline"
              color="red"
              fullWidth
              disabled={mutation.isPending}
              onClick={() => onCancel(reservation)}
            >
              Cancel reservation
            </Button>
          )}
      </div>
      <Stack gap="xs" className={classes.chargingStatus}>
        {statusCheck.isPending && (
          <StatusAlert tone="pending" title="Checking with the station">
            We are checking whether charging started. Please wait.
          </StatusAlert>
        )}
        {isUnresolved(operation) &&
          !operation?.automaticChecksEndAt &&
          check &&
          !statusCheck.isPending &&
          check.outcome === "unresolved" && (
            <StatusAlert tone="unresolved" title="Start is still unresolved">
              The station has not provided a definite result. Charging may still
              have started. Do not submit another start.
            </StatusAlert>
          )}
        {isUnresolved(operation) && statusCheck.isError && (
          <StatusAlert
            tone="error"
            title={getHttpFailure(statusCheck.error).title}
          >
            {getHttpFailure(statusCheck.error).message}
          </StatusAlert>
        )}
        {outcomeContent &&
          !statusCheck.isPending &&
          (!isUnresolved(operation) ||
            operation?.automaticChecksEndAt ||
            !check ||
            check.outcome === "resolved") && (
            <StatusAlert
              tone={outcomeContent.tone}
              title={outcomeContent.title}
            >
              {outcomeContent.text}
              {isUnresolved(operation) &&
                check?.outcome === "unresolved" &&
                operation?.automaticChecksEndAt &&
                " The latest status check did not provide a definite result."}
            </StatusAlert>
          )}
        {submission.unconfirmed && !blocked && (
          <StatusAlert
            tone="unresolved"
            role="alert"
            title="Start request not confirmed"
          >
            We could not confirm your start request. You can safely retry this
            request.
          </StatusAlert>
        )}
        {!blocked && !submission.unconfirmed && failure && (
          <StatusAlert
            tone={connectorBusy ? "warning" : "error"}
            title={connectorBusy ? "Connector is still in use" : failure.title}
          >
            {connectorBusy
              ? "The connector is still in use or waiting for a previous start to be confirmed. Try again when it is free."
              : failure.message}
          </StatusAlert>
        )}
        {!operation && active && early && (
          <StatusAlert tone="pending" title="Not ready to start yet">
            You can start{" "}
            {formatReservationTransitionTime(reservation.startsAt)}. Connect
            your vehicle before requesting a start.
          </StatusAlert>
        )}
        {!blocked && active && late && (
          <StatusAlert tone="neutral" title="Start window has ended">
            This reservation was not used before its{" "}
            {formatClock(reservation.arrivalDeadline)} arrival deadline.
          </StatusAlert>
        )}
      </Stack>
      {opened && (
        <ChargingStartDialog
          reservation={reservation}
          pending={mutation.isPending}
          canStart={canStart}
          onConfirm={(token) => mutation.mutate(token)}
          onClose={() => setOpened(false)}
        />
      )}
    </>
  );
}
