import { PlatformRole } from "../features/auth/PlatformRole";
import { wrapReactRouterRouting } from "@sentry/react/react-router";
import {
  Anchor,
  AppShell,
  Button,
  Container,
  Group,
  Paper,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { Link, Route, Routes, useLocation } from "react-router";
import { sessionQuery } from "../features/auth/api";

const SentryRoutes = wrapReactRouterRouting(Routes);

export function App() {
  const location = useLocation();
  const session = useQuery(sessionQuery);
  const roles = session.data?.user?.roles ?? [];
  const signIn = (
    <Button
      component="a"
      href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`}
    >
      Sign in
    </Button>
  );
  const area = (role: PlatformRole, title: string) =>
    session.isPending ? (
      <Text role="status">Checking your session…</Text>
    ) : session.isError ? (
      <Stack>
        <Text role="alert">Unable to check your session.</Text>
        <Button onClick={() => void session.refetch()}>Try again</Button>
      </Stack>
    ) : !session.data?.signedIn ? (
      <Stack>
        <Title order={1}>Sign in to continue</Title>
        {signIn}
      </Stack>
    ) : !roles.includes(role) ? (
      <Stack>
        <Title order={1}>Access denied</Title>
        <Text>You do not have permission to access this area.</Text>
      </Stack>
    ) : (
      <Stack>
        <Title order={1}>{title}</Title>
        <Text c="dimmed">No activity to display yet.</Text>
      </Stack>
    );

  return (
    <AppShell header={{ height: 64 }} padding="xl">
      <AppShell.Header>
        <Container size="md" h="100%">
          <Group h="100%" justify="space-between">
            <Group>
              <Anchor component={Link} to="/" fw={700} c="teal">
                EV Charging
              </Anchor>
              {roles.includes(PlatformRole.Driver) && (
                <Anchor component={Link} to="/driver">
                  Driver area
                </Anchor>
              )}
              {roles.includes(PlatformRole.Operator) && (
                <Anchor component={Link} to="/operator">
                  Operator area
                </Anchor>
              )}
            </Group>
            {session.data?.signedIn && (
              <Group>
                <Text>
                  {session.data.user?.name ?? session.data.user?.username}
                </Text>
                <form method="post" action="/auth/logout?returnUrl=/">
                  <input
                    type="hidden"
                    name="__RequestVerificationToken"
                    value={session.data.antiforgeryToken ?? ""}
                  />
                  <Button type="submit" variant="light">
                    Sign out
                  </Button>
                </form>
              </Group>
            )}
          </Group>
        </Container>
      </AppShell.Header>
      <AppShell.Main>
        <Container size="md">
          <Paper withBorder radius="md" p="xl">
            <SentryRoutes>
              <Route
                path="/"
                element={
                  <Stack>
                    <Title order={1}>Welcome to EV Charging</Title>
                    <Text c="dimmed">
                      A shared workspace for drivers and operators.
                    </Text>
                    {session.isPending ? (
                      <Text role="status">Checking your session…</Text>
                    ) : session.isError ? (
                      <Stack>
                        <Text role="alert">Unable to check your session.</Text>
                        <Button onClick={() => void session.refetch()}>
                          Try again
                        </Button>
                      </Stack>
                    ) : !session.data?.signedIn ? (
                      signIn
                    ) : (
                      <Text>Select your area to continue.</Text>
                    )}
                  </Stack>
                }
              />
              <Route
                path="/driver"
                element={area(PlatformRole.Driver, "Driver area")}
              />
              <Route
                path="/operator"
                element={area(PlatformRole.Operator, "Operator area")}
              />
              <Route
                path="*"
                element={
                  <Stack>
                    <Title order={1}>Page not found</Title>
                    <Text c="dimmed">This page is unavailable.</Text>
                    <Anchor component={Link} to="/">
                      Return home
                    </Anchor>
                  </Stack>
                }
              />
            </SentryRoutes>
          </Paper>
        </Container>
      </AppShell.Main>
    </AppShell>
  );
}
