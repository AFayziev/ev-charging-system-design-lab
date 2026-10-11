import { Alert, Button, Stack, Text } from "@mantine/core";
import { getHttpFailure } from "@/lib/http-errors";
import type { ConnectorCapability } from "../api";

export function connectorTypeLabel(connectorType: string) {
  return connectorType === "Type2" ? "Type 2" : connectorType;
}

export function capabilityLabel(capability: ConnectorCapability) {
  return `${connectorTypeLabel(capability.connectorType)} · ${capability.powerSupply} · ${capability.ratedPowerKW} kW`;
}

export function StationLoading() {
  return (
    <Text role="status" py="xl">
      Loading station information…
    </Text>
  );
}

export function StationFailure({
  error,
  retry,
}: {
  error: unknown;
  retry: () => void;
}) {
  const failure = getHttpFailure(error);
  return (
    <Alert color="red" title={failure.title} role="alert">
      <Stack gap="sm">
        <Text size="sm">{failure.message}</Text>
        <Button
          variant="light"
          color="red"
          onClick={retry}
          w="fit-content"
          mih={44}
        >
          Try again
        </Button>
      </Stack>
    </Alert>
  );
}
