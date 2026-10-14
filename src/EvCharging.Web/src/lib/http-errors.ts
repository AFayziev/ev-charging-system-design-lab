import axios, { HttpStatusCode } from "axios";

export interface HttpFailure {
  readonly title: string;
  readonly message: string;
}

interface ProblemDetails {
  readonly title?: unknown;
  readonly detail?: unknown;
  readonly code?: unknown;
}

export function getHttpProblemCode(error: unknown): string | undefined {
  if (!axios.isAxiosError<ProblemDetails>(error)) return undefined;
  const code = error.response?.data?.code;
  return typeof code === "string" ? code : undefined;
}

interface ValidationProblemDetails {
  readonly errors?: Record<string, string[]>;
}

export function getHttpStatus(error: unknown): number | undefined {
  if (axios.isAxiosError(error)) return error.response?.status;
  return undefined;
}

export function getHttpFailure(
  error: unknown,
  operation: "read" | "write" = "read",
): HttpFailure {
  const status = getHttpStatus(error);
  const fallback = getHttpFallback(
    status,
    operation,
    getValidationErrors(error),
  );
  if (
    status === undefined ||
    status < HttpStatusCode.BadRequest ||
    status >= HttpStatusCode.InternalServerError ||
    !axios.isAxiosError<ProblemDetails>(error)
  )
    return fallback;

  const problem = error.response?.data;
  if (typeof problem?.detail !== "string" || !problem.detail.trim())
    return fallback;
  return {
    title:
      typeof problem.title === "string" && problem.title.trim()
        ? problem.title
        : fallback.title,
    message: problem.detail,
  };
}

function getHttpFallback(
  status: number | undefined,
  operation: "read" | "write",
  fieldErrors: Record<string, string[]> | undefined,
): HttpFailure {
  switch (status) {
    case HttpStatusCode.BadRequest:
    case HttpStatusCode.UnprocessableEntity:
      if (fieldErrors)
        return {
          title:
            operation === "write" ? "Review the fields" : "Request rejected",
          message:
            operation === "write"
              ? "Review the fields and try again."
              : Object.values(fieldErrors).flat().join(" "),
        };
      return {
        title: "Request rejected",
        message: "The request was rejected. Review the details and try again.",
      };
    case HttpStatusCode.Unauthorized:
      return {
        title: "Authentication failed",
        message: "This request could not be authenticated. Try again.",
      };
    case HttpStatusCode.Forbidden:
      return {
        title: "Access denied",
        message: "You do not have permission to perform this action.",
      };
    case HttpStatusCode.NotFound:
      return {
        title: "Resource not found",
        message: "This resource is unavailable or no longer exists.",
      };
    case HttpStatusCode.Conflict:
      return {
        title: "Conflict",
        message:
          "The request conflicts with the current data. Reload it before trying again.",
      };
    case HttpStatusCode.TooManyRequests:
      return {
        title: "Too many requests",
        message: "Please wait before trying again.",
      };
    default:
      return operation === "write"
        ? {
            title: "Changes not confirmed",
            message:
              "The save could not be confirmed. Check the current data before trying again.",
          }
        : {
            title: "Information unavailable",
            message:
              "We could not load the requested information. Please try again.",
          };
  }
}

export function getValidationErrors(
  error: unknown,
): Record<string, string[]> | undefined {
  if (!axios.isAxiosError<ValidationProblemDetails>(error)) return undefined;
  const status = error.response?.status;
  if (
    status !== HttpStatusCode.BadRequest &&
    status !== HttpStatusCode.UnprocessableEntity
  )
    return undefined;
  const errors = error.response?.data?.errors;
  if (!errors || typeof errors !== "object" || Array.isArray(errors))
    return undefined;
  const messages = Object.values(errors);
  if (
    messages.length === 0 ||
    !messages.every(
      (values) =>
        Array.isArray(values) &&
        values.length > 0 &&
        values.every((value: unknown) => typeof value === "string"),
    )
  )
    return undefined;
  return errors;
}

export function getFormValidationErrors(
  error: unknown,
): Record<string, string> | undefined {
  const errors = getValidationErrors(error);
  if (!errors) return undefined;
  return Object.fromEntries(
    Object.entries(errors).map(([key, messages]) => [
      key.charAt(0).toLowerCase() + key.slice(1),
      messages.join(" "),
    ]),
  );
}
