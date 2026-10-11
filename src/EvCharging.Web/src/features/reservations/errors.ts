import { HttpStatusCode } from "axios";
import {
  getHttpFailure,
  getHttpStatus,
  type HttpFailure,
} from "@/lib/http-errors";

export interface ReservationFailure extends HttpFailure {
  readonly recovery?: "retry" | "reservations" | "first-page";
}

export function getReservationFailure(
  error: unknown,
  booking = false,
): ReservationFailure {
  const failure = getHttpFailure(error, booking ? "write" : "read");
  switch (getHttpStatus(error)) {
    case HttpStatusCode.Unauthorized:
      return { ...failure, recovery: "retry" };
    case HttpStatusCode.BadRequest:
      return booking ? failure : { ...failure, recovery: "first-page" };
    case HttpStatusCode.UnprocessableEntity:
    case HttpStatusCode.Forbidden:
    case HttpStatusCode.NotFound:
    case HttpStatusCode.Conflict:
      return failure;
    default:
      return { ...failure, recovery: booking ? "reservations" : "retry" };
  }
}
