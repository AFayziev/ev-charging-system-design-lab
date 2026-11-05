import { Button, Group, Loader, Modal, Stack, Text } from "@mantine/core";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { FailureAlert } from "@/components/StatusAlert";
import { useUserSession } from "@/features/auth/useUserSession";
import { getHttpFailure } from "@/lib/http-errors";
import {
  chargingKeys,
  formatEquipment,
  releaseChargingStart,
  type UnsettledOperation,
} from "../api";

export function ReleaseStartDialog({
  operation,
  onClose,
}: {
  operation: UnsettledOperation;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const session = useUserSession();
  const release = useMutation({
    mutationFn: (token: string) => releaseChargingStart(operation.id, token),
    retry: false,
    onSuccess: onClose,
    // A refused release means the start changed, so the list is read again either way.
    onSettled: () => {
      void client.invalidateQueries({ queryKey: chargingKeys.unsettledLists });
    },
  });
  const busy = release.isPending;

  return (
    <Modal
      opened
      onClose={() => {
        if (!busy) onClose();
      }}
      title="Release connector?"
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
      withCloseButton={!busy}
    >
      <Stack>
        <Text>{formatEquipment(operation.equipment)}</Text>
        <Text size="sm">
          The station never confirmed whether this start happened, and it has
          sent no readings since. Releasing ends the session and frees the
          connector. If the arrival deadline has passed, the reservation expires
          and its remaining time can be booked. The start stays unresolved.
        </Text>
        <Text size="sm">
          Charging may still be in progress. Check the charger before releasing
          it.
        </Text>
        {session.query.isPending && <Loader aria-label="Loading session" />}
        {session.query.isError && (
          <FailureAlert failure={getHttpFailure(session.query.error)} />
        )}
        {release.isError && (
          <FailureAlert failure={getHttpFailure(release.error, "write")} />
        )}
        <Group justify="flex-end">
          <Button variant="default" disabled={busy} onClick={onClose}>
            Keep waiting
          </Button>
          <Button
            color="red"
            loading={busy}
            disabled={!session.antiforgeryToken}
            onClick={() => {
              if (!busy && session.antiforgeryToken)
                release.mutate(session.antiforgeryToken);
            }}
          >
            Release connector
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
