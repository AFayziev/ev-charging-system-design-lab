import {
  Anchor,
  Badge,
  Card,
  Group,
  Stack,
  Text,
  ThemeIcon,
  Title,
} from "@mantine/core";
import {
  IconArrowLeft,
  IconChargingPile,
  IconClock,
  IconMapPin,
  IconPlug,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import axios from "axios";
import { Link, useParams } from "react-router";
import { stationDetailQuery } from "../api";
import { StationPhoto } from "./StationPhoto";
import {
  capabilityLabel,
  StationLoading,
  StationFailure,
} from "./StationPresentation";
import classes from "./Stations.module.css";

export function StationDetailPage() {
  const { stationId = "" } = useParams();
  const query = useQuery(stationDetailQuery(stationId));
  const missing =
    query.isError &&
    axios.isAxiosError(query.error) &&
    query.error.response?.status === 404;
  const coordinates = query.data
    ? `${query.data.latitude},${query.data.longitude}`
    : null;
  const mapsUrl =
    coordinates === null
      ? undefined
      : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(coordinates)}`;

  return (
    <Stack gap="xs">
      <Anchor
        component={Link}
        to="/stations"
        className={classes.backLink}
        underline="never"
        w="fit-content"
        mih={44}
        display="inline-flex"
      >
        <Group gap={6}>
          <IconArrowLeft size={18} aria-hidden="true" />
          <Text size="sm" fw={600}>
            All stations
          </Text>
        </Group>
      </Anchor>
      {query.isPending ? (
        <StationLoading />
      ) : missing ? (
        <Stack>
          <Title order={1}>Station not found</Title>
          <Text c="dimmed">
            This station is unavailable or no longer exists.
          </Text>
        </Stack>
      ) : query.isError ? (
        <StationFailure retry={() => void query.refetch()} />
      ) : (
        <div className={classes.detailGrid}>
          <Stack gap="xl">
            <div className={classes.stationHeading}>
              <Stack gap="sm">
                <Title order={1}>{query.data.name}</Title>
                <div className={classes.metadata}>
                  <IconMapPin size={19} aria-hidden="true" />
                  <Text size="sm">{query.data.address}</Text>
                </div>
                <Text size="sm" c="dimmed">
                  Review charging equipment and station access before your
                  visit.
                </Text>
              </Stack>
              <StationPhoto />
            </div>
            <Stack gap="md" id="equipment">
              <Title order={2} size="h3">
                Charging equipment
              </Title>
              {query.data.chargers.length === 0 && (
                <Text c="dimmed">No charging equipment listed.</Text>
              )}
              {query.data.chargers.map((charger) => (
                <Card
                  key={charger.id}
                  withBorder
                  className={classes.equipmentCard}
                >
                  <div className={classes.chargerHeading}>
                    <ThemeIcon variant="light" size={32} radius="sm">
                      <IconChargingPile size={21} aria-hidden="true" />
                    </ThemeIcon>
                    <Title order={3} size="h4">
                      {charger.name}
                    </Title>
                  </div>
                  {charger.evses.length === 0 && (
                    <Text size="sm" c="dimmed" p="md">
                      No EVSEs listed.
                    </Text>
                  )}
                  {charger.evses.map((evse) => (
                    <div key={evse.id} className={classes.evse}>
                      {evse.connectors.length === 0 && (
                        <Text size="sm" c="dimmed" py="sm">
                          EVSE {evse.number}: No connectors listed.
                        </Text>
                      )}
                      {evse.connectors.map((connector) => (
                        <div key={connector.id} className={classes.connector}>
                          <ThemeIcon variant="light" size={36} radius="xl">
                            <IconPlug size={21} aria-hidden="true" />
                          </ThemeIcon>
                          <Stack gap={2} className={classes.connectorBody}>
                            <Text size="sm" fw={600}>
                              EVSE {evse.number} · Connector {connector.number}
                            </Text>
                            <Text size="sm" c="dimmed">
                              {capabilityLabel(connector)}
                            </Text>
                          </Stack>
                          <Badge variant="light" radius="sm">
                            {connector.powerSupply}
                          </Badge>
                        </div>
                      ))}
                    </div>
                  ))}
                </Card>
              ))}
            </Stack>
          </Stack>
          <Card
            withBorder
            padding="lg"
            className={classes.accessCard}
            component="aside"
          >
            <Stack gap="lg">
              <Title order={2} size="h3">
                Station access
              </Title>
              <Text size="sm">
                {query.data.accessInstructions ||
                  "No access instructions published."}
              </Text>
              <div className={classes.metadata}>
                <IconClock size={18} aria-hidden="true" />
                <Text size="sm">Time zone: {query.data.timeZoneId}</Text>
              </div>
              <div className={classes.metadata}>
                <IconMapPin size={18} aria-hidden="true" />
                <Text size="sm">
                  Coordinates: {query.data.latitude}, {query.data.longitude}
                </Text>
              </div>
              <Anchor href={mapsUrl} target="_blank" rel="noopener noreferrer">
                Open in maps
              </Anchor>
            </Stack>
          </Card>
        </div>
      )}
    </Stack>
  );
}
