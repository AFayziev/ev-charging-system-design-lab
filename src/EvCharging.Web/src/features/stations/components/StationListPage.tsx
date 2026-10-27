import {
  Button,
  Card,
  EmptyState,
  Group,
  SimpleGrid,
  Stack,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconArrowLeft,
  IconArrowRight,
  IconChargingPile,
  IconSearchOff,
} from "@tabler/icons-react";
import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router";
import { PageHeading } from "@/components/PageHeading";
import { useCurrentPosition } from "@/lib/geolocation";
import {
  stationAvailabilityQuery,
  stationListQuery,
  stationSearchQuery,
  type StationAvailability,
  type StationList,
  type StationSearchResult,
} from "../api";
import { isSearchUnavailable } from "../errors";
import {
  activeFilterCount,
  hasSearchText,
  isSearching,
  readSearchFilters,
  toSearchRequest,
  writeSearchFilters,
  type StationSearchFilters,
} from "../searchParams";
import { StationCard } from "./StationCard";
import { StationFilterPanel } from "./StationFilterPanel";
import { StationSearchBar } from "./StationSearchBar";
import { StationSearchCaption } from "./StationSearchCaption";
import { LocationNotice, SearchUnavailableNotice } from "./StationSearchNotice";
import { StationFailure, StationLoading } from "./StationPresentation";

const clearedFilters = {
  connectorTypes: [],
  powerSupplies: [],
  minPowerKw: null,
} satisfies Partial<StationSearchFilters>;

export function StationListPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = readSearchFilters(searchParams);
  const afterId = searchParams.get("after");
  const searching = isSearching(filters);
  const filtersId = useId();
  const [filtersOpened, { toggle: toggleFilters }] = useDisclosure(false);

  // A navigation reaches the rendered URL a moment after it is made, so a change made in between
  // (a chip clicked while the text settles) is built on the latest URL written, not the rendered one.
  const written = useRef<URLSearchParams | null>(null);
  const url = searchParams.toString();
  useEffect(() => {
    written.current = null;
  }, [url]);
  const update = (change: Partial<StationSearchFilters>) => {
    const next = writeSearchFilters({
      ...readSearchFilters(written.current ?? searchParams),
      ...change,
    });
    written.current = next;
    setSearchParams(next, { replace: true });
  };
  // Only a settled text is written to the URL, and only when it is not there already.
  const settleText = (text: string) => {
    if (text.trim() !== readSearchFilters(written.current ?? searchParams).text)
      update({ text });
  };

  const position = useCurrentPosition(filters.nearMe);
  const locating =
    filters.nearMe &&
    (position.status === "idle" || position.status === "locating");
  // Switching Near me off must not search once more with the position it just released.
  const located =
    filters.nearMe && position.status === "granted" ? position : null;

  // The cursor carries its own search, including the position, so it stays out of the URL.
  const searchKey = `${url}|${located ? "located" : ""}`;
  const [paging, setPaging] = useState({
    key: "",
    cursor: null as string | null,
  });
  const cursor = paging.key === searchKey ? paging.cursor : null;

  const searchQuery = useQuery({
    ...stationSearchQuery(
      cursor ? { cursor } : toSearchRequest(filters, located),
    ),
    enabled: searching && !locating,
  });
  const searchDown =
    searchQuery.isError && isSearchUnavailable(searchQuery.error);
  const browseQuery = useQuery({
    ...stationListQuery(afterId),
    enabled: !searching || searchDown,
  });
  const showingSearch = searching && !searchDown;

  // Availability is read for the stations on screen, apart from the search or browse that listed them.
  // It is advisory, so a failed read shows no counts rather than an error.
  const listed = showingSearch
    ? searchQuery.data?.stations
    : browseQuery.data?.stations;
  const availabilityQuery = useQuery(
    stationAvailabilityQuery(listed?.map((station) => station.id) ?? []),
  );

  return (
    <Stack gap="lg">
      <PageHeading title="Find a station" />
      <Stack gap="xs">
        <StationSearchBar
          text={filters.text}
          onTextSettled={settleText}
          nearMe={filters.nearMe}
          onNearMeChange={(nearMe) => update({ nearMe })}
          filterCount={activeFilterCount(filters)}
          filtersOpened={filtersOpened}
          onFiltersToggle={toggleFilters}
          filtersId={filtersId}
        />
        <StationFilterPanel
          id={filtersId}
          opened={filtersOpened}
          filters={filters}
          onChange={update}
          onClear={() => update(clearedFilters)}
        />
      </Stack>
      {filters.nearMe && <LocationNotice status={position.status} />}
      {searchDown && (
        <SearchUnavailableNotice retry={() => void searchQuery.refetch()} />
      )}
      {showingSearch ? (
        <SearchResults
          availability={availabilityQuery.data}
          locating={locating}
          query={searchQuery}
          sort={
            located
              ? "distance"
              : hasSearchText(filters.text)
                ? "relevance"
                : "name"
          }
          radiusKm={located ? filters.radiusKm : null}
          paged={cursor !== null}
          onFirstPage={() => setPaging({ key: "", cursor: null })}
          onNextPage={(next) => setPaging({ key: searchKey, cursor: next })}
          onClear={() => update({ ...clearedFilters, text: "", nearMe: false })}
        />
      ) : (
        <BrowseResults
          availability={availabilityQuery.data}
          query={browseQuery}
          afterId={afterId}
          onPage={(after) =>
            // Search parameters stay, so a fallback page does not forget the search it stands in for.
            setSearchParams((current) => {
              const next = new URLSearchParams(current);
              if (after) next.set("after", after);
              else next.delete("after");
              return next;
            })
          }
        />
      )}
    </Stack>
  );
}

