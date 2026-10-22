import {
  IconAlertCircle,
  IconCircleCheck,
  IconClock,
  IconHelpCircle,
  IconInfoCircle,
} from "@tabler/icons-react";

/**
 * What a message tells the driver, which fixes its color and icon on every page:
 * pending (waiting for a result), unresolved (the outcome is not known), success, warning, error, info,
 * and neutral (something that no longer applies).
 */
export const tones = {
  pending: { color: "blue", icon: IconClock },
  unresolved: { color: "orange", icon: IconHelpCircle },
  success: { color: "green", icon: IconCircleCheck },
  warning: { color: "orange", icon: IconAlertCircle },
  error: { color: "red", icon: IconAlertCircle },
  info: { color: "blue", icon: IconInfoCircle },
  neutral: { color: "gray", icon: IconClock },
} as const;

export type Tone = keyof typeof tones;

/** What a status tells the driver; the badge is its short form where one is shown. */
export interface Presentation {
  readonly tone: Tone;
  readonly badge?: string;
  readonly title: string;
  readonly text: string;
}
