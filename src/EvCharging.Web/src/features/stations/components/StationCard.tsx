import { Badge, Button, Card, Group, Stack, Text, Title } from "@mantine/core";
import {
  IconArrowRight,
  IconChargingPile,
  IconPlug,
} from "@tabler/icons-react";
import { Link } from "react-router";
import type { StationSummary } from "../api";
import { StationPhoto } from "./StationPhoto";
import { capabilityLabel } from "./StationPresentation";
import classes from "./Stations.module.css";

export function StationCard({
  station,
}: {
  /** The distance is present when the results were ordered by distance from the driver. */
  station: StationSummary & { readonly distanceKm?: number | null };
}) {
  const distanceKm = station.distanceKm ?? null;
  const maximumPower =
    station.capabilities.length > 0
      ? Math.max(
          ...station.capabilities.map((capability) => capability.ratedPowerKW),
        )
      : null;
  return (
    <Card withBorder className={classes.catalogCard} component="article">
      <StationPhoto />
      <Stack className={classes.cardBody}>
        <Stack gap={4}>
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Title order={2} size="h3">
              {station.name}
            </Title>
            {distanceKm !== null && (
              <Badge variant="light" size="lg" style={{ flexShrink: 0 }}>
                {distanceKm.toFixed(1)} km
              </Badge>
            )}
          </Group>
          <Text c="dimmed" size="sm">
            {station.address}
          </Text>
        </Stack>
        <Stack gap={6}>
          <Text size="sm" fw={600}>
            {maximumPower !== null
              ? `Up to ${maximumPower} kW rated power`
              : "No rated power listed"}
          </Text>
          {station.capabilities.map((capability) => (
            <div className={classes.metadata} key={capabilityLabel(capability)}>
              <IconPlug size={17} aria-hidden="true" />
              <Text size="sm">{capabilityLabel(capability)}</Text>
            </div>
          ))}
          <div className={classes.metadata}>
            <IconChargingPile size={17} aria-hidden="true" />
            <Text size="sm">
              {station.chargerCount}{" "}
              {station.chargerCount === 1 ? "charger" : "chargers"} ·{" "}
              {station.connectorCount}{" "}
              {station.connectorCount === 1 ? "connector" : "connectors"}
            </Text>
          </div>
        </Stack>
        <Button
          component={Link}
          to={`/stations/${station.id}`}
          aria-label={`View ${station.name}`}
          fullWidth
          mih={44}
          mt="auto"
          rightSection={<IconArrowRight size={18} aria-hidden="true" />}
        >
          View station
        </Button>
      </Stack>
    </Card>
  );
}
