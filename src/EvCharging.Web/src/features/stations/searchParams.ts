import type { StationSearchRequest } from "./api";

export const minimumSearchTextLength = 2;
export const maximumSearchTextLength = 100;
export const defaultRadiusKm = 10;

export const radiusOptions = [5, 10, 25, 50] as const;

/** What the driver asked for; it lives in the URL, except the position, which stays in the browser. */
export interface StationSearchFilters {
  readonly text: string;
  readonly connectorTypes: readonly string[];
  readonly powerSupplies: readonly string[];
  readonly minPowerKw: number | null;
  readonly nearMe: boolean;
  /** Null means no radius. */
  readonly radiusKm: number | null;
}

export function readSearchFilters(
  parameters: URLSearchParams,
): StationSearchFilters {
  const minimumPower = Number(parameters.get("minKw"));
  const radius = parameters.get("radius");
  return {
    text: parameters.get("q") ?? "",
    connectorTypes: parameters.getAll("type"),
    powerSupplies: parameters.getAll("supply"),
    minPowerKw: minimumPower > 0 ? minimumPower : null,
    nearMe: parameters.get("near") === "1",
    radiusKm:
      radius === "any"
        ? null
        : (radiusOptions.find((option) => String(option) === radius) ??
          defaultRadiusKm),
  };
}

export function writeSearchFilters(
  filters: StationSearchFilters,
): URLSearchParams {
  const parameters = new URLSearchParams();
  const text = filters.text.trim();
  if (text) parameters.set("q", text);
  filters.connectorTypes.forEach((value) => parameters.append("type", value));
  filters.powerSupplies.forEach((value) => parameters.append("supply", value));
  if (filters.minPowerKw !== null)
    parameters.set("minKw", String(filters.minPowerKw));
  if (filters.nearMe) {
    parameters.set("near", "1");
    if (filters.radiusKm === null) parameters.set("radius", "any");
    else if (filters.radiusKm !== defaultRadiusKm)
      parameters.set("radius", String(filters.radiusKm));
  }
  return parameters;
}

/** Text shorter than the minimum is only a hint; it is not searched. */
export function hasSearchText(text: string): boolean {
  return text.trim().length >= minimumSearchTextLength;
}

export function isSearching(filters: StationSearchFilters): boolean {
  return (
    hasSearchText(filters.text) ||
    filters.connectorTypes.length > 0 ||
    filters.powerSupplies.length > 0 ||
    filters.minPowerKw !== null ||
    filters.nearMe
  );
}

/** The exact filters behind the filter icon, not the text or Near me. */
export function activeFilterCount(filters: StationSearchFilters): number {
  return (
    filters.connectorTypes.length +
    filters.powerSupplies.length +
    (filters.minPowerKw === null ? 0 : 1)
  );
}

export function toSearchRequest(
  filters: StationSearchFilters,
  position: { readonly latitude: number; readonly longitude: number } | null,
): StationSearchRequest {
  return {
    q: hasSearchText(filters.text) ? filters.text.trim() : undefined,
    connectorType: filters.connectorTypes.length
      ? filters.connectorTypes
      : undefined,
    powerSupply: filters.powerSupplies.length
      ? filters.powerSupplies
      : undefined,
    minPowerKw: filters.minPowerKw ?? undefined,
    lat: position?.latitude,
    lon: position?.longitude,
    radiusKm: position ? (filters.radiusKm ?? undefined) : undefined,
  };
}
