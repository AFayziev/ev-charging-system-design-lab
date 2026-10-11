import axios, { HttpStatusCode } from "axios";
import {
  getHttpFailure,
  getHttpStatus,
  type HttpFailure,
} from "@/lib/http-errors";

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
