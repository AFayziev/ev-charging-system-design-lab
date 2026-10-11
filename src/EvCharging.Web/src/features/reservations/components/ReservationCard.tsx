import { Anchor, Paper, Stack, Text } from "@mantine/core";
import { IconCalendar, IconPlug } from "@tabler/icons-react";
import { Link } from "react-router";
import { StationPhoto } from "@/features/stations/components/StationPhoto";
import { capabilityLabel } from "@/features/stations/components/StationPresentation";
import type { Reservation } from "../api";
import { ReservationStatus } from "./ReservationStatus";
import { formatReservationSlot } from "./ReservationTimes";
import classes from "./Reservations.module.css";

export function ReservationCard({
  reservation,
  highlighted = false,
}: {
  reservation: Reservation;
  highlighted?: boolean;
}) {
  const active = reservation.state === "active";
  const timeRange = formatReservationSlot(
    reservation.startsAt,
    reservation.endsAt,
    reservation.timeZoneId,
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
            EVSE {reservation.evseNumber} · Connector{" "}
            {reservation.connectorNumber}
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
          Station time · {reservation.timeZoneId}
        </Text>
        <ReservationStatus state={reservation.state} />
      </Stack>
    </Paper>
  );
}
