import axios, { HttpStatusCode } from "axios";
import {
  getHttpFailure,
  getHttpProblemCode,
  getHttpStatus,
  type HttpFailure,
} from "@/lib/http-errors";

const searchUnavailableCode = "search-unavailable";

/** The index is down (typed 503) or the search service is (gateway 502 or 504); browsing still works. */
export function isSearchUnavailable(error: unknown): boolean {
  switch (getHttpStatus(error)) {
    case HttpStatusCode.ServiceUnavailable:
      return getHttpProblemCode(error) === searchUnavailableCode;
    case HttpStatusCode.BadGateway:
    case HttpStatusCode.GatewayTimeout:
      return true;
    default:
      return false;
  }
}

export interface StationEditFailure extends HttpFailure {
  readonly recovery?: "reload";
}

export interface StationEditorLoadFailure extends StationEditFailure {
  readonly blocksSave: boolean;
}

export function getStationEditorLoadFailure(
  error: unknown,
): StationEditorLoadFailure {
  const status = getHttpStatus(error);
  return {
    ...getHttpFailure(error),
    recovery: "reload",
    blocksSave:
      status === HttpStatusCode.Unauthorized ||
      status === HttpStatusCode.Forbidden ||
      status === HttpStatusCode.NotFound,
  };
}

export function getStationEditFailure(
  error: unknown,
): StationEditFailure | null {
  if (axios.isCancel(error)) return null;
  const failure = getHttpFailure(error, "write");
  switch (getHttpStatus(error)) {
    case HttpStatusCode.BadRequest:
    case HttpStatusCode.UnprocessableEntity:
    case HttpStatusCode.Forbidden:
    case HttpStatusCode.NotFound:
      return failure;
    default:
      return { ...failure, recovery: "reload" };
  }
}
