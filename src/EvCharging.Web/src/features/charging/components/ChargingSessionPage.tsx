import { AreaChart } from "@mantine/charts";
import "@mantine/charts/styles.css";
import {
  Button,
  Group,
  Loader,
  Paper,
  SimpleGrid,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { useInterval } from "@mantine/hooks";
import { IconRefresh } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { memo, useEffect, useMemo, useState } from "react";
import { useParams } from "react-router";
import { BackLink } from "@/components/BackLink";
import { FailureAlert, StatusAlert } from "@/components/StatusAlert";
import { formatClockLabel, formatElapsed } from "@/lib/time-formatting";
import { reservationQuery } from "@/features/reservations/api";
import { getHttpFailure, getHttpStatus } from "@/lib/http-errors";
import {
  chargingPowerQuery,
  chargingProgressQuery,
  isLive,
  powerPollingMs,
  shouldPoll,
  type ChargingReading,
  type PowerBucket,
} from "../api";
import { getProgressPresentation } from "../progress-presentation";

const noBuckets: readonly PowerBucket[] = [];

export function ChargingSessionPage() {
  const { sessionId = "" } = useParams();
  const progress = useQuery(chargingProgressQuery(sessionId));
  const data = progress.data;
  // The clock only ticks while the session is live, to advance the elapsed time between answers.
  const now = useNow(data !== undefined && isLive(data));
  const polling = data !== undefined && shouldPoll(data);
  const power = useQuery({
    ...chargingPowerQuery(sessionId, polling ? powerPollingMs : false),
    enabled: data !== undefined,
  });
  const reservation = useQuery({
    ...reservationQuery(data?.reservationId ?? ""),
    enabled: data !== undefined,
  });

  if (progress.isPending) {
    return <Loader aria-label="Loading charging session" />;
  }
  if (data === undefined) {
    const failure = getHttpFailure(progress.error, "read");
    return (
      <Stack>
        <BackLink to="/reservations">Back to reservations</BackLink>
        <FailureAlert
          failure={{
            ...failure,
            title:
              getHttpStatus(progress.error) === 404
                ? "Charging session not found"
                : failure.title,
          }}
        />
      </Stack>
    );
  }

  const presentation = getProgressPresentation(data);
  const paused = isLive(data) && data.isSilentPastLimit;
  // The server counts the elapsed time as of its answer; a live session keeps counting until the next one.
  const elapsedMs =
    data.elapsedSeconds === null
      ? null
      : data.elapsedSeconds * 1000 +
        (isLive(data) ? Math.max(0, now - progress.dataUpdatedAt) : 0);
  return (
    <Stack gap="lg">
      <BackLink to="/reservations">Back to reservations</BackLink>
      <div>
        <Title order={1}>Charging session</Title>
        {reservation.data && (
          <Text c="dimmed">
            {reservation.data.stationName} · {reservation.data.chargerName},
            connector {reservation.data.connectorNumber}
          </Text>
        )}
      </div>
      <StatusAlert tone={presentation.tone} title={presentation.title}>
        {presentation.text}
      </StatusAlert>
      {paused && (
        <StatusAlert tone="neutral" title="Updates paused">
          <Group justify="space-between" wrap="wrap">
            <Text size="sm">
              The charger has been silent for a long time, so we stopped
              checking.
            </Text>
            <Button
              size="xs"
              variant="default"
              leftSection={<IconRefresh size={14} aria-hidden />}
              loading={progress.isFetching}
              onClick={() => void progress.refetch()}
            >
              Refresh
            </Button>
          </Group>
        </StatusAlert>
      )}
      <Metrics reading={data.reading} elapsedMs={elapsedMs} />
      <Paper withBorder p="md" component="section" aria-label="Power over time">
        <Title order={2} size="h4" mb="sm">
          Power over time
        </Title>
        <PowerChart buckets={power.data?.buckets ?? noBuckets} />
      </Paper>
    </Stack>
  );
}

function Metrics({
  reading,
  elapsedMs,
}: {
  reading: ChargingReading | null;
  elapsedMs: number | null;
}) {
  return (
    <SimpleGrid cols={{ base: 1, xs: 3 }}>
      <Metric
        label="Energy"
        value={reading ? `${reading.energyKWh.toFixed(2)} kWh` : "–"}
      />
      <Metric
        label="Power"
        value={reading ? `${reading.powerKw.toFixed(1)} kW` : "–"}
      />
      <Metric
        label="Elapsed"
        value={elapsedMs === null ? "–" : formatElapsed(elapsedMs)}
      />
    </SimpleGrid>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Paper withBorder p="md">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text fz="xl" fw={700}>
        {value}
      </Text>
    </Paper>
  );
}

const powerSeries = [
  { name: "power", label: "Average power (kW)", color: "teal.7" },
];

// The page re-renders every second while live; the chart only when its buckets change.
const PowerChart = memo(function PowerChart({
  buckets,
}: {
  buckets: readonly PowerBucket[];
}) {
  const data = useMemo(
    () =>
      buckets.map((bucket) => ({
        startsAt: bucket.startsAt,
        power: Number(bucket.averagePowerKw.toFixed(1)),
      })),
    [buckets],
  );
  if (data.length === 0) {
    return (
      <Text c="dimmed" size="sm">
        The power history appears once the charger reports.
      </Text>
    );
  }
  return (
    <AreaChart
      h={240}
      data={data}
      dataKey="startsAt"
      xAxisProps={{ tickFormatter: formatClockLabel }}
      tooltipProps={{ labelFormatter: formatClockLabel }}
      series={powerSeries}
      withDots
      withYAxis
      yAxisLabel="kW"
      curveType="linear"
    />
  );
});

/** The current time, refreshed every second while active. */
function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now());
  const { start, stop } = useInterval(() => setNow(Date.now()), 1_000);
  useEffect(() => {
    if (active) start();
    return stop;
  }, [active, start, stop]);
  return now;
}
