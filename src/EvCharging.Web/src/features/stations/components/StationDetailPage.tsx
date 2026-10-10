import {
  Anchor,
  Badge,
  Card,
  Stack,
  Text,
  ThemeIcon,
  Title,
} from "@mantine/core";
import {
  IconChargingPile,
  IconClock,
  IconMapPin,
  IconPlug,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { HttpStatusCode } from "axios";
import { Link, useParams } from "react-router";
import { BackLink } from "@/components/BackLink";
import { PageHeading } from "@/components/PageHeading";
import { getHttpStatus } from "@/lib/http-errors";
import { stationDetailQuery } from "../api";
import { StationPhoto } from "./StationPhoto";
import {
  capabilityLabel,
  StationLoading,
  StationFailure,
} from "./StationPresentation";
import classes from "./Stations.module.css";

export function StationDetailPage({ canEdit = false }: { canEdit?: boolean }) {
  const { stationId = "" } = useParams();
  const query = useQuery(stationDetailQuery(stationId));
  const missing =
    query.isError && getHttpStatus(query.error) === HttpStatusCode.NotFound;
  const coordinates = query.data
    ? `${query.data.latitude},${query.data.longitude}`
    : null;
  const mapsUrl =
    coordinates === null
      ? undefined
      : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(coordinates)}`;

  return (
    <Stack gap="xs">
      <BackLink to="/stations">All stations</BackLink>
      {query.isPending && <StationLoading />}
      {missing && (
        <PageHeading
          title="Station not found"
          description="This station is unavailable or no longer exists."
        />
      )}
      {query.isError && !missing && (
        <StationFailure retry={() => void query.refetch()} />
      )}
      {query.isSuccess && (
        <div className={classes.detailGrid}>
          <Stack gap="xl">
            <div className={classes.stationHeading}>
              <PageHeading
                title={query.data.name}
                description="Review charging equipment and station access before your visit."
              >
                {canEdit && (
                  <Anchor component={Link} to={`/stations/${stationId}/edit`}>
                    Edit station
                  </Anchor>
                )}
                <div className={classes.metadata}>
                  <IconMapPin size={19} aria-hidden="true" />
                  <Text size="sm">{query.data.address}</Text>
                </div>
              </PageHeading>
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
