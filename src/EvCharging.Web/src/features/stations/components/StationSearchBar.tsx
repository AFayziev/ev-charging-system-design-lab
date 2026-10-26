import { useDebouncedValue } from "@mantine/hooks";
import { useEffect, useState } from "react";
import {
  ActionIcon,
  CloseButton,
  Group,
  Indicator,
  Stack,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import {
  IconAdjustmentsHorizontal,
  IconCurrentLocation,
  IconSearch,
} from "@tabler/icons-react";
import {
  hasSearchText,
  maximumSearchTextLength,
  minimumSearchTextLength,
} from "../searchParams";

/**
 * The minimal search row: text, Near me, and the filter toggle with its active-filter count.
 * The typed text is the bar's own until it settles; the page keeps only the settled text in the URL.
 */
export function StationSearchBar({
  text,
  onTextSettled,
  nearMe,
  onNearMeChange,
  filterCount,
  filtersOpened,
  onFiltersToggle,
  filtersId,
}: {
  /** The text the URL holds. */
  text: string;
  onTextSettled: (text: string) => void;
  nearMe: boolean;
  onNearMeChange: (nearMe: boolean) => void;
  filterCount: number;
  filtersOpened: boolean;
  onFiltersToggle: () => void;
  filtersId: string;
}) {
  const [draft, setDraft] = useState(text);
  const [settled] = useDebouncedValue(draft, 300);
  useEffect(() => {
    onTextSettled(settled);
    // Only a newly settled text is reported.
  }, [settled]);
  // A URL that changed from outside (Back, a followed link) replaces the text being shown; a text
  // the driver is typing, or one just written, does not.
  useEffect(() => {
    if (text !== draft.trim() && text !== settled.trim()) setDraft(text);
    // Only a change of the URL's text is of interest.
  }, [text]);
  const tooShort = draft.trim() !== "" && !hasSearchText(draft);
  return (
    <Stack gap={6}>
      <Group wrap="nowrap" align="flex-start" gap="xs">
        <TextInput
          flex={1}
          type="search"
          size="md"
          aria-label="Search stations"
          placeholder="Station, address, connector, or provider"
          maxLength={maximumSearchTextLength}
          leftSection={<IconSearch size={18} aria-hidden="true" />}
          rightSection={
            draft && (
              <CloseButton
                aria-label="Clear text"
                onClick={() => {
                  setDraft("");
                  onTextSettled("");
                }}
              />
            )
          }
          value={draft}
          onChange={(event) => setDraft(event.currentTarget.value)}
        />
        <Tooltip label="Near me">
          <ActionIcon
            size="input-md"
            variant={nearMe ? "outline" : "default"}
            color="teal"
            aria-label="Near me"
            aria-pressed={nearMe}
            onClick={() => onNearMeChange(!nearMe)}
          >
            <IconCurrentLocation size={20} aria-hidden="true" />
          </ActionIcon>
        </Tooltip>
        <Indicator
          label={filterCount}
          disabled={filterCount === 0}
          size={18}
          offset={4}
          color="teal"
        >
          <Tooltip label="Filters">
            <ActionIcon
              size="input-md"
              variant={filtersOpened ? "outline" : "default"}
              color="teal"
              aria-label={
                filterCount > 0 ? `Filters, ${filterCount} active` : "Filters"
              }
              aria-expanded={filtersOpened}
              aria-controls={filtersId}
              onClick={onFiltersToggle}
            >
              <IconAdjustmentsHorizontal size={20} aria-hidden="true" />
            </ActionIcon>
          </Tooltip>
        </Indicator>
      </Group>
      {tooShort && (
        <Text size="xs" c="dimmed" role="status">
          Type at least {minimumSearchTextLength} characters to search.
        </Text>
      )}
    </Stack>
  );
}
