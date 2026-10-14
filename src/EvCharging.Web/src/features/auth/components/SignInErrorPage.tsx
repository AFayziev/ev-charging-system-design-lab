import { Alert, Button, Stack } from "@mantine/core";
import { IconAlertCircle } from "@tabler/icons-react";
import { PageHeading } from "@/components/PageHeading";
import { getSignInUrl } from "../api";

export function SignInErrorPage() {
  return (
    <Stack>
      <PageHeading
        title="Unable to complete sign-in"
        description="Your account is not ready to use."
      />
      <Alert color="red" icon={<IconAlertCircle size={20} />}>
        Your account could not be prepared. Registration requires a verified email.
        If you have completed verification, try signing in again.
      </Alert>
      <Button component="a" href={getSignInUrl("/stations")}>
        Try signing in again
      </Button>
    </Stack>
  );
}
