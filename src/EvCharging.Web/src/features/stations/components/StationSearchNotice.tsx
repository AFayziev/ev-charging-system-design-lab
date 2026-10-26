import { Button, Group, Text } from "@mantine/core";
import { StatusAlert } from "@/components/StatusAlert";
import type { CurrentPosition } from "@/lib/geolocation";

/** Why results are not ordered by distance, when the driver asked for Near me. */
export function LocationNotice({
  status,
}: {
  status: CurrentPosition["status"];
}) {
  switch (status) {
    case "denied":
      return (
        <StatusAlert tone="warning" title="Location is blocked">
          Results are sorted by name. Allow location access in your browser to
          sort by distance.
        </StatusAlert>
      );
    case "unavailable":
      return (
        <StatusAlert tone="warning" title="Location unavailable">
          Your location couldn't be determined, so results are sorted by name.
        </StatusAlert>
      );
    case "unsupported":
      return (
        <StatusAlert tone="warning" title="Location not supported">
          This browser can't share your location, so results are sorted by name.
        </StatusAlert>
      );
    default:
      return null;
  }
}

/** A warning about something unavailable, with the way to try it again. */
export function RetryNotice({
  title,
  message,
  retryLabel,
  retry,
}: {
  title: string;
  message: string;
  retryLabel: string;
  retry: () => void;
}) {
  return (
    <StatusAlert tone="warning" title={title}>
      <Group justify="space-between">
        <Text size="sm">{message}</Text>
        <Button variant="light" color="orange" mih={44} onClick={retry}>
          {retryLabel}
        </Button>
      </Group>
    </StatusAlert>
  );
}

/** Search is down; the catalog below is the unfiltered list. */
export function SearchUnavailableNotice({ retry }: { retry: () => void }) {
  return (
    <RetryNotice
      title="Search unavailable"
      message="Showing all stations. Your search and filters are not applied."
      retryLabel="Retry search"
      retry={retry}
    />
  );
}
