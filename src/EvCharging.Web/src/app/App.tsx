import { PlatformRole } from "@/features/auth/PlatformRole";
import { wrapReactRouterRouting } from "@sentry/react/react-router";
import {
  Anchor,
  AppShell,
  Avatar,
  Button,
  Container,
  Group,
  Menu,
  Loader,
  Stack,
  Text,
  UnstyledButton,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { IconBolt, IconChevronDown, IconLogout } from "@tabler/icons-react";
import {
  Link,
  NavLink,
  Navigate,
  Route,
  Routes,
  useLocation,
  matchPath,
  createPath,
} from "react-router";
import classes from "./App.module.css";
import { AppLoading } from "./AppLoading";
import { StationListPage } from "@/features/stations/components/StationListPage";
import { StationDetailPage } from "@/features/stations/components/StationDetailPage";
import { StationEditPage } from "@/features/stations/components/StationEditPage";
import { AssignedStationsPage } from "@/features/stations/components/AssignedStationsPage";
import { ReservationsPage } from "@/features/reservations/components/ReservationsPage";
import { SignInErrorPage } from "@/features/auth/components/SignInErrorPage";

import { PageHeading } from "@/components/PageHeading";
import { Activity, type ReactNode } from "react";
import { getSignInUrl, sessionQuery } from "@/features/auth/api";

const SentryRoutes = wrapReactRouterRouting(Routes);

export function App() {
  const location = useLocation();
  const session = useQuery(sessionQuery);
  const currentSession = session.isSuccess ? session.data : undefined;
  const roles = currentSession?.user?.roles ?? [];
  const currentUrl = createPath(location);
  const logoutReturnUrl =
    matchPath("/stations", location.pathname) ||
    matchPath("/stations/:stationId", location.pathname)
      ? currentUrl
      : "/stations";

  const signIn = (
    <Button
      component="a"
      size="sm"
      className={`${classes.account} ${classes.accountAction}`}
      href={getSignInUrl(currentUrl)}
    >
      Sign in
    </Button>
  );
  const area = (role: PlatformRole, content: ReactNode) => {
    let fallback: ReactNode = null;
    if (session.isPending) fallback = <AppLoading />;
    else if (session.isError)
      fallback = (
        <Stack>
          <Text role="alert">Unable to check your session.</Text>
          <Button onClick={() => void session.refetch()}>Try again</Button>
        </Stack>
      );
    else if (!currentSession?.signedIn)
      fallback = (
        <PageHeading
          title="Sign in to continue"
          description="Use Sign in in the header to access this area."
        />
      );
    else if (!roles.includes(role))
      fallback = (
        <PageHeading
          title="Access denied"
          description="You do not have permission to access this area."
        />
      );
    return (
      <>
        {fallback}
        <Activity mode={fallback ? "hidden" : "visible"}>{content}</Activity>
      </>
    );
  };

  return (
    <AppShell
      className={classes.shell}
      header={{ height: { base: 112, sm: 72 } }}
      padding={{ base: "md", sm: "xl" }}
    >
      <AppShell.Header className={classes.appHeader}>
        <Container size="lg" h="100%">
          <div className={classes.header}>
            <Anchor
              component={Link}
              to="/"
              className={classes.brand}
              underline="never"
            >
              <IconBolt size={24} stroke={2} aria-hidden="true" />
              EV Charging
            </Anchor>
            <nav className={classes.navigation} aria-label="Main navigation">
              <Anchor
                component={NavLink}
                to="/stations"
                className={classes.navLink}
                underline="never"
              >
                Stations
              </Anchor>
              {roles.includes(PlatformRole.Operator) && (
                <Anchor
                  component={NavLink}
                  to="/operator"
                  className={classes.navLink}
                  underline="never"
                >
                  Operator area
                </Anchor>
              )}
              {roles.includes(PlatformRole.Driver) && (
                <Anchor
                  component={NavLink}
                  to="/reservations"
                  className={classes.navLink}
                  underline="never"
                >
                  My reservations
                </Anchor>
              )}
            </nav>
            {session.isPending ? (
              <Group
                className={classes.account}
                gap="xs"
                role="status"
                aria-label="Loading account"
              >
                <Loader size="xs" />
                <Text size="sm" visibleFrom="sm">
                  Loading account
                </Text>
              </Group>
            ) : session.isError ? (
              <Stack className={classes.account} gap={2} align="flex-end">
                <Text size="xs" role="alert">
                  Account unavailable
                </Text>
                <Button
                  size="compact-xs"
                  variant="subtle"
                  loading={session.isFetching}
                  onClick={() => void session.refetch()}
                >
                  Try again
                </Button>
              </Stack>
            ) : currentSession?.signedIn ? (
              <Menu position="bottom-end" width={220} shadow="md">
                <Menu.Target>
                  <UnstyledButton
                    className={`${classes.account} ${classes.accountAction} ${classes.accountMenu}`}
                    aria-label="Account menu"
                  >
                    <Group gap="xs" wrap="nowrap">
                      <Avatar
                        color="teal"
                        radius="xl"
                        size={34}
                        aria-hidden="true"
                      >
                        {(
                          currentSession.user?.name ??
                          currentSession.user?.username ??
                          ""
                        )
                          .slice(0, 1)
                          .toUpperCase()}
                      </Avatar>
                      <Text size="sm" visibleFrom="sm" maw={180} truncate>
                        {currentSession.user?.name ??
                          currentSession.user?.username}
                      </Text>
                      <IconChevronDown size={16} aria-hidden="true" />
                    </Group>
                  </UnstyledButton>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Label hiddenFrom="sm">
                    {currentSession.user?.name ?? currentSession.user?.username}
                  </Menu.Label>
                  <form
                    method="post"
                    action={`/auth/logout?returnUrl=${encodeURIComponent(logoutReturnUrl)}`}
                  >
                    <input
                      type="hidden"
                      name="__RequestVerificationToken"
                      value={currentSession.antiforgeryToken ?? ""}
                    />
                    <Menu.Item
                      type="submit"
                      mih={44}
                      leftSection={<IconLogout size={18} aria-hidden="true" />}
                    >
                      Sign out
                    </Menu.Item>
                  </form>
                </Menu.Dropdown>
              </Menu>
            ) : (
              signIn
            )}
          </div>
        </Container>
      </AppShell.Header>
      <AppShell.Main className={classes.main}>
        <Container
          size="lg"
          px={{ base: 0, sm: "md" }}
          py={{ base: "sm", sm: "lg" }}
        >
          <SentryRoutes>
            <Route path="/" element={<Navigate to="/stations" replace />} />
            <Route path="/stations" element={<StationListPage />} />
            <Route path="/sign-in-error" element={<SignInErrorPage />} />
            <Route
              path="/stations/:stationId"
              element={
                <StationDetailPage
                  canEdit={roles.includes(PlatformRole.Operator)}
                />
              }
            />
            <Route
              path="/operator"
              element={area(PlatformRole.Operator, <AssignedStationsPage />)}
            />
            <Route
              path="/reservations"
              element={area(PlatformRole.Driver, <ReservationsPage />)}
            />
            <Route
              path="/stations/:stationId/edit"
              element={area(PlatformRole.Operator, <StationEditPage />)}
            />
            <Route
              path="*"
              element={
                <Stack>
                  <PageHeading
                    title="Page not found"
                    description="This page is unavailable."
                  />
                  <Anchor component={Link} to="/">
                    Return home
                  </Anchor>
                </Stack>
              }
            />
          </SentryRoutes>
        </Container>
      </AppShell.Main>
      <footer className={classes.footer}>
        <Container size="lg" px={{ base: 0, sm: "md" }}>
          <Group justify="space-between" gap="xs">
            <Text size="xs" fw={500} c="dimmed">
              EV Charging
            </Text>
            <Text size="xs" c="dimmed">
              System design lab
            </Text>
          </Group>
        </Container>
      </footer>
    </AppShell>
  );
}
