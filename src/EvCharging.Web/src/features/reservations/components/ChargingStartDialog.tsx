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
import { useUserSession } from "@/features/auth/useUserSession";
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
  const session = useUserSession();
  const token = session.antiforgeryToken;
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
        {session.query.isPending && <Loader aria-label="Loading session" />}
        {session.query.isError && (
          <ReservationFailure
            error={session.query.error}
            retry={() => {
              void session.query.refetch();
            }}
          />
        )}
        {session.signedIn === false && (
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
