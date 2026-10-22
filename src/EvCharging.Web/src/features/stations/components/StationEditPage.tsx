import { FailureAlert, StatusAlert } from "@/components/StatusAlert";
import { useState, type ReactNode } from "react";
import {
  Anchor,
  Button,
  Card,
  Group,
  Stack,
  Text,
  Textarea,
  TextInput,
} from "@mantine/core";
import { isNotEmpty, useForm, type FormErrors } from "@mantine/form";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { HttpStatusCode } from "axios";
import { Link, useParams } from "react-router";
import { BackLink } from "@/components/BackLink";
import { PageHeading } from "@/components/PageHeading";
import { useUserSession } from "@/features/auth/useUserSession";
import { getHttpStatus, getFormValidationErrors } from "@/lib/http-errors";
import {
  getStationEditFailure,
  getStationEditorLoadFailure,
  type StationEditFailure,
  type StationEditorLoadFailure,
} from "../errors";
import {
  stationEditorQuery,
  stationKeys,
  updateStation,
  type StationEditor,
  type StationEdit,
  type StationDetail,
} from "../api";
import { StationLoading } from "./StationPresentation";

export function StationEditPage() {
  const { stationId = "" } = useParams();
  const query = useQuery(stationEditorQuery(stationId));
  if (query.isPending || !query.isFetchedAfterMount) return <StationLoading />;
  const loadFailure = query.isError
    ? getStationEditorLoadFailure(query.error)
    : null;
  if (query.isLoadingError && loadFailure)
    return (
      <Stack gap="xs">
        <BackLink to="/operator">Assigned stations</BackLink>
        <StationEditFailureNotice
          failure={loadFailure}
          reloading={query.isFetching}
          reload={() => void query.refetch()}
        />
      </Stack>
    );
  if (!query.data) return <StationLoading />;
  return (
    <StationEditForm
      key={stationId}
      initial={query.data}
      reload={() => query.refetch()}
      reloading={query.isFetching}
      loadFailure={loadFailure}
    />
  );
}

function StationEditForm({
  initial,
  reload,
  reloading,
  loadFailure,
}: {
  initial: StationEditor;
  reload: () => Promise<{ data?: StationEditor; isSuccess: boolean }>;
  reloading: boolean;
  loadFailure: StationEditorLoadFailure | null;
}) {
  const client = useQueryClient();
  const session = useUserSession();
  const [confirmed, setConfirmed] = useState(initial);
  const form = useForm<StationEdit>({
    mode: "uncontrolled",
    initialValues: toEdit(initial),
    validate: {
      name: (value) =>
        isNotEmpty("Enter a station name.")(value) ??
        (value.length > 200 ? "Use 200 characters or fewer." : null),
      address: (value) =>
        isNotEmpty("Enter an address.")(value) ??
        (value.length > 500 ? "Use 500 characters or fewer." : null),
      accessInstructions: (value) =>
        value.length > 4000 ? "Use 4000 characters or fewer." : null,
    },
  });
  const focusFirstError = (errors: FormErrors) => {
    const firstInvalid = ["name", "address", "accessInstructions"].find(
      (field) => errors[field],
    );
    if (firstInvalid) form.getInputNode(firstInvalid)?.focus();
  };
  const mutation = useMutation({
    mutationFn: (edit: StationEdit) =>
      updateStation(initial.id, edit, session.antiforgeryToken ?? ""),
    retry: false,
    onSuccess: (saved) => {
      setConfirmed(saved);
      form.setInitialValues(toEdit(saved));
      form.reset();
      client.setQueryData(stationKeys.editor(saved.id), saved);
      client.setQueryData(
        stationKeys.detail(saved.id),
        (previous: StationDetail | undefined) =>
          previous
            ? {
                ...previous,
                name: saved.name,
                address: saved.address,
                accessInstructions: saved.accessInstructions,
              }
            : previous,
      );
      void client.invalidateQueries({ queryKey: stationKeys.lists });
      void client.invalidateQueries({ queryKey: stationKeys.assignedLists });
    },
    onError: (error) => {
      if (getHttpStatus(error) === HttpStatusCode.BadRequest) {
        const errors = getFormValidationErrors(error);
        if (errors) {
          form.setErrors(errors);
        }
      }
    },
  });
  const failure = mutation.isError
    ? getStationEditFailure(mutation.error)
    : null;
  const visibleFailure = loadFailure ?? failure;
  const saveDisabled =
    !session.antiforgeryToken || reloading || loadFailure?.blocksSave === true;
  const loadCurrent = async () => {
    const result = await reload();
    if (result.isSuccess && result.data) {
      form.setInitialValues(toEdit(result.data));
      form.reset();
      setConfirmed(result.data);
      mutation.reset();
    }
  };
  return (
    <Stack gap="xs">
      <BackLink to="/operator">Assigned stations</BackLink>
      <Stack gap="xl">
        <PageHeading
          title="Edit station"
          description={`Update the information drivers see for ${confirmed.name}.`}
        />
        <Card withBorder padding="lg" maw={760} w="100%">
          <form
            noValidate
            onSubmit={form.onSubmit((values) => {
              if (!saveDisabled && !mutation.isPending) mutation.mutate(values);
            }, focusFirstError)}
          >
            <Stack>
              <TextInput
                key={form.key("name")}
                label="Station name"
                withAsterisk
                aria-required="true"
                disabled={mutation.isPending || reloading}
                maxLength={200}
                {...form.getInputProps("name")}
              />
              <TextInput
                key={form.key("address")}
                label="Address"
                withAsterisk
                aria-required="true"
                disabled={mutation.isPending || reloading}
                maxLength={500}
                {...form.getInputProps("address")}
              />
              <Textarea
                key={form.key("accessInstructions")}
                label="Access instructions"
                disabled={mutation.isPending || reloading}
                autosize
                minRows={4}
                maxLength={4000}
                {...form.getInputProps("accessInstructions")}
              />
              {visibleFailure && (
                <StationEditFailureNotice
                  failure={visibleFailure}
                  reloading={reloading}
                  reload={() => void loadCurrent()}
                >
                  {loadFailure && (
                    <Text size="sm">Your draft has been retained.</Text>
                  )}
                </StationEditFailureNotice>
              )}
              {mutation.isSuccess && !form.isDirty() && (
                <StatusAlert tone="success">Changes saved.</StatusAlert>
              )}
              <Group>
                <Button
                  type="submit"
                  loading={mutation.isPending}
                  disabled={saveDisabled}
                >
                  Save changes
                </Button>
                <Anchor component={Link} to={`/stations/${initial.id}`}>
                  View station
                </Anchor>
              </Group>
            </Stack>
          </form>
        </Card>
      </Stack>
    </Stack>
  );
}

function StationEditFailureNotice({
  failure,
  reloading,
  reload,
  children,
}: {
  failure: StationEditFailure;
  reloading: boolean;
  reload: () => void;
  children?: ReactNode;
}) {
  return (
    <FailureAlert failure={failure}>
      {children}
      {failure.recovery === "reload" && (
        <Button
          variant="white"
          size="sm"
          w="fit-content"
          loading={reloading}
          onClick={reload}
        >
          Reload current station
        </Button>
      )}
    </FailureAlert>
  );
}

function toEdit(station: StationEditor): StationEdit {
  return {
    version: station.version,
    name: station.name,
    address: station.address,
    accessInstructions: station.accessInstructions,
  };
}
