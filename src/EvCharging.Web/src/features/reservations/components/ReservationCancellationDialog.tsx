import { Button, Group, Loader, Modal, Stack, Text } from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { sessionQuery } from "@/features/auth/api";
import {
  cancelReservation,
  reservationKeys,
  type Reservation,
  type ReservationList,
} from "../api";
import { formatReservationSlot } from "../time-formatting";
import { ReservationFailure } from "./ReservationFailure";

export function ReservationCancellationDialog({
  reservation,
  onCancelled,
  onClose,
}: {
  reservation: Reservation;
  onCancelled: (saved: Reservation) => void;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const session = useQuery(sessionQuery);
  const cancellation = useMutation({
    mutationFn: (token: string) => cancelReservation(reservation.id, token),
    retry: false,
    onSuccess: (saved) => {
      client.setQueriesData<ReservationList>(
        { queryKey: reservationKeys.lists },
        (previous) =>
          previous && {
            ...previous,
            reservations: previous.reservations.map((value) =>
              value.id === saved.id ? saved : value,
            ),
          },
      );
      void client.invalidateQueries({ queryKey: reservationKeys.lists });
      void client.invalidateQueries({
        queryKey: reservationKeys.bookingPolicy,
      });
      onCancelled(saved);
      onClose();
    },
    onError: () => {
      void client.invalidateQueries({ queryKey: reservationKeys.lists });
    },
  });
  const canConfirm =
    session.isSuccess &&
    session.data.signedIn &&
    !!session.data.antiforgeryToken;

  return (
    <Modal
      opened
      onClose={() => {
        if (!cancellation.isPending) onClose();
      }}
      title="Cancel reservation?"
      closeOnClickOutside={!cancellation.isPending}
      closeOnEscape={!cancellation.isPending}
      withCloseButton={!cancellation.isPending}
    >
      <Stack>
        <Text>Release your booking at {reservation.stationName}?</Text>
        <Text size="sm">
          {reservation.chargerName} · EVSE {reservation.evseNumber} · Connector{" "}
          {reservation.connectorNumber}
        </Text>
        <Text size="sm">
          {formatReservationSlot(
            reservation.startsAt,
            reservation.endsAt,
            reservation.timeZoneId,
          )}
        </Text>
        <Text size="sm" c="dimmed">
          Station time · {reservation.timeZoneId}
        </Text>
        <Text size="sm">
          This time slot will be available to other drivers.
        </Text>
        {session.isPending && <Loader aria-label="Loading session" />}
        {session.isError && (
          <ReservationFailure
            error={session.error}
            retry={() => void session.refetch()}
          />
        )}
        {cancellation.isError && (
          <ReservationFailure error={cancellation.error} booking />
        )}
        <Group justify="flex-end">
          <Button
            variant="default"
            disabled={cancellation.isPending}
            onClick={onClose}
          >
            Keep reservation
          </Button>
          <Button
            color="red"
            loading={cancellation.isPending}
            disabled={!canConfirm}
            onClick={() => {
              if (
                canConfirm &&
                !cancellation.isPending &&
                session.data?.antiforgeryToken
              )
                cancellation.mutate(session.data.antiforgeryToken);
            }}
          >
            Confirm cancellation
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
