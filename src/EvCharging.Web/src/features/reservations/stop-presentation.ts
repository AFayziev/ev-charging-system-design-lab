import {
  IconAlertCircle,
  IconCircleCheck,
  IconClock,
  IconHelpCircle,
} from "@tabler/icons-react";
import type { StopOperation } from "./api";

export function getStopPresentation(operation: StopOperation | null) {
  switch (operation?.outcome) {
    case "pending":
      return {
        color: "blue",
        badge: "Stopping",
        title: "Stopping charging",
        text: "Your stop request was accepted. Waiting for the station to confirm charging has stopped.",
        icon: IconClock,
      };
    case "uncertain":
      return {
        color: "orange",
        badge: "Stop unresolved",
        title: "Waiting for confirmation",
        text: "We could not confirm whether charging stopped. Charging may still be in progress, so please check the charger.",
        icon: IconHelpCircle,
      };
    case "confirmed":
      return {
        color: "green",
        badge: "Stopped · final usage pending",
        title: "Charging stopped",
        text: "The station confirmed your stop. Your session stays open until its final usage is available.",
        icon: IconCircleCheck,
      };
    case "rejected":
      return {
        color: "red",
        title: "Charging did not stop",
        text: "The station rejected the stop request. Your charging session is still active. You can try stopping again.",
        icon: IconAlertCircle,
      };
    default:
      return undefined;
  }
}
