import type { Presentation } from "@/components/tones";
import type { SessionLifecycle, StopOperation } from "./api";

export function getStopPresentation(
  operation: StopOperation | null,
  lifecycle?: SessionLifecycle,
): Presentation | undefined {
  // The session's end is established by the provider's final reading, whatever became of any stop request.
  if (lifecycle === "completed") {
    return {
      tone: "success",
      badge: "Charging completed",
      title: "Charging completed",
      text: "The station stopped charging and your session is complete. Its final usage is on the charging page.",
    };
  }

  switch (operation?.outcome) {
    case "pending":
      return {
        tone: "pending",
        badge: "Stopping",
        title: "Stopping charging",
        text: "Your stop request was accepted. Waiting for the station to confirm charging has stopped.",
      };
    case "uncertain":
      return {
        tone: "unresolved",
        badge: "Stop unresolved",
        title: "Waiting for confirmation",
        text: "We could not confirm whether charging stopped. Charging may still be in progress, so please check the charger.",
      };
    case "confirmed":
      return {
        tone: "success",
        badge: "Stopped · final usage pending",
        title: "Charging stopped",
        text: "The station confirmed your stop. Your session stays open until its final usage is available.",
      };
    case "rejected":
      return {
        tone: "error",
        title: "Charging did not stop",
        text: "The station rejected the stop request. Your charging session is still active. You can try stopping again.",
      };
    default:
      return undefined;
  }
}
