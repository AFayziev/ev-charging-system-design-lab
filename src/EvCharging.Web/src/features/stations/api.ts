import { queryOptions } from "@tanstack/react-query";
import { api } from "@/lib/api";

export interface ConnectorCapability {
  readonly connectorType: string;
  readonly powerSupply: string;
  readonly ratedPowerKW: number;
}

export interface StationSummary {
  readonly id: string;
  readonly name: string;
  readonly address: string;
  readonly chargerCount: number;
  readonly evseCount: number;
  readonly connectorCount: number;
  readonly capabilities: readonly ConnectorCapability[];
}

export interface StationList {
  readonly stations: readonly StationSummary[];
  readonly nextAfterId: string | null;
}

export interface StationDetail {
  readonly id: string;
  readonly name: string;
  readonly address: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly timeZoneId: string;
  readonly accessInstructions: string;
  readonly chargers: readonly {
    readonly id: string;
    readonly name: string;
    readonly evses: readonly {
      readonly id: string;
      readonly number: number;
      readonly connectors: readonly (ConnectorCapability & {
        readonly id: string;
        readonly number: number;
      })[];
    }[];
  }[];
}

export const stationKeys = {
  list: (afterId: string | null) => ["stations", "list", afterId] as const,
  detail: (id: string) => ["stations", "detail", id] as const,
};

export async function getStations(
  afterId: string | null,
  signal?: AbortSignal,
): Promise<StationList> {
  const response = await api.get<StationList>("/api/stations", {
    params: { afterId },
    signal,
  });
  return response.data;
}

export async function getStation(
  id: string,
  signal?: AbortSignal,
): Promise<StationDetail> {
  const response = await api.get<StationDetail>(
    `/api/stations/${encodeURIComponent(id)}`,
    { signal },
  );
  return response.data;
}

export const stationListQuery = (afterId: string | null) =>
  queryOptions({
    queryKey: stationKeys.list(afterId),
    queryFn: ({ signal }) => getStations(afterId, signal),
    retry: false,
  });

export const stationDetailQuery = (id: string) =>
  queryOptions({
    queryKey: stationKeys.detail(id),
    queryFn: ({ signal }) => getStation(id, signal),
    retry: false,
  });
