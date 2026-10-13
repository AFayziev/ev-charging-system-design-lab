import { queryOptions, replaceEqualDeep } from "@tanstack/react-query";
import { api } from "@/lib/api";

export interface Reservation {
  readonly id: string;
  readonly stationId: string;
  readonly stationName: string;
  readonly stationAddress: string;
  readonly timeZoneId: string;
  readonly connectorId: string;
  readonly chargerName: string;
  readonly connectorNumber: number;
  readonly connectorType: string;
  readonly powerSupply: string;
  readonly ratedPowerKW: number;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly state: "active" | "consumed" | "cancelled" | "expired";
  readonly createdAt: string;
  readonly cancelledAt: string | null;
  readonly expiredAt: string | null;
}

export interface ReservationList {
  readonly reservations: readonly Reservation[];
  readonly nextAfterId: string | null;
}

export interface Booking {
  readonly connectorId: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface BookingPolicy {
  readonly maximumOutstandingReservationsPerDriver: number;
  readonly maximumBookingDurationSeconds: number;
  readonly maximumAdvanceBookingHorizonSeconds: number;
}

export const reservationKeys = {
  bookingPolicy: ["reservations", "booking-policy"] as const,
  lists: ["reservations", "list"] as const,
  list: (afterId: string | null) => ["reservations", "list", afterId] as const,
};

export async function getBookingPolicy(
  signal?: AbortSignal,
): Promise<BookingPolicy> {
  const response = await api.get<BookingPolicy>(
    "/api/reservations/booking-policy",
    { signal },
  );
  return response.data;
}

export const bookingPolicyQuery = queryOptions({
  queryKey: reservationKeys.bookingPolicy,
  queryFn: ({ signal }) => getBookingPolicy(signal),
  retry: false,
});

export async function createReservation(
  booking: Booking,
  token: string,
  signal?: AbortSignal,
): Promise<Reservation> {
  const response = await api.post<Reservation>("/api/reservations", booking, {
    headers: { "X-CSRF-TOKEN": token },
    signal,
  });
  return response.data;
}

export async function getReservations(
  afterId: string | null,
  signal?: AbortSignal,
): Promise<ReservationList> {
  const response = await api.get<ReservationList>("/api/reservations", {
    params: { afterId },
    signal,
  });
  return response.data;
}

export async function cancelReservation(
  id: string,
  token: string,
  signal?: AbortSignal,
): Promise<Reservation> {
  const response = await api.post<Reservation>(
    `/api/reservations/${id}/cancel`,
    undefined,
    { headers: { "X-CSRF-TOKEN": token }, signal },
  );
  return response.data;
}

export const reservationsQuery = (afterId: string | null) =>
  queryOptions({
    queryKey: reservationKeys.list(afterId),
    queryFn: ({ signal }) => getReservations(afterId, signal),
    retry: false,
    structuralSharing: (oldData, newData) => {
      const previous = oldData as ReservationList | undefined;
      const fetched = newData as ReservationList;
      if (!previous) return fetched;
      return replaceEqualDeep(previous, {
        ...fetched,
        reservations: fetched.reservations.map((reservation) => {
          const known = previous.reservations.find(
            (value) => value.id === reservation.id,
          );
          // A stale read cannot undo an authoritative cancellation.
          return known?.state === "cancelled" && reservation.state === "active"
            ? {
                ...reservation,
                state: known.state,
                cancelledAt: known.cancelledAt,
              }
            : reservation;
        }),
      });
    },
    refetchInterval: (query) => {
      if (query.state.error) return false;
      return query.state.data?.reservations.some(
        (reservation) => reservation.state === "active",
      )
        ? 15_000
        : false;
    },
  });
