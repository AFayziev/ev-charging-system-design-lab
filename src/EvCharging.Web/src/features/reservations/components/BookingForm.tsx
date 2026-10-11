import {
  Alert,
  Button,
  Card,
  Group,
  Select,
  Skeleton,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { DateTimePicker, getTimeRange } from "@mantine/dates";
import { useMediaQuery } from "@mantine/hooks";
import dayjs from "dayjs";
import { IconCalendar, IconInfoCircle } from "@tabler/icons-react";
import { useEffect, useState, type ReactNode } from "react";
import { isNotEmpty, useForm } from "@mantine/form";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { sessionQuery } from "@/features/auth/api";
import { PlatformRole } from "@/features/auth/PlatformRole";
import type { StationDetail } from "@/features/stations/api";
import { connectorTypeLabel } from "@/features/stations/components/StationPresentation";
import { getFormValidationErrors, getHttpFailure } from "@/lib/http-errors";
import {
  bookingPolicyQuery,
  createReservation,
  reservationKeys,
  type Booking,
  type BookingPolicy,
} from "../api";
import type { ReservationNavigationState } from "../navigation";
import { ReservationFailure } from "./ReservationFailure";
import { formatTime, ReservationTimes } from "./ReservationTimes";
import classes from "./Booking.module.css";

interface BookingFormProps {
  station: StationDetail;
  connectorId: string;
  onConnectorChange?: (connectorId: string) => void;
}

export function BookingForm(props: BookingFormProps) {
  const session = useQuery(sessionQuery);
  const policy = useQuery({
    ...bookingPolicyQuery,
    enabled:
      session.isSuccess &&
      session.data.user?.roles.includes(PlatformRole.Driver) === true,
  });
  const policyError = policy.isError ? getHttpFailure(policy.error) : null;
  const policyFailure = policyError && (
    <Alert color="red" title={policyError.title} role="alert">
      <Stack gap="xs">
        <Text size="sm">{policyError.message}</Text>
        <Button
          variant="white"
          onClick={() => void policy.refetch()}
          loading={policy.isFetching}
        >
          Try again
        </Button>
      </Stack>
    </Alert>
  );
  if (!policy.data) {
    return (
      <Card withBorder padding="lg" className={classes.bookingCard}>
        <Stack gap="md">
          <Title order={2} size="h4">
            Book a connector
          </Title>
          {policyFailure || (
            <Stack gap="md" role="status" aria-label="Loading booking limits">
              <Skeleton height={36} />
              <Skeleton height={72} />
              <Skeleton height={36} />
            </Stack>
          )}
        </Stack>
      </Card>
    );
  }
  return (
    <ConfiguredBookingForm
      {...props}
      policy={policy.data}
      policyFailure={policyFailure}
    />
  );
}

function ConfiguredBookingForm({
  station,
  connectorId,
  onConnectorChange,
  policy,
  policyFailure,
}: BookingFormProps & {
  policy: BookingPolicy;
  policyFailure: ReactNode;
}) {
  const session = useQuery(sessionQuery);
  const client = useQueryClient();
  const navigate = useNavigate();
  const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const compact = useMediaQuery("(max-width: 35.99em)");
  const [now, setNow] = useState(Date.now);
  const connectors = station.chargers.flatMap((charger) =>
    charger.evses.flatMap((evse) =>
      evse.connectors.map((connector) => {
        const chargerLabel =
          station.chargers.length > 1 ? `${charger.name} · ` : "";
        const equipmentLabel = `${chargerLabel}EVSE ${evse.number} · Connector ${connector.number}`;
        const typeLabel = connectorTypeLabel(connector.connectorType);
        return {
          value: connector.id,
          label: `${equipmentLabel} (${typeLabel}, ${connector.ratedPowerKW} kW)`,
        };
      }),
    ),
  );
  const form = useForm({
    mode: "uncontrolled",
    initialValues: {
      connectorId,
      startsAt: null as string | null,
      endsAt: null as string | null,
    },
    onValuesChange: (values, previous) => {
      if (
        values.connectorId !== previous.connectorId &&
        values.connectorId !== connectorId
      )
        onConnectorChange?.(values.connectorId);
    },
    validate: {
      connectorId: isNotEmpty("Choose a connector."),
      startsAt: (value) =>
        dayjs(value).isValid() ? null : "Choose a start time.",
      endsAt: (value) =>
        dayjs(value).isValid() ? null : "Choose an end time.",
    },
  });
  const { getValues, setFieldValue } = form;
  useEffect(() => {
    if (getValues().connectorId !== connectorId)
      setFieldValue("connectorId", connectorId);
  }, [connectorId, getValues, setFieldValue]);
  const start = form.useWatchValue("startsAt");
  const end = form.useWatchValue("endsAt");
  const earliestStart = dayjs(now).startOf("minute").add(1, "minute");
  const afterStart = dayjs(start).add(1, "minute");
  const earliestEnd = afterStart.isAfter(earliestStart)
    ? afterStart
    : earliestStart;
  const latestStart = dayjs(now).add(
    policy.maximumAdvanceBookingHorizonSeconds,
    "second",
  );
  const latestEnd = dayjs(start).isValid()
    ? dayjs(start).add(policy.maximumBookingDurationSeconds, "second")
    : latestStart.add(policy.maximumBookingDurationSeconds, "second");
  const startMinimum = dayjs(start ?? earliestStart).isSame(
    earliestStart,
    "day",
  )
    ? earliestStart.format("HH:mm")
    : undefined;
  const endMinimum = dayjs(end ?? earliestEnd).isSame(earliestEnd, "day")
    ? earliestEnd.format("HH:mm")
    : undefined;
  const startMaximum = dayjs(start ?? earliestStart).isSame(latestStart, "day")
    ? latestStart.format("HH:mm")
    : undefined;
  const endMaximum = dayjs(end ?? earliestEnd).isSame(latestEnd, "day")
    ? latestEnd.format("HH:mm")
    : undefined;
  const initializePicker = (field: "startsAt" | "endsAt") => {
    const current = Date.now();
    setNow(current);
    const values = form.getValues();
    if (values[field]) return;

    const minimum = dayjs(current).startOf("minute").add(1, "minute");
    const afterStart = dayjs(values.startsAt).add(1, "minute");
    const initial =
      field === "endsAt" && afterStart.isAfter(minimum) ? afterStart : minimum;
    form.setFieldValue(field, initial.format("YYYY-MM-DD HH:mm:ss"));
  };
  const preview = dayjs(start).isValid() && dayjs(end).isValid();
  const mutation = useMutation({
    mutationFn: (booking: Booking) =>
      createReservation(booking, session.data?.antiforgeryToken ?? ""),
    retry: false,
    onSuccess: (saved) => {
      void client.invalidateQueries({ queryKey: reservationKeys.lists });
      void navigate("/reservations", {
        state: {
          createdReservation: saved,
        } satisfies ReservationNavigationState,
      });
    },
    onError: (error) => {
      void client.invalidateQueries({
        queryKey: reservationKeys.bookingPolicy,
      });
      const errors = getFormValidationErrors(error);
      if (errors) form.setErrors(errors);
    },
  });
  return (
    <Card withBorder padding="lg" className={classes.bookingCard}>
      <form
        noValidate
        onSubmit={form.onSubmit(
          (values) => {
            if (
              !mutation.isPending &&
              !policyFailure &&
              session.data?.antiforgeryToken
            )
              mutation.mutate({
                connectorId: values.connectorId,
                startsAt: dayjs(values.startsAt).toISOString(),
                endsAt: dayjs(values.endsAt).toISOString(),
              });
          },
          (errors) => {
            const first = Object.keys(errors)[0];
            if (first) form.getInputNode(first)?.focus();
          },
        )}
      >
        <Stack gap="md">
          <Title order={2} size="h4">
            Book a connector
          </Title>
          {policyFailure}
          <Select
            label="Connector"
            data={connectors}
            key={form.key("connectorId")}
            {...form.getInputProps("connectorId")}
            disabled={mutation.isPending}
            withAsterisk
            aria-required="true"
          />
          <Stack gap="xs">
            <Text size="sm" fw={500}>
              Time slot
            </Text>
            <DateTimePicker
              label="Start time"
              dropdownType={compact ? "modal" : "popover"}
              modalProps={{ title: "Choose start date and time" }}
              placeholder={form.errors.startsAt ? undefined : "Date and time"}
              minDate={earliestStart.format("YYYY-MM-DD HH:mm:ss")}
              maxDate={latestStart.format("YYYY-MM-DD HH:mm:ss")}
              onClick={() => initializePicker("startsAt")}
              valueFormat="D MMM YYYY, HH:mm"
              nextLabel="Next"
              previousLabel="Previous"
              timePickerProps={{
                withDropdown: true,
                presets: getTimeSuggestions(startMinimum, startMaximum),
                closeDropdownOnPresetSelect: true,
                popoverProps: { withinPortal: false },
                hoursInputLabel: "Start hours",
                minutesInputLabel: "Start minutes",
                min: startMinimum,
                max: startMaximum,
              }}
              submitButtonProps={{ "aria-label": "Confirm start time" }}
              leftSection={<IconCalendar size={17} aria-hidden="true" />}
              {...form.getInputProps("startsAt")}
              defaultValue={undefined}
              value={start}
              disabled={mutation.isPending}
              withAsterisk
              aria-required="true"
            />
            <DateTimePicker
              label="End time"
              dropdownType={compact ? "modal" : "popover"}
              modalProps={{ title: "Choose end date and time" }}
              placeholder={form.errors.endsAt ? undefined : "Date and time"}
              minDate={earliestEnd.format("YYYY-MM-DD HH:mm:ss")}
              maxDate={latestEnd.format("YYYY-MM-DD HH:mm:ss")}
              onClick={() => initializePicker("endsAt")}
              valueFormat="D MMM YYYY, HH:mm"
              nextLabel="Next"
              previousLabel="Previous"
              timePickerProps={{
                withDropdown: true,
                presets: getTimeSuggestions(endMinimum, endMaximum),
                closeDropdownOnPresetSelect: true,
                popoverProps: { withinPortal: false },
                hoursInputLabel: "End hours",
                minutesInputLabel: "End minutes",
                min: endMinimum,
                max: endMaximum,
              }}
              submitButtonProps={{ "aria-label": "Confirm end time" }}
              leftSection={<IconCalendar size={17} aria-hidden="true" />}
              {...form.getInputProps("endsAt")}
              defaultValue={undefined}
              value={end}
              disabled={mutation.isPending}
              withAsterisk
              aria-required="true"
            />
          </Stack>
          <Text size="xs" c="dimmed">
            Up to {policy.maximumOutstandingReservationsPerDriver} outstanding{" "}
            {policy.maximumOutstandingReservationsPerDriver === 1
              ? "reservation"
              : "reservations"}
            . Slots up to {formatDuration(policy.maximumBookingDurationSeconds)}
            ; book up to{" "}
            {formatDuration(policy.maximumAdvanceBookingHorizonSeconds)} ahead.
          </Text>
          <Text size="xs" c="dimmed">
            Station time · {station.timeZoneId}
          </Text>
          {browserZone !== station.timeZoneId && (
            <Text size="xs" c="dimmed">
              Enter times in {browserZone}, your browser time zone.
            </Text>
          )}
          {preview && (
            <details className={classes.timePreview}>
              <summary>Review resolved times</summary>
              <Stack gap="xs" mt="sm" aria-live="polite">
                <Text size="sm">
                  Resolved browser time:{" "}
                  {formatTime(dayjs(start).toISOString(), browserZone)} →{" "}
                  {formatTime(dayjs(end).toISOString(), browserZone)}
                </Text>
                <ReservationTimes
                  startsAt={dayjs(start).toISOString()}
                  endsAt={dayjs(end).toISOString()}
                  timeZoneId={station.timeZoneId}
                />
              </Stack>
            </details>
          )}
          {mutation.isError && (
            <ReservationFailure error={mutation.error} booking />
          )}
          <Button
            type="submit"
            loading={mutation.isPending}
            disabled={!session.data?.antiforgeryToken || !!policyFailure}
            fullWidth
          >
            Reserve connector
          </Button>
          <Group gap="xs" align="flex-start" wrap="nowrap">
            <IconInfoCircle
              size={17}
              className={classes.hintIcon}
              aria-hidden="true"
            />
            <Text size="xs" c="dimmed">
              A reservation holds your time slot. Equipment availability can
              change before you arrive.
            </Text>
          </Group>
        </Stack>
      </form>
    </Card>
  );
}

function getTimeSuggestions(minimum?: string, maximum?: string) {
  const suggestions = getTimeRange({
    startTime: "00:00:00",
    endTime: "23:45:00",
    interval: "00:15:00",
  });
  const earliest = minimum ? `${minimum}:00` : "00:00:00";
  const latest = maximum ? `${maximum}:00` : "23:59:00";
  const available = suggestions.filter(
    (time) => time >= earliest && time <= latest,
  );
  return earliest <= latest && available[0] !== earliest
    ? [earliest, ...available]
    : available;
}

function formatDuration(seconds: number) {
  let unit = "second";
  let divisor = 1;
  if (seconds >= 86400) {
    unit = "day";
    divisor = 86400;
  } else if (seconds >= 3600) {
    unit = "hour";
    divisor = 3600;
  } else if (seconds >= 60) {
    unit = "minute";
    divisor = 60;
  }
  return new Intl.NumberFormat(undefined, {
    style: "unit",
    unit,
    unitDisplay: "long",
    maximumFractionDigits: 2,
  }).format(seconds / divisor);
}
