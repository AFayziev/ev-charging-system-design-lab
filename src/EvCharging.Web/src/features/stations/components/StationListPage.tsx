import {
  Button,
  Card,
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
    <Stack gap="xl" py={{ base: "sm", sm: "lg" }}>
      <Stack gap={8}>
        <Title order={1}>Find a station</Title>
        <Text c="dimmed">
          Browse charging stations and check equipment and access details.
        </Text>
      </Stack>
      {query.isPending ? (
        <StationLoading />
      ) : query.isError ? (
        <StationFailure retry={() => void query.refetch()} />
      ) : (
        <>
          {query.data.stations.length === 0 ? (
            <Card withBorder padding="xl">
              <Title order={2} size="h3">
                No stations to display
              </Title>
              <Text c="dimmed" mt="xs">
                Station information will appear here when it is published.
              </Text>
            </Card>
          ) : (
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
              {query.data.stations.map((station) => (
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
              ))}
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
