import {
  Anchor,
  Button,
  Card,
  Loader,
  Radio,
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
import { Activity } from "react";
import { HttpStatusCode } from "axios";
import {
  createPath,
  Link,
  useLocation,
  useParams,
  useSearchParams,
} from "react-router";
import { BackLink } from "@/components/BackLink";
import { PageHeading } from "@/components/PageHeading";
import { getHttpStatus } from "@/lib/http-errors";
import { getSignInUrl, sessionQuery } from "@/features/auth/api";
import { PlatformRole } from "@/features/auth/PlatformRole";
import { BookingForm } from "@/features/reservations/components/BookingForm";
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
  const session = useQuery(sessionQuery);
  const location = useLocation();
  const [search, setSearch] = useSearchParams();
  const canBook =
    session.isSuccess && session.data.user?.roles.includes(PlatformRole.Driver);
  const connectorId =
    search.get("connectorId") ??
    query.data?.chargers.flatMap((charger) => charger.connectors)[0]?.id ??
    "";
  const selectConnector = (id: string) => {
    const parameters = new URLSearchParams(search);
    parameters.set("connectorId", id);
    setSearch(parameters, { replace: true });
  };
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
        <StationFailure
          error={query.error}
          retry={() => void query.refetch()}
        />
      )}
      {query.isSuccess && (
        <div className={classes.detailGrid}>
          <Stack gap="xl">
            <div className={classes.stationHeading}>
              <div>
                <PageHeading
                  title={query.data.name}
                  description={
                    query.data.accessInstructions ||
                    "No access instructions published."
                  }
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
                <div className={classes.stationLinks}>
                  <div className={classes.metadata}>
                    <IconClock size={16} aria-hidden="true" />
                    <Text size="xs">Time zone: {query.data.timeZoneId}</Text>
                  </div>
                  <Anchor
                    href={mapsUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    size="xs"
                  >
                    Open in maps
                  </Anchor>
                </div>
              </div>
              <StationPhoto />
            </div>
            <Radio.Group
              value={connectorId}
              onChange={selectConnector}
              name="station-connector"
              aria-label="Charging connector"
            >
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
                    {charger.connectors.length === 0 && (
                      <Text size="sm" c="dimmed" p="md">
                        No connectors listed.
                      </Text>
                    )}
                    <div className={classes.connectors}>
                      {charger.connectors.map((connector) => (
                        <Radio.Card
                          key={connector.id}
                          className={classes.connector}
                          value={connector.id}
                          withBorder={false}
                          aria-label={`${charger.name} · Connector ${connector.number} · ${capabilityLabel(connector)}`}
                        >
                          <ThemeIcon variant="light" size={36} radius="xl">
                            <IconPlug size={21} aria-hidden="true" />
                          </ThemeIcon>
                          <Stack gap={2} className={classes.connectorBody}>
                            <Text size="sm" fw={600}>
                              Connector {connector.number}
                            </Text>
                            <Text size="sm" c="dimmed">
                              {capabilityLabel(connector)}
                            </Text>
                          </Stack>
                        </Radio.Card>
                      ))}
                    </div>
                  </Card>
                ))}
              </Stack>
            </Radio.Group>
          </Stack>
          <aside
            className={classes.bookingSidebar}
            aria-label="Connector booking"
          >
            <Activity mode={canBook ? "visible" : "hidden"}>
              <BookingForm
                key={query.data.id}
                station={query.data}
                connectorId={connectorId}
                onConnectorChange={selectConnector}
              />
            </Activity>
            {!canBook && (
              <Card withBorder padding="lg">
                <Stack gap="md">
                  <Title order={2} size="h4">
                    Book a connector
                  </Title>
                  {session.isPending && (
                    <Loader size="sm" aria-label="Loading booking account" />
                  )}
                  {session.isError && (
                    <Text size="sm" c="dimmed">
                      Booking is unavailable while your account cannot be
                      checked.
                    </Text>
                  )}
                  {session.isSuccess &&
                    (session.data.signedIn ? (
                      <Text size="sm" c="dimmed">
                        Reservations require a driver account.
                      </Text>
                    ) : (
                      <>
                        <Text size="sm" c="dimmed">
                          Sign in with a driver account to reserve a connector
                          and choose your time slot.
                        </Text>
                        <Button
                          component="a"
                          href={getSignInUrl(createPath(location))}
                          fullWidth
                        >
                          Sign in to book
                        </Button>
                      </>
                    ))}
                </Stack>
              </Card>
            )}
          </aside>
        </div>
      )}
    </Stack>
  );
}
