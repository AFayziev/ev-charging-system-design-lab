import { Button, Card, Group, Stack, Table, Text } from "@mantine/core";
import { IconArrowLeft, IconArrowRight, IconPencil } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { PageHeading } from "@/components/PageHeading";
import { assignedStationsQuery } from "../api";
import { StationFailure, StationLoading } from "./StationPresentation";

export function AssignedStationsPage() {
  const [parameters, setParameters] = useSearchParams();
  const afterId = parameters.get("afterId");
  const query = useQuery(assignedStationsQuery(afterId));
  return (
    <Stack gap="xl">
      <PageHeading
        title="Operator area"
        description="Maintain content for your assigned stations."
      />
      {query.isPending && <StationLoading />}
      {query.isError && <StationFailure retry={() => void query.refetch()} />}
      {query.isSuccess && (
        <>
          {query.data.stations.length === 0 && (
            <Text>No assigned stations to display.</Text>
          )}
          {query.data.stations.length > 0 && (
            <Card withBorder padding={0}>
              <Table horizontalSpacing="md" verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Station</Table.Th>
                    <Table.Th visibleFrom="sm">Address</Table.Th>
                    <Table.Th ta="right">Actions</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {query.data.stations.map((station) => (
                    <Table.Tr key={station.id}>
                      <Table.Td>
                        <Text size="sm" fw={600}>
                          {station.name}
                        </Text>
                        <Text size="xs" c="dimmed" hiddenFrom="sm">
                          {station.address}
                        </Text>
                      </Table.Td>
                      <Table.Td visibleFrom="sm">
                        <Text size="sm" c="dimmed">
                          {station.address}
                        </Text>
                      </Table.Td>
                      <Table.Td ta="right" w={100}>
                        <Button
                          component={Link}
                          to={`/stations/${station.id}/edit`}
                          aria-label={`Edit ${station.name}`}
                          variant="light"
                          size="xs"
                          leftSection={
                            <IconPencil size={14} aria-hidden="true" />
                          }
                        >
                          Edit
                        </Button>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Card>
          )}
          <Group justify="space-between">
            {afterId && (
              <Button
                variant="subtle"
                mih={44}
                onClick={() => setParameters({})}
                leftSection={<IconArrowLeft size={18} aria-hidden="true" />}
              >
                First page
              </Button>
            )}
            {query.data.nextAfterId && (
              <Button
                variant="light"
                ml="auto"
                mih={44}
                rightSection={<IconArrowRight size={18} aria-hidden="true" />}
                onClick={() =>
                  setParameters({ afterId: query.data.nextAfterId! })
                }
              >
                Next page
              </Button>
            )}
          </Group>
        </>
      )}
    </Stack>
  );
}
