import { Anchor, Button, Paper, Stack, Text } from "@mantine/core";
import { IconCalendar, IconPlug } from "@tabler/icons-react";
import { Link } from "react-router";
import { StationPhoto } from "@/features/stations/components/StationPhoto";
import { capabilityLabel } from "@/features/stations/components/StationPresentation";
import type { Reservation } from "../api";
import { formatReservationSlot, getBrowserTimeZone } from "../time-formatting";
import { ReservationStatus } from "./ReservationStatus";
import classes from "./Reservations.module.css";

export function ReservationCard({
  reservation,
  highlighted = false,
  onCancel,
}: {
  reservation: Reservation;
  highlighted?: boolean;
  onCancel?: (reservation: Reservation) => void;
}) {
  const active = reservation.state === "active";
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
        <ReservationStatus reservation={reservation} />
      </Stack>
      {active && onCancel && (
        <div className={classes.actions}>
          <Button fullWidth disabled>
            Start charging
          </Button>
          <Button
            variant="outline"
            color="red"
            fullWidth
            onClick={() => onCancel(reservation)}
          >
            Cancel reservation
          </Button>
        </div>
      )}
    </Paper>
  );
}
