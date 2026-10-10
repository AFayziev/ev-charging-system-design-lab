import { Anchor, Group, Text } from "@mantine/core";
import { IconArrowLeft } from "@tabler/icons-react";
import { Link } from "react-router";
import classes from "./BackLink.module.css";

export function BackLink({ to, children }: { to: string; children: string }) {
  return (
    <Anchor
      component={Link}
      to={to}
      className={classes.link}
      underline="never"
      w="fit-content"
      mih={44}
      display="inline-flex"
    >
      <Group gap={6}>
        <IconArrowLeft size={18} aria-hidden="true" />
        <Text size="sm" fw={600}>
          {children}
        </Text>
      </Group>
    </Anchor>
  );
}
