/**
 * ProfileActions component
 *
 * Dropdown menu with profile actions like start/stop, show/hide window, etc.
 * Also exports pure builder functions for use in context menus.
 */

import { useNavigate } from "react-router-dom";
import { Dropdown, type DropdownItem } from "@/components/ui";
import { useProfileActions, useIsLocalhost } from "@/hooks";
import { RunState } from "@/generated/common_pb";
import type { Profile, ProfileState } from "@/generated/profiles_pb";
import { canStart, canStop, isActive } from "./profile-states";
import {
  PlayIcon,
  StopIcon,
  EyeIcon,
  EyeSlashIcon,
  DocumentDuplicateIcon,
  ArrowPathIcon,
  CubeIcon,
  KeyIcon,
  LockOpenIcon,
  CalendarIcon,
  CalendarDaysIcon,
  TrashIcon,
  PencilSquareIcon,
} from "@heroicons/react/24/outline";

export interface ProfileActionsProps {
  profile: Profile;
  status?: ProfileState;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
}

export interface BuildSingleParams {
  profile: Profile;
  status?: ProfileState;
  isLocalhost: boolean;
  actions: ReturnType<typeof useProfileActions>;
  onEdit: (profile: Profile) => void;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
}

export interface BuildMultiParams {
  profiles: Profile[];
  statuses: Record<string, ProfileState | undefined>;
  isLocalhost: boolean;
  actions: ReturnType<typeof useProfileActions>;
  onDelete: (names: string[]) => void;
}

/** Pure builder for single-profile context menu items */
export function buildSingleProfileActionItems({
  profile,
  status,
  isLocalhost,
  actions,
  onEdit,
  onClone,
  onDelete,
}: BuildSingleParams): DropdownItem[] {
  const state = status?.state;
  const windowVisible = status?.windowVisible ?? false;
  const scheduleEnabled = profile.scheduleEnabled;

  const isStopped = state === RunState.STOPPED || state === undefined;

  return [
    {
      label: "Edit",
      icon: PencilSquareIcon,
      onClick: () => onEdit(profile),
    },
    // Show Start/Stop based on state machine — Error state gets both
    ...(canStart(state)
      ? [
          {
            label: "Start",
            icon: PlayIcon,
            onClick: () => actions.start.mutate([profile.name]),
          },
        ]
      : []),
    ...(canStop(state)
      ? [
          {
            label: "Stop",
            icon: StopIcon,
            onClick: () => actions.stop.mutate([profile.name]),
          },
        ]
      : []),
    // Show/Hide Window — only when profile is active (has a game process)
    ...(isLocalhost && isActive(state)
      ? [
          windowVisible
            ? {
                label: "Hide Window",
                icon: EyeSlashIcon,
                onClick: () => actions.hideWindow.mutate([profile.name]),
              }
            : {
                label: "Show Window",
                icon: EyeIcon,
                onClick: () => actions.showWindow.mutate([profile.name]),
              },
        ]
      : []),
    {
      label: "Clone",
      icon: DocumentDuplicateIcon,
      onClick: () => onClone(profile),
    },
    {
      label: "Reset Stats",
      icon: ArrowPathIcon,
      onClick: () => actions.resetStats.mutate([profile.name]),
    },
    // Trigger Mule — only when running
    ...(state === RunState.RUNNING
      ? [
          {
            label: "Trigger Mule",
            icon: CubeIcon,
            onClick: () => actions.triggerMule.mutate([profile.name]),
          },
        ]
      : []),
    // Rotate Key — only when holding a key (swaps to next available)
    ...(status?.keyName
      ? [
          {
            label: "Rotate Key",
            icon: KeyIcon,
            onClick: () => actions.rotateKey.mutate([profile.name]),
          },
        ]
      : []),
    // Release Key — only when holding a key
    ...(status?.keyName
      ? [
          {
            label: "Release Key",
            icon: LockOpenIcon,
            onClick: () => actions.releaseKey.mutate([profile.name]),
          },
        ]
      : []),
    scheduleEnabled
      ? {
          label: "Disable Schedule",
          icon: CalendarIcon,
          onClick: () => actions.disableSchedule.mutate([profile.name]),
        }
      : {
          label: "Enable Schedule",
          icon: CalendarDaysIcon,
          onClick: () => actions.enableSchedule.mutate([profile.name]),
        },
    // Delete — only when stopped
    ...(isStopped
      ? [
          {
            label: "Delete",
            icon: TrashIcon,
            onClick: () => onDelete(profile),
            danger: true,
          },
        ]
      : []),
  ];
}

