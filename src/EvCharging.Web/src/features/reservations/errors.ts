import { HttpStatusCode } from "axios";
import { getHttpStatus } from "@/lib/http-errors";
import { signInRequiredMessage } from "@/features/auth/errors";

export interface ReservationFailure {
  readonly message: string;
  readonly recovery?: "sign-in" | "retry" | "reservations" | "first-page";
}

export function getReservationFailure(
  error: unknown,
  booking = false,
): ReservationFailure {
  switch (getHttpStatus(error)) {
    case HttpStatusCode.Unauthorized:
      return {
        message: signInRequiredMessage,
        recovery: "sign-in",
      };
    case HttpStatusCode.Forbidden:
      return {
        message: booking
          ? "Your account cannot book this connector."
          : "Access denied. Your account cannot view reservations.",
      };
    case HttpStatusCode.NotFound:
      return {
        message: booking
          ? "This connector is no longer available."
          : "Reservations unavailable.",
      };
    case HttpStatusCode.BadRequest:
      return booking
        ? { message: "Check the booking details." }
        : {
            message:
              "This reservation page is no longer available. Return to the first page.",
            recovery: "first-page",
          };
    case HttpStatusCode.Conflict:
      return {
        message:
          "This connector is already reserved during that slot. Choose another time or connector.",
      };
    default:
      return booking
        ? {
            message:
              "Booking not confirmed. Your draft has been retained. Check My reservations before submitting again.",
            recovery: "reservations",
          }
        : {
            message: "Reservations unavailable. Try again.",
            recovery: "retry",
          };
  }
}
