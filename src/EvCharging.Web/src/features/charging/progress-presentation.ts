import type { Presentation } from "@/components/tones";
import { formatClockWithSeconds } from "@/lib/time-formatting";
import type { ChargingProgress, ObservationState } from "./api";

const stateLabels: Record<ObservationState, string> = {
  available: "Available",
  charging: "Charging",
  stopped: "Stopped",
  faulted: "Faulted",
  offline: "Offline",
  unknown: "Unknown",
};

/**
 * What the driver is told about a session. Silence is shown as the last report with its source time; it is
 * never shown as stopped or offline.
 */
export function getProgressPresentation(
  progress: ChargingProgress,
): Presentation {
  const { lifecycle, reading, isStale } = progress;

  if (lifecycle === "starting") {
    return {
      tone: "pending",
      title: "Confirming start",
      text: "Waiting for the station to confirm charging has started.",
    };
  }

  if (lifecycle === "failed") {
    return {
      tone: "error",
      title: "Charging did not start",
      text: "This charging session did not start.",
    };
  }

  if (reading === null) {
    return isStale
      ? {
          tone: "unresolved",
          title: "No charging data since start",
          text: "The charger has not reported anything since charging started. Please check the charger.",
        }
      : {
          tone: "pending",
          title: "Waiting for charger data",
          text: "Charging has started. The first report from the charger is on its way.",
        };
  }

  const reported = formatClockWithSeconds(reading.occurredAt);
  if (lifecycle === "stopping") {
    return {
      tone: "pending",
      title: "Stopping charging",
      text: reading.isFinalEnergy
        ? `The station stopped charging. Final report ${reported}; final usage follows when the session completes.`
        : `Waiting for the station to confirm charging has stopped. Last report ${reported}.`,
    };
  }

  const label = stateLabels[reading.state];
  return isStale
    ? {
        tone: "unresolved",
        title: `Last reported ${label.toLowerCase()}`,
        text: `Last update ${reported}. The charger has not reported since, so this may no longer be current.`,
      }
    : {
        tone: reading.state === "charging" ? "success" : "info",
        title: label,
        text: `Last update ${reported}.`,
      };
}
