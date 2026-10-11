import { Alert, Button, Stack, Text } from "@mantine/core";
import { createPath, Link, useLocation } from "react-router";
import { getSignInUrl } from "@/features/auth/api";
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
  const location = useLocation();
  const failure = getReservationFailure(error, booking);
  return (
    <Alert color="red" role="alert">
      <Stack gap="xs">
        <Text size="sm">{failure.message}</Text>
        {failure.recovery === "sign-in" && (
          <Button
            component="a"
            variant="white"
            href={getSignInUrl(createPath(location))}
          >
            Sign in
          </Button>
        )}
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
      </Stack>
    </Alert>
  );
}
