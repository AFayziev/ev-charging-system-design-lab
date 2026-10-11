import {
  Button,
  Card,
  EmptyState,
  Group,
  SimpleGrid,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import {
  IconArrowLeft,
  IconArrowRight,
  IconChargingPile,
  IconPlug,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { PageHeading } from "@/components/PageHeading";
import { stationListQuery } from "../api";
import { StationPhoto } from "./StationPhoto";
import {
  capabilityLabel,
  StationLoading,
  StationFailure,
} from "./StationPresentation";
import classes from "./Stations.module.css";

export function StationListPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const afterId = searchParams.get("after");
  const query = useQuery(stationListQuery(afterId));

  return (
    <Stack gap="xl">
      <PageHeading
        title="Find a station"
        description="Browse fictional lab stations and check equipment and access details."
      />
      {query.isPending && <StationLoading />}
      {query.isError && <StationFailure retry={() => void query.refetch()} />}
      {query.isSuccess && (
        <>
          {query.data.stations.length === 0 ? (
            <Card withBorder padding="xl">
              <EmptyState icon={<IconChargingPile aria-hidden />}>
                <EmptyState.Title order={2}>
                  No stations to display
                </EmptyState.Title>
                <EmptyState.Description>
                  Station information will appear here when it is published.
                </EmptyState.Description>
              </EmptyState>
            </Card>
          ) : (
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
              {query.data.stations.map((station) => {
                const maximumPower =
                  station.capabilities.length > 0
                    ? Math.max(
                        ...station.capabilities.map(
                          (capability) => capability.ratedPowerKW,
                        ),
                      )
                    : null;
                return (
                  <Card
                    key={station.id}
                    withBorder
                    className={classes.catalogCard}
                    component="article"
                  >
                    <StationPhoto />
                    <Stack className={classes.cardBody}>
                      <Stack gap={4}>
                        <Title order={2} size="h3">
                          {station.name}
                        </Title>
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
                          <div
                            className={classes.metadata}
                            key={capabilityLabel(capability)}
                          >
                            <IconPlug size={17} aria-hidden="true" />
                            <Text size="sm">{capabilityLabel(capability)}</Text>
                          </div>
                        ))}
                        <div className={classes.metadata}>
                          <IconChargingPile size={17} aria-hidden="true" />
                          <Text size="sm">
                            {station.chargerCount}{" "}
                            {station.chargerCount === 1
                              ? "charger"
                              : "chargers"}{" "}
                            · {station.evseCount}{" "}
                            {station.evseCount === 1 ? "EVSE" : "EVSEs"} ·{" "}
                            {station.connectorCount}{" "}
                            {station.connectorCount === 1
                              ? "connector"
                              : "connectors"}
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
                        rightSection={
                          <IconArrowRight size={18} aria-hidden="true" />
                        }
                      >
                        View station
                      </Button>
                    </Stack>
                  </Card>
                );
              })}
            </SimpleGrid>
          )}
          <Group justify="space-between">
            {afterId !== null && (
              <Button
                variant="subtle"
                onClick={() => setSearchParams({})}
                mih={44}
                leftSection={<IconArrowLeft size={18} aria-hidden="true" />}
              >
                First page
              </Button>
            )}
            {query.data.nextAfterId !== null && (
              <Button
                variant="light"
                ml="auto"
                onClick={() =>
                  setSearchParams({ after: String(query.data.nextAfterId) })
                }
                mih={44}
                rightSection={<IconArrowRight size={18} aria-hidden="true" />}
              >
                Next stations
              </Button>
            )}
          </Group>
        </>
      )}
    </Stack>
  );
}
