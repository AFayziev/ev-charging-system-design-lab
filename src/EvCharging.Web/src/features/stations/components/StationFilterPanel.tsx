import {
  Button,
  Chip,
  Collapse,
  Group,
  Select,
  Stack,
  Text,
} from "@mantine/core";
import { IconX } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { stationSearchOptionsQuery } from "../api";
import { isSearchUnavailable } from "../errors";
import { radiusOptions, type StationSearchFilters } from "../searchParams";
import { connectorTypeLabel, StationFailure } from "./StationPresentation";
import { RetryNotice } from "./StationSearchNotice";

const anyValue = "";
const anyRadius = "any";

/** The exact filters behind the filter icon; the choices are whatever the search index holds. */
export function StationFilterPanel({
  id,
  opened,
  filters,
  onChange,
  onClear,
}: {
  id: string;
  opened: boolean;
  filters: StationSearchFilters;
  onChange: (change: Partial<StationSearchFilters>) => void;
  onClear: () => void;
}) {
  const options = useQuery(stationSearchOptionsQuery(opened));
  return (
    <Collapse expanded={opened} id={id}>
      <Stack gap="md" pb="xs">
        {options.isPending && opened && (
          <Text role="status" size="sm">
            Loading filters…
          </Text>
        )}
        {options.isError &&
          (isSearchUnavailable(options.error) ? (
            <RetryNotice
              title="Filters unavailable"
              message="Search filters can't be loaded right now."
              retryLabel="Retry filters"
              retry={() => void options.refetch()}
            />
          ) : (
            <StationFailure
              error={options.error}
              retry={() => void options.refetch()}
            />
          ))}
        {options.isSuccess && (
          <>
            <ChipFilter
              label="Connector"
              values={withSelected(
                options.data.connectorTypes,
                filters.connectorTypes,
              )}
              selected={filters.connectorTypes}
              format={connectorTypeLabel}
              onChange={(connectorTypes) => onChange({ connectorTypes })}
            />
            <ChipFilter
              label="Supply"
              values={withSelected(
                options.data.powerSupplies,
                filters.powerSupplies,
              )}
              selected={filters.powerSupplies}
              onChange={(powerSupplies) => onChange({ powerSupplies })}
            />
            <Group align="flex-end">
              <Select
                label="Minimum power"
                allowDeselect={false}
                data={[
                  { value: anyValue, label: "Any" },
                  ...withSelected(
                    options.data.ratedPowersKw,
                    filters.minPowerKw === null ? [] : [filters.minPowerKw],
                  ).map((power) => ({
                    value: String(power),
                    label: `${power} kW or more`,
                  })),
                ]}
                value={
                  filters.minPowerKw === null
                    ? anyValue
                    : String(filters.minPowerKw)
                }
                onChange={(value) =>
                  onChange({ minPowerKw: value ? Number(value) : null })
                }
              />
              {filters.nearMe && (
                <Select
                  label="Within"
                  allowDeselect={false}
                  data={[
                    ...radiusOptions.map((radius) => ({
                      value: String(radius),
                      label: `${radius} km`,
                    })),
                    { value: anyRadius, label: "Any distance" },
                  ]}
                  value={
                    filters.radiusKm === null
                      ? anyRadius
                      : String(filters.radiusKm)
                  }
                  onChange={(value) =>
                    onChange({
                      radiusKm: value === anyRadius ? null : Number(value),
                    })
                  }
                />
              )}
            </Group>
          </>
        )}
        <Button
          variant="subtle"
          color="gray"
          w="fit-content"
          mih={44}
          leftSection={<IconX size={18} aria-hidden="true" />}
          onClick={onClear}
        >
          Clear filters
        </Button>
      </Stack>
    </Collapse>
  );
}

function ChipFilter({
  label,
  values,
  selected,
  format = (value) => value,
  onChange,
}: {
  label: string;
  values: readonly string[];
  selected: readonly string[];
  format?: (value: string) => string;
  onChange: (selected: string[]) => void;
}) {
  if (values.length === 0) return null;
  return (
    <Stack gap={6}>
      <Text size="sm" fw={500} id={`${label}-filter`}>
        {label}
      </Text>
      <Chip.Group multiple value={[...selected]} onChange={onChange}>
        <Group gap="xs" role="group" aria-labelledby={`${label}-filter`}>
          {values.map((value) => (
            <Chip key={value} value={value} variant="outline">
              {format(value)}
            </Chip>
          ))}
        </Group>
      </Chip.Group>
    </Stack>
  );
}

/** A value from the URL that the index no longer offers stays visible, so it can be removed. */
function withSelected<T extends string | number>(
  offered: readonly T[],
  selected: readonly T[],
): T[] {
  return [...new Set([...offered, ...selected])];
}
