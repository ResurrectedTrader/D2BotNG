/**
 * ProfileActions component
 *
 * Dropdown menu with profile actions like start/stop, show/hide window, etc.
 */

import { useNavigate } from "react-router-dom";
import { Dropdown, type DropdownItem } from "@/components/ui";
import { useProfileActions, useIsLocalhost } from "@/hooks";
import { ProfileState } from "@/generated/common_pb";
import type { Profile, ProfileStatus } from "@/generated/profiles_pb";
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
  status?: ProfileStatus;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
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

  const isRunning =
    status?.state === ProfileState.STARTING ||
    status?.state === ProfileState.RUNNING ||
    status?.state === ProfileState.BUSY;
  const isStopped = !status || status.state === ProfileState.STOPPED;
  const windowVisible = status?.windowVisible ?? false;
  const scheduleEnabled = profile.scheduleEnabled;

  return [
    // Edit profile
    {
      label: "Edit",
      icon: PencilSquareIcon,
      onClick: () => navigate(`/profiles/${encodeURIComponent(profile.name)}`),
    },
    // Start/Stop toggle
    isStopped
      ? {
          label: "Start",
          icon: PlayIcon,
          onClick: () => actions.start.mutate([profile.name]),
        }
      : {
          label: "Stop",
          icon: StopIcon,
          onClick: () => actions.stop.mutate([profile.name]),
        },
    // Show/Hide Window toggle (only on localhost, only enabled when running)
    ...(isLocalhost
      ? [
          windowVisible
            ? {
                label: "Hide Window",
                icon: EyeSlashIcon,
                onClick: () => actions.hideWindow.mutate([profile.name]),
                disabled: isStopped,
              }
            : {
                label: "Show Window",
                icon: EyeIcon,
                onClick: () => actions.showWindow.mutate([profile.name]),
                disabled: isStopped,
              },
        ]
      : []),
    // Clone
    {
      label: "Clone",
      icon: DocumentDuplicateIcon,
      onClick: () => onClone(profile),
    },
    // Reset Stats
    {
      label: "Reset Stats",
      icon: ArrowPathIcon,
      onClick: () => actions.resetStats.mutate(profile.name),
    },
    // Trigger Mule (disabled if not running)
    {
      label: "Trigger Mule",
      icon: CubeIcon,
      onClick: () => actions.triggerMule.mutate(profile.name),
      disabled: !isRunning,
    },
    // Rotate Key (only when stopped)
    {
      label: "Rotate Key",
      icon: KeyIcon,
      onClick: () => actions.rotateKey.mutate(profile.name),
      disabled: !isStopped,
    },
    // Release Key (only when a key is assigned)
    {
      label: "Release Key",
      icon: LockOpenIcon,
      onClick: () => actions.releaseKey.mutate(profile.name),
      disabled: !status?.currentKey,
    },
    // Enable/Disable Schedule toggle
    scheduleEnabled
      ? {
          label: "Disable Schedule",
          icon: CalendarIcon,
          onClick: () =>
            actions.setScheduleEnabled.mutate({
              profileName: profile.name,
              enabled: false,
            }),
        }
      : {
          label: "Enable Schedule",
          icon: CalendarDaysIcon,
          onClick: () =>
            actions.setScheduleEnabled.mutate({
              profileName: profile.name,
              enabled: true,
            }),
        },
    // Delete (danger, disabled when not fully stopped)
    {
      label: "Delete",
      icon: TrashIcon,
      onClick: () => onDelete(profile),
      danger: true,
      disabled: !isStopped,
    },
  ];
}

export function ProfileActions(props: ProfileActionsProps) {
  const items = useProfileActionItems(props);
  return <Dropdown items={items} />;
}
