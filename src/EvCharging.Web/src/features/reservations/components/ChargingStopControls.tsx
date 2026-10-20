import { Alert, Button, Stack } from "@mantine/core";
import {
  IconAlertCircle,
  IconHelpCircle,
  IconPlayerStop,
} from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { getHttpFailure } from "@/lib/http-errors";
import {
  reservationKeys,
  stopCharging,
  type Reservation,
  type ReservationList,
  type StopOperation,
} from "../api";
import { getStopPresentation } from "../stop-presentation";
import { useSubmissionIdentity } from "../useSubmissionIdentity";
import { ChargingStopDialog } from "./ChargingStopDialog";
import classes from "./Reservations.module.css";

export function ChargingStopControls({
  reservation,
  statusUnavailable,
  onRefresh,
}: {
  reservation: Reservation;
  statusUnavailable: boolean;
  onRefresh: () => Promise<unknown>;
}) {
  const client = useQueryClient();
  const [opened, setOpened] = useState(false);
  const stop = reservation.stopOperation;
  const submission = useSubmissionIdentity(stop?.id ?? null);
  const updateOperation = (saved: StopOperation) => {
    client.setQueriesData<ReservationList>(
      { queryKey: reservationKeys.lists },
      (previous) =>
        previous && {
          ...previous,
          reservations: previous.reservations.map((value) =>
            value.id === reservation.id
              ? { ...value, stopOperation: saved }
              : value,
          ),
        },
    );
  };
  const mutation = useMutation({
    mutationFn: (token: string) =>
      stopCharging(reservation.id, submission.requestKey, token),
    retry: false,
    onSuccess: (saved) => {
      submission.succeeded();
      setOpened(false);
      updateOperation(saved);
      void onRefresh();
    },
    onError: (error) => {
      submission.failed(error);
      setOpened(false);
      void onRefresh();
    },
  });
  // A live stop ends the session before the next refresh reports its lifecycle.
  const sessionActive =
    reservation.startOperation?.outcome === "confirmed" &&
    reservation.startOperation.lifecycle === "active" &&
    (!stop || stop.outcome === "rejected");
  const canStop = sessionActive && !statusUnavailable && !mutation.isPending;
  const content = getStopPresentation(stop);
  const failure =
    mutation.isError && submission.showsFailure
      ? getHttpFailure(mutation.error, "write")
      : undefined;

  return (
    <>
      <div className={classes.actions}>
        {sessionActive && (
          <Button
            color="red"
            variant="light"
            leftSection={<IconPlayerStop size={16} aria-hidden />}
            fullWidth
            disabled={!canStop}
            loading={mutation.isPending}
            onClick={() => {
              submission.prepare();
              setOpened(true);
            }}
          >
            {submission.unconfirmed
              ? "Retry stop request"
              : stop?.outcome === "rejected" || mutation.isError
                ? "Try stopping again"
                : "Stop charging"}
          </Button>
        )}
      </div>
      <Stack gap="xs" className={classes.chargingStatus}>
        {content && (
          <Alert
            color={content.color}
            title={content.title}
            icon={<content.icon size={20} aria-hidden />}
            role="status"
          >
            {content.text}
          </Alert>
        )}
        {submission.unconfirmed && sessionActive && (
          <Alert
            color="orange"
            title="Stop request not confirmed"
            icon={<IconHelpCircle size={20} aria-hidden />}
            role="alert"
          >
            We could not confirm your stop request. You can safely retry this
            request.
          </Alert>
        )}
        {sessionActive && !submission.unconfirmed && failure && (
          <Alert
            color="red"
            title={failure.title}
            icon={<IconAlertCircle size={20} aria-hidden />}
            role="alert"
          >
            {failure.message}
          </Alert>
        )}
      </Stack>
      {opened && (
        <ChargingStopDialog
          reservation={reservation}
          pending={mutation.isPending}
          onConfirm={(token) => mutation.mutate(token)}
          onClose={() => setOpened(false)}
        />
      )}
    </>
  );
}