function SearchResults({
  availability,
  locating,
  query,
  sort,
  radiusKm,
  paged,
  onFirstPage,
  onNextPage,
  onClear,
}: {
  availability: ReadonlyMap<string, StationAvailability> | undefined;
  locating: boolean;
  query: UseQueryResult<StationSearchResult>;
  sort: "distance" | "relevance" | "name";
  radiusKm: number | null;
  paged: boolean;
  onFirstPage: () => void;
  onNextPage: (cursor: string) => void;
  onClear: () => void;
}) {
  if (locating || query.isPending) return <StationLoading />;
  if (query.isError)
    return (
      <StationFailure error={query.error} retry={() => void query.refetch()} />
    );
  const { stations, nextCursor, total, totalIsExact } = query.data;
  if (stations.length === 0)
    return (
      <Card withBorder padding="xl">
        <EmptyState icon={<IconSearchOff aria-hidden />}>
          <EmptyState.Title order={2}>No stations match</EmptyState.Title>
          <EmptyState.Description>
            Try a different search, or remove a filter.
          </EmptyState.Description>
          <Button variant="light" mih={44} onClick={onClear}>
            Clear search
          </Button>
        </EmptyState>
      </Card>
    );
  return (
    <Stack
      gap="lg"
      aria-busy={query.isPlaceholderData}
      opacity={query.isPlaceholderData ? 0.6 : 1}
    >
      <StationSearchCaption
        total={total}
        totalIsExact={totalIsExact}
        radiusKm={radiusKm}
        sort={sort}
      />
      <StationGrid>
        {stations.map((station) => (
          <StationCard
            key={station.id}
            station={station}
            availability={availability?.get(station.id)}
          />
        ))}
      </StationGrid>
      <Pager
        previous={paged ? { label: "First page", onClick: onFirstPage } : null}
        next={
          nextCursor === null
            ? null
            : { label: "Next stations", onClick: () => onNextPage(nextCursor) }
        }
      />
    </Stack>
  );
}

function BrowseResults({
  availability,
  query,
  afterId,
  onPage,
}: {
  availability: ReadonlyMap<string, StationAvailability> | undefined;
  query: UseQueryResult<StationList>;
  afterId: string | null;
  onPage: (afterId: string | null) => void;
}) {
  if (query.isPending) return <StationLoading />;
  if (query.isError)
    return (
      <StationFailure error={query.error} retry={() => void query.refetch()} />
    );
  const { stations, nextAfterId } = query.data;
  return (
    <>
      {stations.length === 0 ? (
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
        <StationGrid>
          {stations.map((station) => (
            <StationCard
              key={station.id}
              station={station}
              availability={availability?.get(station.id)}
            />
          ))}
        </StationGrid>
      )}
      <Pager
        previous={
          afterId === null
            ? null
            : { label: "First page", onClick: () => onPage(null) }
        }
        next={
          nextAfterId === null
            ? null
            : {
                label: "Next stations",
                onClick: () => onPage(nextAfterId),
              }
        }
      />
    </>
  );
}

function StationGrid({ children }: { children: ReactNode }) {
  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
      {children}
    </SimpleGrid>
  );
}

function Pager({
  previous,
  next,
}: {
  previous: { label: string; onClick: () => void } | null;
  next: { label: string; onClick: () => void } | null;
}) {
  return (
    <Group justify="space-between">
      {previous && (
        <Button
          variant="subtle"
          onClick={previous.onClick}
          mih={44}
          leftSection={<IconArrowLeft size={18} aria-hidden="true" />}
        >
          {previous.label}
        </Button>
      )}
      {next && (
        <Button
          variant="light"
          ml="auto"
          onClick={next.onClick}
          mih={44}
          rightSection={<IconArrowRight size={18} aria-hidden="true" />}
        >
          {next.label}
        </Button>
      )}
    </Group>
  );
}
