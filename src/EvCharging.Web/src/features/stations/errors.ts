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

/** The photo's slot count, gallery version, or state moved since the editor was loaded. */
export const isConcurrencyConflict = (error: unknown): boolean =>
  getHttpProblemCode(error) === "concurrency_conflict";

export const isGalleryFull = (error: unknown): boolean =>
  getHttpProblemCode(error) === "media_limit_reached";

/** Why processing refused an upload, by the failure code the editor reports. */
export function getPhotoFailureMessage(code: string | null): string {
  switch (code) {
    case "upload_missing":
      return "The file never reached storage. Try again.";
    case "upload_expired":
      return "The upload took too long and expired. Try again on a steadier connection.";
    case "undecodable":
      return "This file isn't a readable image.";
    case "too_many_pixels":
      return "This image is too large in pixels. Resize it and try again.";
    case "unsupported_format":
      return "Only JPEG, PNG, or WebP images are accepted.";
    default:
      return "The photo could not be processed. Try again.";
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
