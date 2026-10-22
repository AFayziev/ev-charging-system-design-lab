import { StatusAlert } from "@/components/StatusAlert";
import { Button, Group, Loader, Modal, Stack, Text } from "@mantine/core";
import { useUserSession } from "@/features/auth/useUserSession";
import type { Reservation } from "../api";
import { ReservationFailure } from "./ReservationFailure";

export function ChargingStopDialog({
  reservation,
  pending,
  onConfirm,
  onClose,
}: {
  reservation: Reservation;
  pending: boolean;
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
      title="Stop charging?"
      closeOnClickOutside={!pending}
      closeOnEscape={!pending}
      withCloseButton={!pending}
    >
      <Stack>
        <Text fw={600}>{reservation.stationName}</Text>
        <Text size="sm">
          {reservation.chargerName} · Connector {reservation.connectorNumber}
        </Text>
        <Text size="sm">
          We will show the result after the station confirms your request. Final
          usage is available after charging has fully ended.
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
          <StatusAlert tone="warning">
            Sign in again to request a stop.
          </StatusAlert>
        )}
        <Group justify="flex-end">
          <Button variant="default" disabled={pending} onClick={onClose}>
            Keep charging
          </Button>
          <Button
            color="red"
            loading={pending}
            disabled={!token}
            onClick={() => {
              if (token) onConfirm(token);
            }}
          >
            Stop charging
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
