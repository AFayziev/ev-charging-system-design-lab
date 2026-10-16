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
