import {
  Alert,
  Button,
  EmptyState,
  Group,
  Loader,
  Paper,
  Stack,
  Title,
} from "@mantine/core";
import { IconArrowRight, IconCalendar } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  createPath,
  Link,
  useLocation,
  useNavigate,
  useSearchParams,
} from "react-router";
import { PageHeading } from "@/components/PageHeading";
import { reservationsQuery } from "../api";
import type { ReservationNavigationState } from "../navigation";
import { ReservationFailure } from "./ReservationFailure";
import { ReservationCard } from "./ReservationCard";

export function ReservationsPage() {
  const [search, setSearch] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [createdReservation] = useState(
    () =>
      (location.state as ReservationNavigationState | null)?.createdReservation,
  );
  useEffect(() => {
    if (
      (location.state as ReservationNavigationState | null)?.createdReservation
    )
      void navigate(createPath(location), { replace: true, state: null });
  }, [location, navigate]);
  const query = useQuery(reservationsQuery(search.get("afterId")));
  const nextAfterId = query.data?.nextAfterId;
  const reservations = query.data?.reservations ?? [];
  const visibleReservations =
    createdReservation && !search.has("afterId")
      ? [
          reservations.find(
            (reservation) => reservation.id === createdReservation.id,
          ) ?? createdReservation,
          ...reservations.filter(
            (reservation) => reservation.id !== createdReservation.id,
          ),
        ]
      : reservations;
  const active = visibleReservations.filter(
    (reservation) => reservation.state === "active",
  );
  const past = visibleReservations.filter(
    (reservation) => reservation.state !== "active",
  );

  return (
    <Stack gap="lg">
      <PageHeading
        title="My reservations"
        description="View your charging reservations and booked time slots."
      />
      {createdReservation && (
        <Alert color="teal" role="status">
          Reservation created.
        </Alert>
      )}
      {query.isPending && <Loader aria-label="Loading reservations" />}
      {query.isError && (
        <ReservationFailure
          error={query.error}
          retry={() => void query.refetch()}
        />
      )}
      {(query.isSuccess || createdReservation) && (
        <>
          {visibleReservations.length === 0 && (
            <Paper withBorder p="xl" radius="sm">
              <EmptyState>
                <EmptyState.Indicator>
                  <IconCalendar size={32} aria-hidden />
                </EmptyState.Indicator>
                <EmptyState.Title order={2}>
                  No reservations yet
                </EmptyState.Title>
                <EmptyState.Description>
                  Choose a station and book a connector for your next charge.
                </EmptyState.Description>
                <EmptyState.Actions>
                  <Button
                    component={Link}
                    to="/stations"
                    rightSection={<IconArrowRight size={16} aria-hidden />}
                  >
                    Browse stations
                  </Button>
                </EmptyState.Actions>
              </EmptyState>
            </Paper>
          )}
          {active.length > 0 && (
            <Stack
              component="section"
              aria-labelledby="active-reservations"
              gap="sm"
            >
              <Title id="active-reservations" order={2} size="h4">
                Active reservations
              </Title>
              {active.map((reservation) => (
                <ReservationCard
                  key={reservation.id}
                  reservation={reservation}
                  highlighted={reservation.id === createdReservation?.id}
                />
              ))}
            </Stack>
          )}
          {past.length > 0 && (
            <Stack
              component="section"
              aria-labelledby="past-reservations"
              gap="sm"
            >
              <Title id="past-reservations" order={2} size="h4">
                Past reservations
              </Title>
              {past.map((reservation) => (
                <ReservationCard
                  key={reservation.id}
                  reservation={reservation}
                  highlighted={reservation.id === createdReservation?.id}
                />
              ))}
            </Stack>
          )}
          {(nextAfterId || search.has("afterId")) && (
            <Group justify="flex-end">
              {search.has("afterId") && (
                <Button variant="subtle" onClick={() => setSearch({})}>
                  First page
                </Button>
              )}
              {nextAfterId && (
                <Button
                  variant="light"
                  onClick={() => setSearch({ afterId: nextAfterId })}
                >
                  More reservations
                </Button>
              )}
            </Group>
          )}
        </>
      )}
    </Stack>
  );
}
