import axios from "axios";

interface ValidationProblemDetails {
  readonly errors?: Record<string, string[]>;
}

export function getHttpStatus(error: unknown): number | undefined {
  if (axios.isAxiosError(error)) return error.response?.status;
  return undefined;
}

export function getValidationErrors(
  error: unknown,
): Record<string, string[]> | undefined {
  if (!axios.isAxiosError<ValidationProblemDetails>(error)) return undefined;
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
