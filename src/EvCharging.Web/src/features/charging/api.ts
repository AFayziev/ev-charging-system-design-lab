import { queryOptions } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type {
  CommandOutcome,
  SessionLifecycle,
} from "@/features/reservations/api";

export type ObservationState =
  "available" | "charging" | "stopped" | "faulted" | "offline" | "unknown";

export interface ChargingReading {
  readonly state: ObservationState;
  readonly powerKw: number;
  readonly energyKWh: number;
  readonly isFinalEnergy: boolean;
  readonly occurredAt: string;
}

export interface ChargingProgress {
  readonly sessionId: string;
  readonly reservationId: string;
  readonly lifecycle: SessionLifecycle;
  readonly startedAt: string | null;
  /** The end of a completed session, frozen with its final usage. */
  readonly completedAt: string | null;
  readonly finalEnergyKWh: number | null;
  readonly reading: ChargingReading | null;
  readonly isStale: boolean;
  /** A running session silent for so long that it is no longer polled. */
  readonly isSilentPastLimit: boolean;
  /** How long the session had run when the server answered; null while that is unknown. */
  readonly elapsedSeconds: number | null;
}

export interface PowerBucket {
  readonly startsAt: string;
  readonly averagePowerKw: number;
  readonly maximumPowerKw: number;
  readonly lastEnergyKWh: number;
}

export interface ChargingPower {
  readonly buckets: readonly PowerBucket[];
}

/** How often the progress and the power history are read while a session reports. */
export const progressPollingMs = 5_000;
export const powerPollingMs = 30_000;

function isRunning(progress: ChargingProgress) {
  return (
    progress.lifecycle === "starting" ||
    progress.lifecycle === "active" ||
    progress.lifecycle === "stopping"
  );
}

export function isLive(progress: ChargingProgress) {
  return isRunning(progress) && !progress.reading?.isFinalEnergy;
}

/**
 * A running session is polled until it completes or the server reports its source silent for too long. The final
 * reading does not end the polling, because the session completes after it.
 */
export function shouldPoll(progress: ChargingProgress) {
  return isRunning(progress) && !progress.isSilentPastLimit;
}

/** Adds the fetched buckets to the held ones; the fetched ones replace any held bucket from their start on. */
export function mergePowerBuckets(
  held: readonly PowerBucket[],
  fetched: readonly PowerBucket[],
): PowerBucket[] {
  if (fetched.length === 0) return [...held];
  const firstFetched = Date.parse(fetched[0].startsAt);
  return [
    ...held.filter((bucket) => Date.parse(bucket.startsAt) < firstFetched),
    ...fetched,
  ];
}

export const chargingKeys = {
  progress: (sessionId: string) => ["charging", sessionId, "progress"] as const,
  power: (sessionId: string) => ["charging", sessionId, "power"] as const,
  unsettledLists: ["charging", "unsettled"] as const,
  unsettled: (afterId: string | null) =>
    ["charging", "unsettled", afterId] as const,
};

export interface UnsettledOperationAttempt {
  readonly number: number;
  readonly authorizedAt: string;
  readonly finishedAt: string | null;
  readonly outcome: CommandOutcome | null;
  readonly failureCode: string | null;
  readonly responseStatusCode: number | null;
  readonly integrationKey: string;
  readonly providerStationId: string;
  readonly providerChargerId: string;
  readonly providerConnectorId: string;
  readonly providerCommandId: string | null;
  readonly providerSessionId: string | null;
}

export interface ChargingEquipment {
  readonly stationName: string;
  readonly chargerName: string;
  readonly connectorNumber: number;
}

export function formatEquipment(equipment: ChargingEquipment) {
  return `${equipment.stationName} · ${equipment.chargerName} · Connector ${equipment.connectorNumber}`;
}

export interface ReleasedChargingStart {
  readonly id: string;
  readonly releasedAt: string;
}

/** A start or stop on an assigned station that no evidence has settled yet. */
export interface UnsettledOperation {
  readonly id: string;
  readonly kind: "start" | "stop";
  readonly outcome: CommandOutcome;
  readonly acceptedAt: string;
  readonly sessionId: string;
  readonly equipment: ChargingEquipment;
  readonly lastRequestedAt: string;
  readonly hasObservations: boolean;
  /** When a start's release window ends; null for a stop. */
  readonly releasableAt: string | null;
  /** Decided by the server, so the device clock does not matter. */
  readonly canRelease: boolean;
  readonly attempts: readonly UnsettledOperationAttempt[];
}

export interface UnsettledOperationList {
  readonly operations: readonly UnsettledOperation[];
  readonly nextAfterId: string | null;
}

export async function getUnsettledOperations(
  afterId: string | null,
  signal?: AbortSignal,
): Promise<UnsettledOperationList> {
  const response = await api.get<UnsettledOperationList>(
    "/api/charging-operations/unsettled",
    { params: { afterId }, signal },
  );
  return response.data;
}

export async function releaseChargingStart(
  operationId: string,
  antiforgeryToken: string,
): Promise<ReleasedChargingStart> {
  const response = await api.post<ReleasedChargingStart>(
    `/api/charging-operations/${encodeURIComponent(operationId)}/release`,
    undefined,
    { headers: { "X-CSRF-TOKEN": antiforgeryToken } },
  );
  return response.data;
}

export const unsettledOperationsQuery = (afterId: string | null) =>
  queryOptions({
    queryKey: chargingKeys.unsettled(afterId),
    queryFn: ({ signal }) => getUnsettledOperations(afterId, signal),
    retry: false,
  });

export async function getChargingProgress(
  sessionId: string,
  signal?: AbortSignal,
): Promise<ChargingProgress> {
  const response = await api.get<ChargingProgress>(
    `/api/charging-sessions/${encodeURIComponent(sessionId)}/progress`,
    { signal },
  );
  return response.data;
}

export async function getChargingPower(
  sessionId: string,
  from: string | null,
  signal?: AbortSignal,
): Promise<ChargingPower> {
  const response = await api.get<ChargingPower>(
    `/api/charging-sessions/${encodeURIComponent(sessionId)}/power`,
    { params: from === null ? undefined : { from }, signal },
  );
  return response.data;
}

export const chargingProgressQuery = (sessionId: string) =>
  queryOptions({
    queryKey: chargingKeys.progress(sessionId),
    queryFn: ({ signal }) => getChargingProgress(sessionId, signal),
    retry: false,
    // A silent session is refreshed by hand, so returning to the tab must not restart the polling.
    refetchOnWindowFocus: false,
    refetchInterval: (query) =>
      query.state.error || !query.state.data || !shouldPoll(query.state.data)
        ? false
        : progressPollingMs,
  });

/**
 * The power history is read from the last bucket already held, which may still be open, so each request
 * carries one bucket for an unchanged session.
 */
export const chargingPowerQuery = (
  sessionId: string,
  refetchInterval: number | false,
) =>
  queryOptions({
    queryKey: chargingKeys.power(sessionId),
    queryFn: async ({ client, signal }): Promise<ChargingPower> => {
      const held = client.getQueryData<ChargingPower>(
        chargingKeys.power(sessionId),
      );
      const fetched = await getChargingPower(
        sessionId,
        held?.buckets.at(-1)?.startsAt ?? null,
        signal,
      );
      return {
        buckets: mergePowerBuckets(held?.buckets ?? [], fetched.buckets),
      };
    },
    retry: false,
    refetchOnWindowFocus: false,
    refetchInterval,
  });
