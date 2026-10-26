import { keepPreviousData, queryOptions } from "@tanstack/react-query";
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
  readonly connectorCount: number;
  readonly capabilities: readonly ConnectorCapability[];
}

export interface StationList {
  readonly stations: readonly StationSummary[];
  readonly nextAfterId: string | null;
}

export interface StationSearchItem extends StationSummary {
  readonly distanceKm: number | null;
}

export interface StationSearchResult {
  readonly stations: readonly StationSearchItem[];
  readonly nextCursor: string | null;
  /** Every match of the search, not the page; at least this many when not exact. */
  readonly total: number;
  readonly totalIsExact: boolean;
}

/** A search is either a cursor alone, which carries its own search, or the search parameters. */
export interface StationSearchRequest {
  readonly q?: string;
  readonly connectorType?: readonly string[];
  readonly powerSupply?: readonly string[];
  readonly minPowerKw?: number;
  readonly lat?: number;
  readonly lon?: number;
  readonly radiusKm?: number;
  readonly cursor?: string;
}

/** What the index can filter on, so the filters offer only values that can match. */
export interface StationSearchOptions {
  readonly connectorTypes: readonly string[];
  readonly powerSupplies: readonly string[];
  readonly ratedPowersKw: readonly number[];
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
    readonly providerName: string | null;
    readonly connectors: readonly (ConnectorCapability & {
      readonly id: string;
      readonly number: number;
    })[];
  }[];
}

export const stationKeys = {
  lists: ["stations", "list"] as const,
  list: (afterId: string | null) => ["stations", "list", afterId] as const,
  searchOptions: ["stations", "search-options"] as const,
  search: (request: StationSearchRequest) =>
    ["stations", "list", "search", request] as const,
  detail: (id: string) => ["stations", "detail", id] as const,
  editor: (id: string) => ["stations", "editor", id] as const,
  assignedLists: ["stations", "assigned"] as const,
  assigned: (afterId: string | null) =>
    ["stations", "assigned", afterId] as const,
};

export interface StationEditor {
  readonly id: string;
  readonly version: number;
  readonly name: string;
  readonly address: string;
  readonly accessInstructions: string;
}

export type StationEdit = Omit<StationEditor, "id">;

export interface AssignedStationList {
  readonly stations: readonly Pick<StationSummary, "id" | "name" | "address">[];
  readonly nextAfterId: string | null;
}

export async function getStationEditor(
  id: string,
  signal?: AbortSignal,
): Promise<StationEditor> {
  const response = await api.get<StationEditor>(
    `/api/stations/${encodeURIComponent(id)}/edit`,
    { signal },
  );
  return response.data;
}

export async function updateStation(
  id: string,
  edit: StationEdit,
  antiforgeryToken: string,
  signal?: AbortSignal,
): Promise<StationEditor> {
  const response = await api.put<StationEditor>(
    `/api/stations/${encodeURIComponent(id)}`,
    edit,
    {
      headers: { "X-CSRF-TOKEN": antiforgeryToken },
      signal,
    },
  );
  return response.data;
}

export async function getAssignedStations(
  afterId: string | null,
  signal?: AbortSignal,
): Promise<AssignedStationList> {
  const response = await api.get<AssignedStationList>(
    "/api/stations/assigned",
    { params: { afterId, limit: 5 }, signal },
  );
  return response.data;
}

export const stationEditorQuery = (id: string) =>
  queryOptions({
    queryKey: stationKeys.editor(id),
    queryFn: ({ signal }) => getStationEditor(id, signal),
    retry: false,
    refetchOnWindowFocus: false,
  });

export const assignedStationsQuery = (afterId: string | null) =>
  queryOptions({
    queryKey: stationKeys.assigned(afterId),
    queryFn: ({ signal }) => getAssignedStations(afterId, signal),
    retry: false,
  });

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

export async function searchStations(
  request: StationSearchRequest,
  signal?: AbortSignal,
): Promise<StationSearchResult> {
  const response = await api.get<StationSearchResult>("/api/search/stations", {
    params: request,
    signal,
  });
  return response.data;
}

export async function getStationSearchOptions(
  signal?: AbortSignal,
): Promise<StationSearchOptions> {
  const response = await api.get<StationSearchOptions>(
    "/api/search/stations/options",
    { signal },
  );
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

export const stationSearchQuery = (request: StationSearchRequest) =>
  queryOptions({
    queryKey: stationKeys.search(request),
    queryFn: ({ signal }) => searchStations(request, signal),
    // The results stay on screen, dimmed, while the next search loads.
    placeholderData: keepPreviousData,
    retry: false,
  });

export const stationSearchOptionsQuery = (enabled: boolean) =>
  queryOptions({
    queryKey: stationKeys.searchOptions,
    queryFn: ({ signal }) => getStationSearchOptions(signal),
    enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });

export const stationDetailQuery = (id: string) =>
  queryOptions({
    queryKey: stationKeys.detail(id),
    queryFn: ({ signal }) => getStation(id, signal),
    retry: false,
  });
