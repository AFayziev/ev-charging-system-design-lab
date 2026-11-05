import {
  Badge,
  Button,
  Card,
  EmptyState,
  Group,
  Stack,
  Table,
  Text,
} from "@mantine/core";
import {
  IconArrowLeft,
  IconArrowRight,
  IconCircleCheck,
  IconRefresh,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useSearchParams } from "react-router";
import { FailureAlert } from "@/components/StatusAlert";
import { PageHeading } from "@/components/PageHeading";
import { getHttpFailure } from "@/lib/http-errors";
import { formatClockWithSeconds } from "@/lib/time-formatting";
import {
  formatEquipment,
  unsettledOperationsQuery,
  type UnsettledOperation,
} from "../api";
import { ReleaseStartDialog } from "./ReleaseStartDialog";

export function UnsettledOperationsPage() {
  const [parameters, setParameters] = useSearchParams();
  const afterId = parameters.get("afterId");
  const query = useQuery(unsettledOperationsQuery(afterId));
  const [releasing, setReleasing] = useState<UnsettledOperation | null>(null);

  return (
    <Stack gap="xl">
      <PageHeading
        title="Unsettled charging operations"
        description="Starts and stops on your assigned stations that the station has not confirmed or rejected."
      />
      {query.isPending && (
        <Text role="status" py="xl">
          Loading unsettled operations…
        </Text>
      )}
      {query.isError && (
        <FailureAlert failure={getHttpFailure(query.error)}>
          <Button
            variant="light"
            color="red"
            onClick={() => void query.refetch()}
            w="fit-content"
            mih={44}
          >
            Try again
          </Button>
        </FailureAlert>
      )}
      {query.isSuccess && (
        <>
          <Group justify="flex-end">
            <Button
              variant="default"
              mih={44}
              loading={query.isFetching}
              leftSection={<IconRefresh size={18} aria-hidden="true" />}
              onClick={() => void query.refetch()}
            >
              Refresh
            </Button>
          </Group>
          {query.data.operations.length === 0 && (
            <Card withBorder padding="xl">
              <EmptyState icon={<IconCircleCheck aria-hidden />}>
                <EmptyState.Title order={2}>
                  No unsettled operations
                </EmptyState.Title>
                <EmptyState.Description>
                  Every start and stop on your assigned stations has a result.
                </EmptyState.Description>
              </EmptyState>
            </Card>
          )}
          {query.data.operations.map((operation) => (
            <UnsettledOperationCard
              key={operation.id}
              operation={operation}
              onRelease={() => setReleasing(operation)}
            />
          ))}
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
      {releasing && (
        <ReleaseStartDialog
          operation={releasing}
          onClose={() => setReleasing(null)}
        />
      )}
    </Stack>
  );
}

function UnsettledOperationCard({
  operation,
  onRelease,
}: {
  operation: UnsettledOperation;
  onRelease: () => void;
}) {
  const label = formatEquipment(operation.equipment);
  return (
    <Card
      withBorder
      component="section"
      aria-label={`${operation.kind} at ${label}`}
    >
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Stack gap={4}>
            <Group gap="xs">
              <Text fw={600}>{label}</Text>
              <Badge variant="light" color="gray">
                {operation.kind === "start" ? "Start" : "Stop"}
              </Badge>
              <Badge
                variant="light"
                color={operation.outcome === "uncertain" ? "orange" : "blue"}
              >
                {operation.outcome === "uncertain" ? "Uncertain" : "Pending"}
              </Badge>
            </Group>
            <Text size="sm" c="dimmed">
              Accepted {formatClockWithSeconds(operation.acceptedAt)} · Last
              request {formatClockWithSeconds(operation.lastRequestedAt)}
            </Text>
            <Text size="sm" c="dimmed">
              Session {operation.sessionId} · Operation {operation.id}
            </Text>
            <Text size="sm">{releaseStatus(operation)}</Text>
          </Stack>
          {operation.canRelease && (
            <Button
              color="red"
              variant="light"
              mih={44}
              aria-label={`Release connector at ${label}`}
              onClick={onRelease}
            >
              Release connector
            </Button>
          )}
        </Group>
        <Table.ScrollContainer minWidth={640}>
          <Table horizontalSpacing="sm" verticalSpacing="xs" fz="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Request</Table.Th>
                <Table.Th>Sent</Table.Th>
                <Table.Th>Result</Table.Th>
                <Table.Th>Provider</Table.Th>
                <Table.Th>Provider equipment</Table.Th>
                <Table.Th>Provider command / session</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {operation.attempts.map((attempt) => (
                <Table.Tr key={attempt.number}>
                  <Table.Td>{attempt.number}</Table.Td>
                  <Table.Td>
                    {formatClockWithSeconds(attempt.authorizedAt)}
                  </Table.Td>
                  <Table.Td>
                    {attempt.finishedAt === null
                      ? "No response yet"
                      : [
                          attempt.outcome,
                          attempt.failureCode,
                          attempt.responseStatusCode,
                        ]
                          .filter((value) => value !== null)
                          .join(" · ")}
                  </Table.Td>
                  <Table.Td>{attempt.integrationKey}</Table.Td>
                  <Table.Td>
                    {attempt.providerStationId} / {attempt.providerChargerId} /{" "}
                    {attempt.providerConnectorId}
                  </Table.Td>
                  <Table.Td>
                    {attempt.providerCommandId ?? "—"} /{" "}
                    {attempt.providerSessionId ?? "—"}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Stack>
    </Card>
  );
}

function releaseStatus(operation: UnsettledOperation) {
  if (operation.kind === "stop")
    return "Stops are settled by status checks or the station's final reading.";
  if (operation.hasObservations)
    return "The station has sent readings for this session, so the connector is not released.";
  if (operation.canRelease)
    return "No readings arrived within the release window. Check the charger, then release the connector.";
  if (operation.outcome === "pending")
    return "The station's answer is still awaited.";
  return `Automatic checks continue. The connector can be released from ${formatClockWithSeconds(operation.releasableAt!)} if no readings arrive.`;
}
