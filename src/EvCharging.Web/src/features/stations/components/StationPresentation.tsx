import { Alert, Button, Stack, Text } from "@mantine/core";
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

export function StationFailure({ retry }: { retry: () => void }) {
  return (
    <Alert color="red" title="Station information unavailable" role="alert">
      <Stack gap="sm">
        <Text size="sm">
          We could not load station information. Please try again.
        </Text>
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
