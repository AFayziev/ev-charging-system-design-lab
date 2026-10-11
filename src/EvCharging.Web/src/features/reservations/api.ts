import { queryOptions } from "@tanstack/react-query";
import { api } from "@/lib/api";

export interface Reservation {
  readonly id: string;
  readonly stationId: string;
  readonly stationName: string;
  readonly stationAddress: string;
  readonly timeZoneId: string;
  readonly connectorId: string;
  readonly chargerName: string;
  readonly evseNumber: number;
  readonly connectorNumber: number;
  readonly connectorType: string;
  readonly powerSupply: string;
  readonly ratedPowerKW: number;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly state: "active" | "consumed" | "cancelled" | "expired";
  readonly createdAt: string;
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

export const reservationKeys = {
  lists: ["reservations", "list"] as const,
  list: (afterId: string | null) => ["reservations", "list", afterId] as const,
};

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

export const reservationsQuery = (afterId: string | null) =>
  queryOptions({
    queryKey: reservationKeys.list(afterId),
    queryFn: ({ signal }) => getReservations(afterId, signal),
    retry: false,
  });
