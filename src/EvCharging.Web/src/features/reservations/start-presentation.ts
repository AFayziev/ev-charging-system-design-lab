import type { Presentation } from "@/components/tones";
import { formatClock } from "@/lib/time-formatting";
import type { StartOperation } from "./api";

export function getStartPresentation(
  operation: StartOperation | null,
  canRetry: boolean,
  arrivalDeadline: string,
): Presentation | undefined {
  const automatic = operation?.automaticChecksEndAt;
  const ended = automatic && Date.now() >= Date.parse(automatic);
  if (
    automatic &&
    (operation.outcome === "pending" || operation.outcome === "uncertain")
  ) {
    return {
      tone: ended ? "unresolved" : "pending",
      badge:
        operation.outcome === "pending" ? "Start pending" : "Start unresolved",
      title: ended
        ? "Waiting to check again"
        : "Automatic status checks scheduled",
      text: ended
        ? "Recent automatic checks did not get a definite result. We will keep checking automatically, and you can also check status yourself. Charging may still have started; do not submit another start."
        : "We are checking for the station's result automatically. You can also check status yourself. Charging may still have started; do not submit another start.",
    };
  }

  switch (operation?.outcome) {
    case "confirmed":
      return {
        tone: "success",
        badge: "Reservation used",
        title: "Charging started",
        text: `The station confirmed your start${operation.startedAt ? ` at ${formatClock(operation.startedAt)}` : ""}.${operation.lifecycle === "active" ? " Your charging session is active." : ""}`,
      };
    case "uncertain":
      return {
        tone: "unresolved",
        badge: "Start unresolved",
        title: "Waiting for confirmation",
        text: "We could not confirm whether charging started. Please check the charger before taking further action. Do not submit another start.",
      };
    case "pending":
      return {
        tone: "pending",
        badge: "Start pending",
        title: "Confirming start",
        text: "Your request was accepted. Waiting for the station to confirm charging has started.",
      };
    case "rejected": {
      const reason =
        operation.failureReason === "chargingServiceUnavailable"
          ? "Charging did not start because we could not connect to the charging service."
          : "Charging did not start.";
      const availability = canRetry
        ? `Your reservation is still available until ${formatClock(arrivalDeadline)}.`
        : "The start window has ended.";
      return {
        tone: "error",
        title: "Charging could not start",
        text: `${reason} ${availability}`,
      };
    }
    default:
      return undefined;
  }
}
