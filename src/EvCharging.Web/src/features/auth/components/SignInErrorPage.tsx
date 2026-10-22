import { Button, Stack } from "@mantine/core";
import { StatusAlert } from "@/components/StatusAlert";
import { PageHeading } from "@/components/PageHeading";
import { getSignInUrl } from "../api";

export function SignInErrorPage() {
  return (
    <Stack>
      <PageHeading
        title="Unable to complete sign-in"
        description="Your account is not ready to use."
      />
      <StatusAlert tone="error">
        Your account could not be prepared. Registration requires a verified
        email. If you have completed verification, try signing in again.
      </StatusAlert>
      <Button component="a" href={getSignInUrl("/stations")}>
        Try signing in again
      </Button>
    </Stack>
  );
}
