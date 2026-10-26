import { useEffect, useState } from "react";

export type CurrentPosition =
  | {
      readonly status:
        "idle" | "locating" | "denied" | "unavailable" | "unsupported";
    }
  | {
      readonly status: "granted";
      readonly latitude: number;
      readonly longitude: number;
    };

const idle: CurrentPosition = { status: "idle" };

/** Asks the browser for the position once while enabled; turning it off forgets the result. */
export function useCurrentPosition(enabled: boolean): CurrentPosition {
  const [position, setPosition] = useState<CurrentPosition>(idle);

  useEffect(() => {
    if (!enabled) {
      setPosition(idle);
      return;
    }
    if (!("geolocation" in navigator)) {
      setPosition({ status: "unsupported" });
      return;
    }
    let current = true;
    setPosition({ status: "locating" });
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        if (current)
          setPosition({
            status: "granted",
            latitude: coords.latitude,
            longitude: coords.longitude,
          });
      },
      (failure) => {
        if (current)
          setPosition({
            status:
              failure.code === failure.PERMISSION_DENIED
                ? "denied"
                : "unavailable",
          });
      },
      { timeout: 10_000, maximumAge: 300_000 },
    );
    return () => {
      current = false;
    };
  }, [enabled]);

  return position;
}
