import {
  keepPreviousData,
  queryOptions,
  type QueryClient,
} from "@tanstack/react-query";
import axios from "axios";
import { api } from "@/lib/api";

export interface ConnectorCapability {
  readonly connectorType: string;
  readonly powerSupply: string;
  readonly ratedPowerKW: number;
}

/** A published photo, with the variants a card and a full view load. */
export interface StationPhoto {
  readonly id: string;
  readonly thumbnailUrl: string;
  readonly displayUrl: string;
}

export interface StationSummary {
  readonly id: string;
  readonly name: string;
  readonly address: string;
  readonly chargerCount: number;
  readonly connectorCount: number;
  readonly capabilities: readonly ConnectorCapability[];
  readonly coverPhoto: StationPhoto | null;
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
  /** Published photos in gallery order. */
  readonly photos: readonly StationPhoto[];
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

export interface ConnectorAvailability {
  readonly id: string;
  readonly available: boolean;
}

/** Advisory: what a connector looks like now, never a promise that a booking or start will be accepted. */
export interface StationAvailability {
  readonly id: string;
  readonly connectors: readonly ConnectorAvailability[];
}

export interface StationAvailabilityList {
  readonly stations: readonly StationAvailability[];
}

export const stationKeys = {
  lists: ["stations", "list"] as const,
  list: (afterId: string | null) => ["stations", "list", afterId] as const,
  searchOptions: ["stations", "search-options"] as const,
  search: (request: StationSearchRequest) =>
    ["stations", "list", "search", request] as const,
  detail: (id: string) => ["stations", "detail", id] as const,
  availabilityLists: ["stations", "availability"] as const,
  availability: (ids: readonly string[]) =>
    ["stations", "availability", ids] as const,
  editor: (id: string) => ["stations", "editor", id] as const,
  gallery: (id: string) => ["stations", "gallery", id] as const,
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

export type GalleryItemState =
  "uploading" | "processing" | "published" | "failed";

/** The photo waiting to take the place of a published one. */
export interface GalleryReplacement {
  readonly id: string;
  readonly state: GalleryItemState;
  readonly failureCode: string | null;
}

export interface GalleryItem {
  readonly id: string;
  readonly position: number;
  readonly state: GalleryItemState;
  readonly failureCode: string | null;
  readonly thumbnailUrl: string | null;
  readonly replacement: GalleryReplacement | null;
}

export interface GalleryLimits {
  readonly maximumPhotosPerStation: number;
  readonly maximumPhotoBytes: number;
  readonly maximumPhotoPixels: number;
  readonly photoContentTypes: readonly string[];
  readonly uploadUrlLifetimeSeconds: number;
}

export interface GalleryEditor {
  readonly stationId: string;
  readonly galleryVersion: number;
  readonly items: readonly GalleryItem[];
  readonly limits: GalleryLimits;
}

export interface PhotoUpload {
  readonly itemId: string;
  readonly uploadUrl: string;
  readonly galleryVersion: number;
}

const galleryPath = (stationId: string) =>
  `/api/stations/${encodeURIComponent(stationId)}/media`;

const csrf = (antiforgeryToken: string) => ({
  "X-CSRF-TOKEN": antiforgeryToken,
});

export async function getStationGallery(
  stationId: string,
  signal?: AbortSignal,
): Promise<GalleryEditor> {
  const response = await api.get<GalleryEditor>(
    `${galleryPath(stationId)}/edit`,
    { signal },
  );
  return response.data;
}

export async function startPhotoUpload(
  stationId: string,
  upload: {
    readonly galleryVersion: number;
    readonly contentType: string;
    readonly byteLength: number;
    readonly replacesItemId?: string;
  },
  antiforgeryToken: string,
): Promise<PhotoUpload> {
  const response = await api.post<PhotoUpload>(
    `${galleryPath(stationId)}/photo-uploads`,
    upload,
    { headers: csrf(antiforgeryToken) },
  );
  return response.data;
}

/**
 * Sends the bytes straight to storage through the signed URL. This is not an API call: it carries no
 * session, no antiforgery header, and only the Content-Type the URL was signed for.
 */
export async function uploadToStorage(
  uploadUrl: string,
  file: File,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<void> {
  // Storage that accepts the connection but never answers leaves no error to show, so a transfer that
  // makes no progress for a while is abandoned. A slow transfer that keeps moving is left alone.
  const stalled = new AbortController();
  let stallTimer: ReturnType<typeof setTimeout> | undefined;
  const watch = () => {
    clearTimeout(stallTimer);
    stallTimer = setTimeout(() => stalled.abort(), uploadStallMs);
  };
  watch();
  try {
    await axios.put(uploadUrl, file, {
      headers: { "Content-Type": file.type },
      signal: AbortSignal.any([signal, stalled.signal]),
      onUploadProgress: (event) => {
        watch();
        if (event.total) onProgress(event.loaded / event.total);
      },
    });
  } finally {
    clearTimeout(stallTimer);
  }
}

const uploadStallMs = 20_000;

export async function completePhotoUpload(
  stationId: string,
  itemId: string,
  antiforgeryToken: string,
): Promise<void> {
  await api.post(
    `${galleryPath(stationId)}/${encodeURIComponent(itemId)}/upload-completion`,
    undefined,
    { headers: csrf(antiforgeryToken) },
  );
}

export async function reorderGallery(
  stationId: string,
  galleryVersion: number,
  itemIds: readonly string[],
  antiforgeryToken: string,
): Promise<void> {
  await api.put(
    `${galleryPath(stationId)}/order`,
    { galleryVersion, itemIds },
    { headers: csrf(antiforgeryToken) },
  );
}

export async function removeGalleryItem(
  stationId: string,
  itemId: string,
  antiforgeryToken: string,
): Promise<void> {
  await api.delete(`${galleryPath(stationId)}/${encodeURIComponent(itemId)}`, {
    headers: csrf(antiforgeryToken),
  });
}

const galleryPollingMs = 2_000;

const isSettling = (state: GalleryItemState) =>
  state === "uploading" || state === "processing";

export const stationGalleryQuery = (stationId: string) =>
  queryOptions({
    queryKey: stationKeys.gallery(stationId),
    queryFn: ({ signal }) => getStationGallery(stationId, signal),
    // Photos are optimized in the background, so the editor follows them until they settle.
    refetchInterval: (query) =>
      query.state.data?.items.some(
        (item) =>
          isSettling(item.state) ||
          (item.replacement !== null && isSettling(item.replacement.state)),
      )
        ? galleryPollingMs
        : false,
    retry: false,
    refetchOnWindowFocus: false,
  });

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
      headers: csrf(antiforgeryToken),
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

export async function getStationAvailability(
  ids: readonly string[],
  signal?: AbortSignal,
): Promise<StationAvailabilityList> {
  const response = await api.get<StationAvailabilityList>(
    "/api/stations/availability",
    { params: { ids }, signal },
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

const byStationId = (data: StationAvailabilityList) =>
  new Map(data.stations.map((station) => [station.id, station]));

export const stationAvailabilityQuery = (ids: readonly string[]) => {
  // The order of the ids does not change the answer, so it does not change the cache entry.
  const sorted = [...ids].sort();
  return queryOptions({
    queryKey: stationKeys.availability(sorted),
    queryFn: ({ signal }) => getStationAvailability(sorted, signal),
    select: byStationId,
    // The counts stay on the cards, not blanked, while the next page's are read.
    placeholderData: keepPreviousData,
    enabled: sorted.length > 0,
    staleTime: 30_000,
    retry: false,
  });
};

/** A booking or session change moves what the availability counts say. */
export const invalidateStationAvailability = (client: QueryClient) =>
  client.invalidateQueries({ queryKey: stationKeys.availabilityLists });
