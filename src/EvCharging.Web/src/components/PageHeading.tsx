import { Stack, Text, Title } from "@mantine/core";
import type { ReactNode } from "react";

export function PageHeading({
  title,
  description,
  children,
}: {
  title: string;
  description: string;
  children?: ReactNode;
}) {
  return (
    <Stack gap={8}>
      <Title order={1}>{title}</Title>
      {children}
      <Text c="dimmed">{description}</Text>
    </Stack>
  );
}
