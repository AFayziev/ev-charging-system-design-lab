import { Anchor, Badge, Paper, Stack, Text } from "@mantine/core";
import { IconCalendar, IconPlug } from "@tabler/icons-react";
import { Link } from "react-router";
import { StationPhoto } from "@/features/stations/components/StationPhoto";
import { capabilityLabel } from "@/features/stations/components/StationPresentation";
import { hasCurrentCharging, type Reservation } from "../api";
import {
  formatReservationSlot,
  getBrowserTimeZone,
  formatStartDeadline,
} from "../time-formatting";
import { ReservationStatus } from "./ReservationStatus";
import classes from "./Reservations.module.css";
import { ChargingStartControls } from "./ChargingStartControls";
import { getStartPresentation } from "../start-presentation";

export function ReservationCard({
  reservation,
  highlighted = false,
  onCancel,
  statusUnavailable = false,
  onRefresh,
}: {
  reservation: Reservation;
  highlighted?: boolean;
  onCancel?: (reservation: Reservation) => void;
  statusUnavailable?: boolean;
  onRefresh: () => Promise<unknown>;
}) {
  const active =
    reservation.state === "active" || hasCurrentCharging(reservation);
  const startPresentation = getStartPresentation(
    reservation.startOperation,
    reservation.state === "active",
    reservation.arrivalDeadline,
  );
  const timeRange = formatReservationSlot(
    reservation.startsAt,
    reservation.endsAt,
  );

  return (
    <Paper
      component="article"
      aria-label={`${reservation.stationName} reservation`}
      withBorder
      radius="sm"
      className={classes.card}
      data-active={active}
      data-highlighted={highlighted}
    >
      <StationPhoto className={classes.photo} />
      <Stack gap={4} className={classes.station}>
        <Anchor
          component={Link}
          to={`/stations/${reservation.stationId}`}
          className={classes.stationName}
          underline="never"
        >
          {reservation.stationName}
        </Anchor>
        <Text size="xs" c="dimmed">
          {reservation.stationAddress}
        </Text>
      </Stack>
      <div className={classes.equipment}>
        <IconPlug size={24} aria-hidden />
        <Stack gap={3}>
          <Text size="xs" c="dimmed">
            {reservation.chargerName}
          </Text>
          <Text size="sm" fw={600}>
            Connector {reservation.connectorNumber}
          </Text>
          <Text size="sm" c="dimmed">
            {capabilityLabel(reservation)}
          </Text>
        </Stack>
      </div>
      <Stack gap={8} className={classes.slot}>
        <div className={classes.time}>
          <IconCalendar size={18} aria-hidden />
          <Text size="sm">{timeRange}</Text>
        </div>
        <Text size="xs" c="dimmed">
          Your time · {getBrowserTimeZone()}
        </Text>
        {startPresentation?.badge ? (
          <Badge
            color={startPresentation.color}
            variant="light"
            styles={{ label: { textTransform: "none" } }}
          >
            {startPresentation.badge}
          </Badge>
        ) : (
          <ReservationStatus reservation={reservation} />
        )}
        {reservation.state === "active" && (
          <Text size="xs" c="dimmed">
            Start by {formatStartDeadline(reservation.arrivalDeadline)}
          </Text>
        )}
      </Stack>
      {(active || reservation.startOperation) && (
        <ChargingStartControls
          reservation={reservation}
          statusUnavailable={statusUnavailable}
          onRefresh={onRefresh}
          onCancel={onCancel}
        />
      )}
    </Paper>
  );
}