/** Pure builder for multi-profile context menu items */
export function buildMultiProfileActionItems({
  profiles,
  statuses,
  isLocalhost,
  actions,
  onDelete,
}: BuildMultiParams): DropdownItem[] {
  const names = profiles.map((p) => p.name);
  const count = names.length;

  const startableNames: string[] = [];
  const stoppableNames: string[] = [];
  const showableNames: string[] = []; // active + window hidden
  const hidableNames: string[] = []; // active + window visible
  const stoppedNames: string[] = []; // for delete
  const keyHolderNames: string[] = []; // for release key
  const hasScheduleEnabled = profiles.some((p) => p.scheduleEnabled);
  const hasScheduleDisabled = profiles.some((p) => !p.scheduleEnabled);

  for (const profile of profiles) {
    const status = statuses[profile.name];
    const state = status?.state;
    if (canStart(state)) startableNames.push(profile.name);
    if (canStop(state)) stoppableNames.push(profile.name);
    if (isActive(state)) {
      if (status?.windowVisible) hidableNames.push(profile.name);
      else showableNames.push(profile.name);
    }
    if (state === RunState.STOPPED || state === undefined)
      stoppedNames.push(profile.name);
    if (status?.keyName) keyHolderNames.push(profile.name);
  }

  return [
    ...(startableNames.length > 0
      ? [
          {
            label: `Start (${startableNames.length})`,
            icon: PlayIcon,
            onClick: () => actions.start.mutate(startableNames),
          },
        ]
      : []),
    ...(stoppableNames.length > 0
      ? [
          {
            label: `Stop (${stoppableNames.length})`,
            icon: StopIcon,
            onClick: () => actions.stop.mutate(stoppableNames),
          },
        ]
      : []),
    ...(isLocalhost && showableNames.length > 0
      ? [
          {
            label: `Show Window (${showableNames.length})`,
            icon: EyeIcon,
            onClick: () => actions.showWindow.mutate(showableNames),
          },
        ]
      : []),
    ...(isLocalhost && hidableNames.length > 0
      ? [
          {
            label: `Hide Window (${hidableNames.length})`,
            icon: EyeSlashIcon,
            onClick: () => actions.hideWindow.mutate(hidableNames),
          },
        ]
      : []),
    {
      label: `Reset Stats (${count})`,
      icon: ArrowPathIcon,
      onClick: () => actions.resetStats.mutate(names),
    },
    ...(keyHolderNames.length > 0
      ? [
          {
            label: `Rotate Key (${keyHolderNames.length})`,
            icon: KeyIcon,
            onClick: () => actions.rotateKey.mutate(keyHolderNames),
          },
        ]
      : []),
    ...(keyHolderNames.length > 0
      ? [
          {
            label: `Release Key (${keyHolderNames.length})`,
            icon: LockOpenIcon,
            onClick: () => actions.releaseKey.mutate(keyHolderNames),
          },
        ]
      : []),
    ...(hasScheduleDisabled
      ? [
          {
            label: `Enable Schedule (${profiles.filter((p) => !p.scheduleEnabled).length})`,
            icon: CalendarDaysIcon,
            onClick: () =>
              actions.enableSchedule.mutate(
                profiles.filter((p) => !p.scheduleEnabled).map((p) => p.name),
              ),
          },
        ]
      : []),
    ...(hasScheduleEnabled
      ? [
          {
            label: `Disable Schedule (${profiles.filter((p) => p.scheduleEnabled).length})`,
            icon: CalendarIcon,
            onClick: () =>
              actions.disableSchedule.mutate(
                profiles.filter((p) => p.scheduleEnabled).map((p) => p.name),
              ),
          },
        ]
      : []),
    ...(stoppedNames.length > 0
      ? [
          {
            label: `Delete (${stoppedNames.length})`,
            icon: TrashIcon,
            onClick: () => onDelete(stoppedNames),
            danger: true,
          },
        ]
      : []),
  ];
}

export function useProfileActionItems({
  profile,
  status,
  onClone,
  onDelete,
}: ProfileActionsProps): DropdownItem[] {
  const navigate = useNavigate();
  const actions = useProfileActions();
  const isLocalhost = useIsLocalhost();

  return buildSingleProfileActionItems({
    profile,
    status,
    isLocalhost,
    actions,
    onEdit: (p) => navigate(`/profiles/${encodeURIComponent(p.name)}`),
    onClone,
    onDelete,
  });
}

export function ProfileActions(props: ProfileActionsProps) {
  const items = useProfileActionItems(props);
  return <Dropdown items={items} />;
}
