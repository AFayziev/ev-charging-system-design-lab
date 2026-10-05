import {
  Anchor,
  AppShell,
  Container,
  Group,
  Paper,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { Link, Route, Routes } from "react-router";

export function App() {
  return (
    <AppShell header={{ height: 64 }} padding="xl">
      <AppShell.Header>
        <Container size="md" h="100%">
          <Group h="100%">
            <Anchor component={Link} to="/" fw={700} c="teal">
              EV Charging
            </Anchor>
          </Group>
        </Container>
      </AppShell.Header>
      <AppShell.Main>
        <Container size="md">
          <Paper withBorder radius="md" p="xl">
            <Routes>
              <Route
                path="/"
                element={
                  <Stack>
                    <Title order={1}>Welcome to EV Charging</Title>
                    <Text c="dimmed">
                      A shared workspace for drivers and operators.
                    </Text>
                  </Stack>
                }
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
            </Routes>
          </Paper>
        </Container>
      </AppShell.Main>
    </AppShell>
  );
}
