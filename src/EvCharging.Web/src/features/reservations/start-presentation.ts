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
        text: "Charging may have started. We are checking with the station. Please do not submit another start.",
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
    case "rejected":
      return {
        color: "red",
        title: "Charging could not start",
        text: canRetry
          ? `The station rejected the start. Your reservation is still available until ${formatStartDeadline(arrivalDeadline)}.`
          : "The station rejected the start. The start window has ended.",
        icon: IconAlertCircle,
      };
    default:
      return undefined;
  }
}
