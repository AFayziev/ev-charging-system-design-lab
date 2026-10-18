import {
  IconAlertCircle,
  IconCircleCheck,
  IconClock,
  IconHelpCircle,
} from "@tabler/icons-react";
import type { StartOperation } from "./api";
import { formatStartDeadline } from "./time-formatting";

export function getStartPresentation(
  operation: StartOperation | null,
  canRetry: boolean,
  arrivalDeadline: string,
) {
  const automatic = operation?.automaticChecksEndAt;
  const ended = automatic && Date.now() >= Date.parse(automatic);
  if (
    automatic &&
    (operation.outcome === "pending" || operation.outcome === "uncertain")
  ) {
    return {
      color: ended ? "orange" : "blue",
      badge:
        operation.outcome === "pending" ? "Start pending" : "Start unresolved",
      title: ended
        ? "Automatic checking ended"
        : "Automatic status checks scheduled",
      text: ended
        ? "Automatic checks have ended without a definite result. You can check status again. Charging may still have started; do not submit another start."
        : "We will check for the station's result during a limited period. You can also check status yourself. Charging may still have started; do not submit another start.",
      icon: ended ? IconHelpCircle : IconClock,
    };
  }

  switch (operation?.outcome) {
    case "confirmed":
      return {
        color: "green",
        badge: "Reservation used",
        title: "Charging started",
        text: `The station confirmed your start${operation.startedAt ? ` at ${formatStartDeadline(operation.startedAt)}` : ""}.${operation.lifecycle === "active" ? " Your charging session is active." : ""}`,
        icon: IconCircleCheck,
      };
    case "uncertain":
      return {
        color: "orange",
        badge: "Start unresolved",
        title: "Waiting for confirmation",
        text: "We could not confirm whether charging started. Please check the charger before taking further action. Do not submit another start.",
        icon: IconHelpCircle,
      };
    case "pending":
      return {
        color: "blue",
        badge: "Start pending",
        title: "Confirming start",
        text: "Your request was accepted. Waiting for the station to confirm charging has started.",
        icon: IconClock,
      };
    case "rejected": {
      const reason =
        operation.failureReason === "chargingServiceUnavailable"
          ? "Charging did not start because we could not connect to the charging service."
          : "Charging did not start.";
      const availability = canRetry
        ? `Your reservation is still available until ${formatStartDeadline(arrivalDeadline)}.`
        : "The start window has ended.";
      return {
        color: "red",
        title: "Charging could not start",
        text: `${reason} ${availability}`,
        icon: IconAlertCircle,
      };
    }
    default:
      return undefined;
  }
}
