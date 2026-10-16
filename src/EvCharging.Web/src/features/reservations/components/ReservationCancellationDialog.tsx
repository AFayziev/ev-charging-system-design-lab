import { Button, Group, Loader, Modal, Stack, Text } from "@mantine/core";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useUserSession } from "@/features/auth/useUserSession";
import {
  cancelReservation,
  reservationKeys,
  type Reservation,
  type ReservationList,
} from "../api";
import { formatReservationSlot, getBrowserTimeZone } from "../time-formatting";
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
  const session = useUserSession();
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
          {reservation.chargerName} · Connector {reservation.connectorNumber}
        </Text>
        <Text size="sm">
          {formatReservationSlot(reservation.startsAt, reservation.endsAt)}
        </Text>
        <Text size="sm" c="dimmed">
          Your time · {getBrowserTimeZone()}
        </Text>
        <Text size="sm">
          This time slot will be available to other drivers.
        </Text>
        {session.query.isPending && <Loader aria-label="Loading session" />}
        {session.query.isError && (
          <ReservationFailure
            error={session.query.error}
            retry={() => void session.query.refetch()}
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
            disabled={!session.antiforgeryToken}
            onClick={() => {
              if (!cancellation.isPending && session.antiforgeryToken)
                cancellation.mutate(session.antiforgeryToken);
            }}
          >
            Confirm cancellation
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
