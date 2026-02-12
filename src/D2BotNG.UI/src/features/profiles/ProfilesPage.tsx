/**
 * ProfilesPage component
 *
 * Main page for displaying and managing profiles.
 * Includes bulk actions and a table of all profiles.
 */

import { useCallback, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Button,
  EmptyState,
  Card,
  LoadingSpinner,
  DeleteConfirmationDialog,
  Dropdown,
  type DropdownItem,
} from "@/components/ui";
import {
  useDeleteProfile,
  useProfileActions,
  useDeleteDialog,
  useIsLocalhost,
  useProfileTableColumns,
  PROFILE_COLUMNS,
} from "@/hooks";
import { useProfiles, useIsLoading } from "@/stores/event-store";
import type { Profile } from "@/generated/profiles_pb";
import { canStart, canStop, isActive } from "./profile-states";
import { ProfilesTable } from "./ProfilesTable";
import {
  PlayIcon,
  StopIcon,
  EyeIcon,
  EyeSlashIcon,
  PlusIcon,
  UserGroupIcon,
  ArrowPathIcon,
  ViewColumnsIcon,
} from "@heroicons/react/24/outline";

export function ProfilesPage() {
  const navigate = useNavigate();
  // Get profiles from event store - data arrives via event stream
  const isLoading = useIsLoading();
  const profilesData = useProfiles();
  const profiles = profilesData.map((p) => p.profile);
  const deleteProfile = useDeleteProfile();
  const actions = useProfileActions();
  const isLocalhost = useIsLocalhost();

  // Column visibility
  const { isColumnVisible, toggleColumn } = useProfileTableColumns();

  const visibleColumns = useMemo(
    () => PROFILE_COLUMNS.filter((col) => isColumnVisible(col.key)),
    [isColumnVisible],
  );

  const columnSelectorItems: DropdownItem[] = PROFILE_COLUMNS.map((col) => ({
    label: col.label,
    checked: isColumnVisible(col.key),
    onClick: () => toggleColumn(col.key),
  }));

  // Selected profiles for bulk actions
  const [selectedProfiles, setSelectedProfiles] = useState<Set<string>>(
    new Set(),
  );

  // Single-profile delete dialog
  const {
    deleteTarget,
    isOpen: isDeleteDialogOpen,
    requestDelete,
    confirmDelete,
    cancelDelete,
  } = useDeleteDialog<Profile, string[]>(deleteProfile, (name) => [name]);

  // Multi-profile delete state
  const [deleteNames, setDeleteNames] = useState<string[] | null>(null);

  const handleDeleteMultiple = useCallback((names: string[]) => {
    setDeleteNames(names);
  }, []);

  const confirmDeleteMultiple = useCallback(() => {
    if (deleteNames) {
      deleteProfile.mutate(deleteNames);
      setDeleteNames(null);
    }
  }, [deleteNames, deleteProfile]);

  const cancelDeleteMultiple = useCallback(() => {
    setDeleteNames(null);
  }, []);

  const handleNewProfile = useCallback(() => {
    navigate("/profiles/new");
  }, [navigate]);

  const handleClone = useCallback(
    (profile: Profile) => {
      // Navigate to new profile page with clone source
      navigate(`/profiles/new?clone=${encodeURIComponent(profile.name)}`);
    },
    [navigate],
  );

  const hasProfiles = profiles && profiles.length > 0;

  // Calculate profile state counts for bulk actions (mirrors backend state machine)
  // If profiles are selected, only include those; otherwise include all
  const { startableNames, stoppableNames, hiddenNames, visibleNames } =
    useMemo(() => {
      const startable: string[] = [];
      const stoppable: string[] = [];
      const hidden: string[] = [];
      const visible: string[] = [];

      const hasSelection = selectedProfiles.size > 0;

      for (const { profile, status } of profilesData) {
        // If we have a selection, only include selected profiles
        if (hasSelection && !selectedProfiles.has(profile.name)) continue;

        const state = status?.state;

        if (canStart(state)) startable.push(profile.name);
        if (canStop(state)) stoppable.push(profile.name);

        // Window visibility only matters when actively running
        if (isActive(state)) {
          if (status?.windowVisible) visible.push(profile.name);
          else hidden.push(profile.name);
        }
      }

      return {
        startableNames: startable,
        stoppableNames: stoppable,
        hiddenNames: hidden,
        visibleNames: visible,
      };
    }, [profilesData, selectedProfiles]);

  // Loading state - waiting for initial data from event stream
  if (isLoading) {
    return <LoadingSpinner fullPage />;
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-100">Profiles</h1>
          <p className="mt-1 text-sm text-zinc-400">
            Manage your bot profiles and monitor their status.
          </p>
        </div>

        {/* Bulk actions - operate on selected profiles, or all if none selected */}
        {hasProfiles && (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => actions.start.mutate(startableNames)}
              disabled={startableNames.length === 0 || actions.start.isPending}
            >
              {actions.start.isPending ? (
                <ArrowPathIcon className="h-4 w-4 animate-spin" />
              ) : (
                <PlayIcon className="h-4 w-4" />
              )}
              {selectedProfiles.size > 0
                ? `Start (${startableNames.length})`
                : "Start All"}
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => actions.stop.mutate(stoppableNames)}
              disabled={stoppableNames.length === 0 || actions.stop.isPending}
            >
              {actions.stop.isPending ? (
                <ArrowPathIcon className="h-4 w-4 animate-spin" />
              ) : (
                <StopIcon className="h-4 w-4" />
              )}
              {selectedProfiles.size > 0
                ? `Stop (${stoppableNames.length})`
                : "Stop All"}
            </Button>
            {isLocalhost && (
              <>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => actions.showWindow.mutate(hiddenNames)}
                  disabled={
                    hiddenNames.length === 0 || actions.showWindow.isPending
                  }
                >
                  {actions.showWindow.isPending ? (
                    <ArrowPathIcon className="h-4 w-4 animate-spin" />
                  ) : (
                    <EyeIcon className="h-4 w-4" />
                  )}
                  {selectedProfiles.size > 0
                    ? `Show (${hiddenNames.length})`
                    : "Show All"}
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => actions.hideWindow.mutate(visibleNames)}
                  disabled={
                    visibleNames.length === 0 || actions.hideWindow.isPending
                  }
                >
                  {actions.hideWindow.isPending ? (
                    <ArrowPathIcon className="h-4 w-4 animate-spin" />
                  ) : (
                    <EyeSlashIcon className="h-4 w-4" />
                  )}
                  {selectedProfiles.size > 0
                    ? `Hide (${visibleNames.length})`
                    : "Hide All"}
                </Button>
              </>
            )}
          </div>
        )}
      </div>

      {/* Action bar */}
      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={handleNewProfile}>
          <PlusIcon className="h-4 w-4" />
          New Profile
        </Button>
        {hasProfiles && (
          <Dropdown
            items={columnSelectorItems}
            trigger={<ViewColumnsIcon className="h-5 w-5" aria-hidden="true" />}
          />
        )}
      </div>

      {/* Content */}
      {hasProfiles ? (
        <Card className="overflow-hidden">
          <ProfilesTable
            profiles={profiles}
            visibleColumns={visibleColumns}
            selectedProfiles={selectedProfiles}
            onSelectionChange={setSelectedProfiles}
            onClone={handleClone}
            onDelete={requestDelete}
            onDeleteMultiple={handleDeleteMultiple}
          />
        </Card>
      ) : (
        <EmptyState
          icon={UserGroupIcon}
          title="No profiles yet"
          description="Create your first profile to start botting."
          action={
            <Button onClick={handleNewProfile}>
              <PlusIcon className="h-4 w-4" />
              Create Profile
            </Button>
          }
        />
      )}

      {/* Single delete confirmation dialog */}
      <DeleteConfirmationDialog
        open={isDeleteDialogOpen}
        entityType="Profile"
        entityName={deleteTarget?.name ?? ""}
        warningMessage="All profile data including statistics will be permanently deleted."
        isPending={deleteProfile.isPending}
        onConfirm={confirmDelete}
        onCancel={cancelDelete}
      />

      {/* Multi-delete confirmation dialog */}
      <DeleteConfirmationDialog
        open={deleteNames !== null}
        entityType="Profiles"
        entityName={`${deleteNames?.length ?? 0} profiles`}
        warningMessage="All profile data including statistics will be permanently deleted for all selected profiles."
        isPending={deleteProfile.isPending}
        onConfirm={confirmDeleteMultiple}
        onCancel={cancelDeleteMultiple}
      />
    </div>
  );
}
