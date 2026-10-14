import {
  Alert,
  Button,
  Group,
  Loader,
  Modal,
  Stack,
  Text,
} from "@mantine/core";
import { IconAlertCircle, IconInfoCircle } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { sessionQuery } from "@/features/auth/api";
import { capabilityLabel } from "@/features/stations/components/StationPresentation";
import type { Reservation } from "../api";
import {
  formatReservationSlot,
  formatStartDeadline,
  getBrowserTimeZone,
} from "../time-formatting";
import { ReservationFailure } from "./ReservationFailure";

export function ChargingStartDialog({
  reservation,
  pending,
  canStart,
  onConfirm,
  onClose,
}: {
  reservation: Reservation;
  pending: boolean;
  canStart: boolean;
  onConfirm: (token: string) => void;
  onClose: () => void;
}) {
  const session = useQuery(sessionQuery);
  const token =
    session.isSuccess && session.data.signedIn
      ? session.data.antiforgeryToken
      : null;
  return (
    <Modal
      opened
      onClose={() => {
        if (!pending) onClose();
      }}
      title="Start charging?"
      closeOnClickOutside={!pending}
      closeOnEscape={!pending}
      withCloseButton={!pending}
    >
      <Stack>
        <Text fw={600}>{reservation.stationName}</Text>
        <Text size="sm">
          {reservation.chargerName} · Connector {reservation.connectorNumber}
        </Text>
        <Text size="sm" c="dimmed">
          {capabilityLabel(reservation)}
        </Text>
        <Text size="sm">
          {formatReservationSlot(reservation.startsAt, reservation.endsAt)}
        </Text>
        <Text size="sm" c="dimmed">
          Your time · {getBrowserTimeZone()} · Start by{" "}
          {formatStartDeadline(reservation.arrivalDeadline)}
        </Text>
        <Alert
          color="blue"
          icon={<IconInfoCircle size={20} aria-hidden />}
          role="note"
        >
          Connect your vehicle to Connector {reservation.connectorNumber} before
          starting.
        </Alert>
        <Text size="sm">
          We will show the result after the station confirms your request.
        </Text>
        {session.isPending && <Loader aria-label="Loading session" />}
        {session.isError && (
          <ReservationFailure
            error={session.error}
            retry={() => {
              void session.refetch();
            }}
          />
        )}
        {session.isSuccess && !token && (
          <Alert
            color="orange"
            icon={<IconAlertCircle size={20} aria-hidden />}
            role="alert"
          >
            Sign in again to request a start.
          </Alert>
        )}
        <Group justify="flex-end">
          <Button variant="default" disabled={pending} onClick={onClose}>
            Not now
          </Button>
          <Button
            loading={pending}
            disabled={!canStart || !token}
            onClick={() => {
              if (canStart && token) onConfirm(token);
            }}
          >
            Start charging
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
