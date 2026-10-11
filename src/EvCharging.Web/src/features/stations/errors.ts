import axios, { HttpStatusCode } from "axios";
import { getHttpStatus, getValidationErrors } from "@/lib/http-errors";
import { signInRequiredMessage } from "@/features/auth/errors";

export interface StationEditFailure {
  readonly title: string;
  readonly message: string;
  readonly recovery?: "reload" | "sign-in";
}

export interface StationEditorLoadFailure extends StationEditFailure {
  readonly blocksSave: boolean;
}

export function getStationEditorLoadFailure(
  error: unknown,
): StationEditorLoadFailure {
  switch (getHttpStatus(error)) {
    case HttpStatusCode.Unauthorized:
      return {
        title: "Sign in required",
        message: signInRequiredMessage,
        recovery: "sign-in",
        blocksSave: true,
      };
    case HttpStatusCode.Forbidden:
      return {
        title: "Access denied",
        message: "This station is not assigned to your operator account.",
        recovery: "reload",
        blocksSave: true,
      };
    case HttpStatusCode.NotFound:
      return {
        title: "Station not found",
        message: "This station is unavailable or no longer exists.",
        recovery: "reload",
        blocksSave: true,
      };
    default:
      return {
        title: "Station information unavailable",
        message: "We could not load station information. Please try again.",
        recovery: "reload",
        blocksSave: false,
      };
  }
}

export function getStationEditFailure(
  error: unknown,
): StationEditFailure | null {
  if (axios.isCancel(error)) return null;

  switch (getHttpStatus(error)) {
    case HttpStatusCode.BadRequest:
      if (getValidationErrors(error))
        return {
          title: "Review the fields",
          message: "Review the fields and try again.",
        };
      return {
        title: "Request rejected",
        message:
          "The station update request was rejected. Your draft is still in the form.",
      };
    case HttpStatusCode.Unauthorized:
      return {
        title: "Sign in required",
        message: signInRequiredMessage,
        recovery: "sign-in",
      };
    case HttpStatusCode.Forbidden:
      return {
        title: "Access denied",
        message: "You no longer have permission to edit this station.",
      };
    case HttpStatusCode.NotFound:
      return {
        title: "Station not found",
        message:
          "This station is unavailable or no longer exists. Your draft is still in the form.",
      };
    case HttpStatusCode.Conflict:
      return {
        title: "Station changed",
        message:
          "Another edit was saved after you loaded this station. Your draft is still in the form. Reload the current station before editing again.",
        recovery: "reload",
      };
    default:
      return {
        title: "Changes not confirmed",
        message:
          "The save could not be confirmed. Your draft is still in the form; check the current station before trying again.",
        recovery: "reload",
      };
  }
}
