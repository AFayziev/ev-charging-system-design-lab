import { Button } from "@mantine/core";
import { Link } from "react-router";
import { FailureAlert } from "@/components/StatusAlert";
import { getReservationFailure } from "../errors";

export function ReservationFailure({
  error,
  retry,
  booking = false,
}: {
  error: unknown;
  retry?: () => void;
  booking?: boolean;
}) {
  const failure = getReservationFailure(error, booking);
  return (
    <FailureAlert failure={failure}>
      {failure.recovery === "retry" && retry && (
        <Button variant="white" onClick={retry}>
          Try again
        </Button>
      )}
      {failure.recovery === "reservations" && (
        <Button component={Link} variant="white" to="/reservations">
          My reservations
        </Button>
      )}
      {failure.recovery === "first-page" && (
        <Button component={Link} variant="white" to="/reservations">
          First page
        </Button>
      )}
    </FailureAlert>
  );
}
