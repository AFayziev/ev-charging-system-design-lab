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
  Stack,
  Text,
  Title,
  UnstyledButton,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { IconBolt, IconChevronDown, IconLogout } from "@tabler/icons-react";
import {
  Link,
  NavLink,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from "react-router";
import classes from "./App.module.css";
import { AppLoading } from "./AppLoading";
import { StationListPage } from "@/features/stations/components/StationListPage";
import { StationDetailPage } from "@/features/stations/components/StationDetailPage";
import { sessionQuery } from "@/features/auth/api";

const SentryRoutes = wrapReactRouterRouting(Routes);

export function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const session = useQuery(sessionQuery);
  const roles = session.data?.user?.roles ?? [];
  const homePath =
    session.isSuccess && session.data.signedIn
      ? roles.includes(PlatformRole.Driver)
        ? "/stations"
        : roles.includes(PlatformRole.Operator)
          ? "/operator"
          : null
      : null;

  useEffect(() => {
    if (location.pathname === "/" && homePath) {
      void navigate(homePath, { replace: true });
    }
  }, [homePath, location.pathname, navigate]);

  if (session.isPending || (location.pathname === "/" && homePath)) {
    return <AppLoading />;
  }

  const signIn = (
    <Button
      component="a"
      href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname + location.search + location.hash)}`}
    >
      Sign in
    </Button>
  );
  const area = (
    role: PlatformRole,
    title: string,
    content?: React.ReactNode,
  ) =>
    session.isError ? (
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
      (content ?? (
        <Stack>
          <Title order={1}>{title}</Title>
          <Text c="dimmed">No activity to display yet.</Text>
        </Stack>
      ))
    );

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
              {roles.includes(PlatformRole.Driver) && (
                <Anchor
                  component={NavLink}
                  to="/stations"
                  className={classes.navLink}
                  underline="never"
                >
                  Stations
                </Anchor>
              )}
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
            </nav>
            {session.data?.signedIn && (
              <Menu position="bottom-end" width={220} shadow="md">
                <Menu.Target>
                  <UnstyledButton
                    className={classes.account}
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
                          session.data.user?.name ??
                          session.data.user?.username ??
                          ""
                        )
                          .slice(0, 1)
                          .toUpperCase()}
                      </Avatar>
                      <Text size="sm" visibleFrom="sm" maw={180} truncate>
                        {session.data.user?.name ?? session.data.user?.username}
                      </Text>
                      <IconChevronDown size={16} aria-hidden="true" />
                    </Group>
                  </UnstyledButton>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Label hiddenFrom="sm">
                    {session.data.user?.name ?? session.data.user?.username}
                  </Menu.Label>
                  <form method="post" action="/auth/logout?returnUrl=/">
                    <input
                      type="hidden"
                      name="__RequestVerificationToken"
                      value={session.data.antiforgeryToken ?? ""}
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
            )}
          </div>
        </Container>
      </AppShell.Header>
      <AppShell.Main>
        <Container size="lg" px={{ base: 0, sm: "md" }}>
          <SentryRoutes>
            <Route
              path="/"
              element={
                <Stack>
                  <Title order={1}>Welcome to EV Charging</Title>
                  <Text c="dimmed">
                    A shared workspace for drivers and operators.
                  </Text>
                  {session.isError ? (
                    <Stack>
                      <Text role="alert">Unable to check your session.</Text>
                      <Button onClick={() => void session.refetch()}>
                        Try again
                      </Button>
                    </Stack>
                  ) : !session.data?.signedIn ? (
                    signIn
                  ) : (
                    <Text>
                      Your account does not have access to a workspace.
                    </Text>
                  )}
                </Stack>
              }
            />
            <Route
              path="/stations"
              element={area(
                PlatformRole.Driver,
                "Stations",
                <StationListPage />,
              )}
            />
            <Route
              path="/stations/:stationId"
              element={area(
                PlatformRole.Driver,
                "Station",
                <StationDetailPage />,
              )}
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
        </Container>
      </AppShell.Main>
    </AppShell>
  );
}
