import { Alert, Stack, Text } from "@mantine/core";
import type { ComponentPropsWithoutRef, ReactNode } from "react";
import { tones, type Tone } from "./tones";

type StatusAlertProps = Omit<
  ComponentPropsWithoutRef<typeof Alert>,
  "color" | "icon"
> & { tone: Tone };

/** An alert whose color, icon and announcement follow its tone; problems are announced as alerts. */
export function StatusAlert({ tone, role, ...props }: StatusAlertProps) {
  const { color, icon: Icon } = tones[tone];
  return (
    <Alert
      color={color}
      icon={<Icon size={20} aria-hidden />}
      role={
        role ?? (tone === "error" || tone === "warning" ? "alert" : "status")
      }
      {...props}
    />
  );
}

/** A failed request: its title and message, then whatever the driver can do about it. */
export function FailureAlert({
  failure,
  children,
}: {
  failure: { readonly title: string; readonly message: string };
  children?: ReactNode;
}) {
  return (
    <StatusAlert tone="error" title={failure.title}>
      <Stack gap="xs">
        <Text size="sm">{failure.message}</Text>
        {children}
      </Stack>
    </StatusAlert>
  );
}
