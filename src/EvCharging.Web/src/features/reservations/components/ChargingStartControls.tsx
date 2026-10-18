import { Alert, Button, Stack } from "@mantine/core";
import {
  IconAlertCircle,
  IconBolt,
  IconClock,
  IconHelpCircle,
  IconPlug,
} from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { HttpStatusCode } from "axios";
import { useState } from "react";
import { useUserSession } from "@/features/auth/useUserSession";
import {
  getHttpFailure,
  getHttpProblemCode,
  getHttpStatus,
} from "@/lib/http-errors";
import {
  isStartUnresolved,
  checkChargingStart,
  reservationKeys,
  startCharging,
  type Reservation,
  type ReservationList,
  type StartOperation,
} from "../api";
import { getStartPresentation } from "../start-presentation";
import { connectorBusyCode } from "../errors";
import {
  formatReservationTransitionTime,
  formatStartDeadline,
} from "../time-formatting";
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
  const [requestKey, setRequestKey] = useState(() => crypto.randomUUID());
  const operation = reservation.startOperation;
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
      if (!isStartUnresolved(operation) || !operation) {
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
  const [unconfirmedAgainst, setUnconfirmedAgainst] = useState<string | null>();
  const ambiguous =
    unconfirmedAgainst !== undefined &&
    unconfirmedAgainst === (operation?.id ?? null);
  const mutation = useMutation({
    mutationFn: (token: string) =>
      startCharging(reservation.id, requestKey, token),
    retry: false,
    onSuccess: (operation) => {
      setUnconfirmedAgainst(undefined);
      setOpened(false);
      updateOperation(operation);
      void onRefresh();
    },
    onError: (error) => {
      const status = getHttpStatus(error);
      setUnconfirmedAgainst(
        status === undefined || status >= HttpStatusCode.InternalServerError
          ? (operation?.id ?? null)
          : undefined,
      );
      setOpened(false);
      void onRefresh();
    },
  });
  const blocked =
    isStartUnresolved(operation) ||
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
    mutation.isError && (unconfirmedAgainst === undefined || ambiguous)
      ? getHttpFailure(mutation.error, "write")
      : undefined;
  const connectorBusy =
    getHttpProblemCode(mutation.error) === connectorBusyCode;
  const outcomeContent = getStartPresentation(
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
              if (!ambiguous) setRequestKey(crypto.randomUUID());
              setOpened(true);
            }}
          >
            {ambiguous
              ? "Retry start request"
              : operation?.outcome === "rejected" || mutation.isError
                ? "Try starting again"
                : "Start charging"}
          </Button>
        )}
        {isStartUnresolved(operation) && (
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
        {active && !blocked && !ambiguous && !statusUnavailable && onCancel && (
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
          <Alert
            color="blue"
            title="Checking with the station"
            icon={<IconClock size={20} aria-hidden />}
            role="status"
          >
            We are checking whether charging started. Please wait.
          </Alert>
        )}
        {isStartUnresolved(operation) &&
          !operation?.automaticChecksEndAt &&
          check &&
          !statusCheck.isPending &&
          check.outcome === "unresolved" && (
            <Alert
              color="orange"
              title="Start is still unresolved"
              icon={<IconHelpCircle size={20} aria-hidden />}
              role="status"
            >
              The station has not provided a definite result. Charging may still
              have started. Do not submit another start.
            </Alert>
          )}
        {isStartUnresolved(operation) && statusCheck.isError && (
          <Alert
            color="red"
            title={getHttpFailure(statusCheck.error).title}
            icon={<IconAlertCircle size={20} aria-hidden />}
            role="alert"
          >
            {getHttpFailure(statusCheck.error).message}
          </Alert>
        )}
        {outcomeContent &&
          !statusCheck.isPending &&
          (!isStartUnresolved(operation) ||
            operation?.automaticChecksEndAt ||
            !check ||
            check.outcome === "resolved") && (
            <Alert
              color={outcomeContent.color}
              title={outcomeContent.title}
              icon={<outcomeContent.icon size={20} aria-hidden />}
              role="status"
            >
              {outcomeContent.text}
              {isStartUnresolved(operation) &&
                check?.outcome === "unresolved" &&
                operation?.automaticChecksEndAt &&
                " The latest status check did not provide a definite result."}
            </Alert>
          )}
        {ambiguous && !blocked && (
          <Alert
            color="orange"
            title="Start request not confirmed"
            icon={<IconHelpCircle size={20} aria-hidden />}
            role="alert"
          >
            We could not confirm your start request. You can safely retry this
            request.
          </Alert>
        )}
        {!blocked && !ambiguous && failure && (
          <Alert
            color={connectorBusy ? "orange" : "red"}
            title={connectorBusy ? "Connector is still in use" : failure.title}
            icon={
              connectorBusy ? (
                <IconPlug size={20} aria-hidden />
              ) : (
                <IconAlertCircle size={20} aria-hidden />
              )
            }
            role="alert"
          >
            {connectorBusy
              ? "The connector is still in use or waiting for a previous start to be confirmed. Try again when it is free."
              : failure.message}
          </Alert>
        )}
        {!operation && active && early && (
          <Alert
            color="blue"
            title="Not ready to start yet"
            icon={<IconClock size={20} aria-hidden />}
            role="status"
          >
            You can start{" "}
            {formatReservationTransitionTime(reservation.startsAt)}. Connect
            your vehicle before requesting a start.
          </Alert>
        )}
        {!blocked && active && late && (
          <Alert
            color="gray"
            title="Start window has ended"
            icon={<IconClock size={20} aria-hidden />}
            role="status"
          >
            This reservation was not used before its{" "}
            {formatStartDeadline(reservation.arrivalDeadline)} arrival deadline.
          </Alert>
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
