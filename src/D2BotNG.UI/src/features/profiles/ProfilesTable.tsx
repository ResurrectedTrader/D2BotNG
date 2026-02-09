/**
 * ProfilesTable component
 *
 * Displays a table of profiles with status and actions.
 * Supports grouping profiles with expandable group headers.
 * Supports drag-and-drop reordering within and across groups.
 */

import { useState, useMemo, Fragment } from "react";
import { useNavigate } from "react-router-dom";
import clsx from "clsx";
import {
  DndContext,
  DragOverlay,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragStartEvent,
  type DragEndEvent,
  type DragOverEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableHeader,
  TableCell,
  Button,
  Dropdown,
  type DropdownItem,
  useContextMenu,
} from "@/components/ui";
import { useAllProfileStates } from "@/stores/event-store";
import {
  useProfileActions,
  useProfileTableColumns,
  PROFILE_COLUMNS,
} from "@/hooks";
import type { ProfileColumnKey } from "@/hooks";
import { RunState } from "@/generated/common_pb";
import type { Profile, ProfileState } from "@/generated/profiles_pb";
import { ProfileStatusBadge } from "./ProfileStatusBadge";
import { useProfileActionItems } from "./ProfileActions";
import {
  PlayIcon,
  StopIcon,
  EyeIcon,
  EyeSlashIcon,
  ChevronDownIcon,
  ViewColumnsIcon,
  Bars2Icon,
  ArrowPathIcon,
} from "@heroicons/react/24/outline";

export interface ProfilesTableProps {
  profiles: Profile[];
  selectedProfiles: Set<string>;
  onSelectionChange: (selected: Set<string>) => void;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
}

interface ProfileGroup {
  name: string;
  profiles: Profile[];
}

// Breakpoint classes for progressive column hiding (right to left)
// Rightmost columns hide first as viewport shrinks
const COLUMN_BREAKPOINT_CLASSES = [
  "hidden sm:table-cell", // index 0: show at sm (640px+) - stays visible longest
  "hidden md:table-cell", // index 1: show at md (768px+)
  "hidden lg:table-cell", // index 2: show at lg (1024px+)
  "hidden xl:table-cell", // index 3: show at xl (1280px+)
  "hidden 2xl:table-cell", // index 4+: show at 2xl (1536px+) - hides first
];

// Matching breakpoint classes for inline-flex elements (e.g., buttons)
const INLINE_BREAKPOINT_CLASSES = [
  "hidden sm:inline-flex",
  "hidden md:inline-flex",
  "hidden lg:inline-flex",
  "hidden xl:inline-flex",
  "hidden 2xl:inline-flex",
];

function getColumnBreakpointClass(index: number): string {
  const breakpointIndex = Math.min(index, COLUMN_BREAKPOINT_CLASSES.length - 1);
  return COLUMN_BREAKPOINT_CLASSES[breakpointIndex];
}

// Get breakpoint class for elements that should hide when columns start hiding
function getColumnAlignedInlineClass(columnCount: number): string {
  if (columnCount <= 0) return INLINE_BREAKPOINT_CLASSES[0];
  const breakpointIndex = Math.min(
    columnCount - 1,
    INLINE_BREAKPOINT_CLASSES.length - 1,
  );
  return INLINE_BREAKPOINT_CLASSES[breakpointIndex];
}

function getColumnValue(
  key: ProfileColumnKey,
  profile: Profile,
  status?: ProfileState,
): string | number {
  switch (key) {
    case "runs":
      return profile.runs;
    case "chickens":
      return profile.chickens;
    case "deaths":
      return profile.deaths;
    case "crashes":
      return profile.crashes;
    case "restarts":
      return profile.restarts;
    case "key":
      return status?.keyName || "-";
    case "gamePath": {
      if (!profile.d2Path) return "-";
      // Extract last path component (filename)
      const parts = profile.d2Path.replace(/\\/g, "/").split("/");
      return parts[parts.length - 1] || "-";
    }
    default:
      return "-";
  }
}

// Prefix for group drop zone IDs
const GROUP_DROP_PREFIX = "group:";
const UNGROUPED_DROP_ID = "group:";

interface SortableProfileRowProps {
  profile: Profile;
  status?: ProfileState;
  visibleColumns: { key: ProfileColumnKey; label: string }[];
  actions: ReturnType<typeof useProfileActions>;
  isSelected: boolean;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
  onSelect: (name: string, ctrlKey: boolean) => void;
  onNavigate: (name: string) => void;
  isDragOverlay?: boolean;
}

function SortableProfileRow({
  profile,
  status,
  visibleColumns,
  actions,
  isSelected,
  onClone,
  onDelete,
  onSelect,
  onNavigate,
  isDragOverlay,
}: SortableProfileRowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: profile.name });

  const actionItems = useProfileActionItems({
    profile,
    status,
    onClone,
    onDelete,
  });
  const { contextMenu, onContextMenu } = useContextMenu(actionItems);

  // Only apply transform to the dragging item to prevent other items from shifting
  const style = isDragging
    ? { transform: CSS.Transform.toString(transform), transition }
    : {};
  const state = status?.state ?? RunState.STOPPED;
  const isRunning =
    state === RunState.RUNNING;
  const isStopped = state === RunState.STOPPED;
  const windowVisible = status?.windowVisible ?? false;

  const titleParts = [profile.account, profile.character].filter(Boolean);
  const title = titleParts.length > 0 ? titleParts.join(" / ") : undefined;

  return (
    <tr
      ref={setNodeRef}
      style={style}
      className={clsx(
        "border-b border-zinc-800 cursor-pointer hover:bg-zinc-800/50 transition-colors",
        isDragging && "opacity-50 bg-zinc-800",
        isDragOverlay && "bg-zinc-900 shadow-lg border border-zinc-700",
        isSelected && "bg-blue-900/30",
      )}
      onClick={(e) =>
        !isDragging && onSelect(profile.name, e.ctrlKey || e.metaKey)
      }
      onDoubleClick={() => !isDragging && onNavigate(profile.name)}
      onContextMenu={(e) => {
        if (!isDragging) onContextMenu(e);
      }}
    >
      <TableCell className="w-px whitespace-nowrap">
        <div className="flex items-center gap-2">
          <button
            className="cursor-grab touch-none p-1 -m-1 text-zinc-500 hover:text-zinc-300"
            {...attributes}
            {...listeners}
            onClick={(e) => e.stopPropagation()}
          >
            <Bars2Icon className="h-4 w-4" />
          </button>
          <span
            className={clsx(
              "h-2 w-2 rounded-full flex-shrink-0",
              state === RunState.RUNNING && "bg-green-500",
              (state === RunState.STARTING ||
                state === RunState.STOPPING) &&
                "bg-amber-500",
              state === RunState.STOPPED && "bg-zinc-500",
              state === RunState.ERROR && "bg-red-500",
            )}
            title={RunState[state]}
          />
          <span className="font-medium text-zinc-100" title={title}>
            {profile.name}
          </span>
        </div>
      </TableCell>
      <TableCell className="w-full max-w-0">
        <ProfileStatusBadge state={state} status={status?.status} />
      </TableCell>
      {visibleColumns.map((col, index) => (
        <TableCell
          key={col.key}
          className={`${getColumnBreakpointClass(index)} w-px whitespace-nowrap text-zinc-400 text-right`}
        >
          {getColumnValue(col.key, profile, status)}
        </TableCell>
      ))}
      <TableCell
        className="w-px whitespace-nowrap"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-end gap-1">
          {isStopped ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => actions.start.mutate([profile.name])}
              disabled={actions.start.isPending}
              title="Start profile"
            >
              {actions.start.isPending ? (
                <ArrowPathIcon className="h-4 w-4 animate-spin" />
              ) : (
                <PlayIcon className="h-4 w-4" />
              )}
            </Button>
          ) : (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => actions.stop.mutate([profile.name])}
              disabled={actions.stop.isPending}
              title="Stop profile"
            >
              {actions.stop.isPending ? (
                <ArrowPathIcon className="h-4 w-4 animate-spin" />
              ) : (
                <StopIcon className="h-4 w-4" />
              )}
            </Button>
          )}

          {/* Show/hide window - hidden when columns start hiding, available in context menu */}
          <span className={getColumnAlignedInlineClass(visibleColumns.length)}>
            {isRunning &&
              (windowVisible ? (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => actions.hideWindow.mutate([profile.name])}
                  disabled={actions.hideWindow.isPending}
                  title="Hide window"
                >
                  {actions.hideWindow.isPending ? (
                    <ArrowPathIcon className="h-4 w-4 animate-spin" />
                  ) : (
                    <EyeSlashIcon className="h-4 w-4" />
                  )}
                </Button>
              ) : (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => actions.showWindow.mutate([profile.name])}
                  disabled={actions.showWindow.isPending}
                  title="Show window"
                >
                  {actions.showWindow.isPending ? (
                    <ArrowPathIcon className="h-4 w-4 animate-spin" />
                  ) : (
                    <EyeIcon className="h-4 w-4" />
                  )}
                </Button>
              ))}
          </span>

          <Dropdown items={actionItems} />
        </div>
      </TableCell>
      {contextMenu}
    </tr>
  );
}

export function ProfilesTable({
  profiles,
  selectedProfiles,
  onSelectionChange,
  onClone,
  onDelete,
}: ProfilesTableProps) {
  const navigate = useNavigate();
  const statuses = useAllProfileStates();
  const actions = useProfileActions();
  const { isColumnVisible, toggleColumn } = useProfileTableColumns();

  const [activeId, setActiveId] = useState<string | null>(null);
  const [overGroupId, setOverGroupId] = useState<string | null>(null);

  const handleSelect = (name: string, ctrlKey: boolean) => {
    const newSelected = new Set(selectedProfiles);
    if (ctrlKey) {
      // Ctrl+Click: toggle individual selection
      if (newSelected.has(name)) {
        newSelected.delete(name);
      } else {
        newSelected.add(name);
      }
    } else {
      // Single click: toggle selection, or select if not selected
      if (newSelected.has(name) && newSelected.size === 1) {
        // Clicking the only selected item deselects it
        newSelected.clear();
      } else {
        // Select only this item
        newSelected.clear();
        newSelected.add(name);
      }
    }
    onSelectionChange(newSelected);
  };

  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 8,
      },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  // Get visible columns in order
  const visibleColumns = useMemo(
    () => PROFILE_COLUMNS.filter((col) => isColumnVisible(col.key)),
    [isColumnVisible],
  );

  // Column selector dropdown items
  const columnSelectorItems: DropdownItem[] = PROFILE_COLUMNS.map((col) => ({
    label: col.label,
    checked: isColumnVisible(col.key),
    onClick: () => toggleColumn(col.key),
  }));

  // Separate ungrouped profiles from grouped ones
  const { ungroupedProfiles, groupedProfiles } = useMemo(() => {
    const ungrouped: Profile[] = [];
    const groups = new Map<string, Profile[]>();

    for (const profile of profiles) {
      const groupName = profile.group || "";
      if (groupName === "") {
        ungrouped.push(profile);
      } else {
        const existing = groups.get(groupName) || [];
        existing.push(profile);
        groups.set(groupName, existing);
      }
    }

    // Sort groups alphabetically
    const sortedGroups = Array.from(groups.entries()).sort((a, b) =>
      a[0].localeCompare(b[0]),
    );

    return {
      ungroupedProfiles: ungrouped,
      groupedProfiles: sortedGroups.map(([name, profs]) => ({
        name,
        profiles: profs,
      })),
    };
  }, [profiles]);

  // All profile IDs for sortable context - must match visual render order
  const allProfileIds = useMemo(() => {
    const ids: string[] = [];
    // Ungrouped profiles first (matches render order)
    for (const profile of ungroupedProfiles) {
      ids.push(profile.name);
    }
    // Then grouped profiles in group order (matches render order)
    for (const group of groupedProfiles) {
      for (const profile of group.profiles) {
        ids.push(profile.name);
      }
    }
    return ids;
  }, [ungroupedProfiles, groupedProfiles]);

  // Track expanded state for each group (all expanded by default)
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() => {
    return new Set(groupedProfiles.map((g) => g.name));
  });

  // Update expanded groups when new groups appear
  useMemo(() => {
    setExpandedGroups((prev) => {
      const newSet = new Set(prev);
      for (const group of groupedProfiles) {
        if (!prev.has(group.name)) {
          newSet.add(group.name);
        }
      }
      return newSet;
    });
  }, [groupedProfiles]);

  const toggleGroup = (groupName: string) => {
    setExpandedGroups((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(groupName)) {
        newSet.delete(groupName);
      } else {
        newSet.add(groupName);
      }
      return newSet;
    });
  };

  const handleNavigate = (name: string) => {
    navigate(`/profiles/${encodeURIComponent(name)}`);
  };

  const handleDragStart = (event: DragStartEvent) => {
    setActiveId(event.active.id as string);
  };

  const handleDragOver = (event: DragOverEvent) => {
    const { over } = event;
    if (!over) {
      setOverGroupId(null);
      return;
    }

    // Check if over a group header
    const overId = over.id as string;
    if (overId.startsWith(GROUP_DROP_PREFIX)) {
      setOverGroupId(overId);
    } else {
      // Over a profile - find its group
      const overProfile = profiles.find((p) => p.name === overId);
      if (overProfile) {
        setOverGroupId(
          overProfile.group
            ? `${GROUP_DROP_PREFIX}${overProfile.group}`
            : UNGROUPED_DROP_ID,
        );
      }
    }
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveId(null);
    setOverGroupId(null);

    if (!over) return;

    const activeId = active.id as string;
    const overId = over.id as string;

    if (activeId === overId) return;

    const activeProfile = profiles.find((p) => p.name === activeId);
    if (!activeProfile) return;

    let targetGroup: string;
    let targetIndex: number;

    if (overId.startsWith(GROUP_DROP_PREFIX)) {
      // Dropped on a group header - move to end of that group
      targetGroup = overId.slice(GROUP_DROP_PREFIX.length);
      const groupProfiles =
        targetGroup === ""
          ? ungroupedProfiles
          : (groupedProfiles.find((g) => g.name === targetGroup)?.profiles ??
            []);
      targetIndex = groupProfiles.filter((p) => p.name !== activeId).length;
    } else {
      // Dropped on a profile - insert at that position
      const overProfile = profiles.find((p) => p.name === overId);
      if (!overProfile) return;

      targetGroup = overProfile.group || "";
      const groupProfiles =
        targetGroup === ""
          ? ungroupedProfiles
          : (groupedProfiles.find((g) => g.name === targetGroup)?.profiles ??
            []);

      // Find index of the profile we're dropping on (excluding the active one)
      const filteredProfiles = groupProfiles.filter((p) => p.name !== activeId);
      targetIndex = filteredProfiles.findIndex((p) => p.name === overId);
      if (targetIndex === -1) targetIndex = filteredProfiles.length;
    }

    // Only call API if something changed
    const currentGroup = activeProfile.group || "";
    const currentIndex =
      currentGroup === ""
        ? ungroupedProfiles.findIndex((p) => p.name === activeId)
        : (groupedProfiles
            .find((g) => g.name === currentGroup)
            ?.profiles.findIndex((p) => p.name === activeId) ?? -1);

    if (currentGroup === targetGroup && currentIndex === targetIndex) return;

    actions.reorder.mutate({
      profileName: activeId,
      newIndex: targetIndex,
      newGroup: targetGroup !== currentGroup ? targetGroup : undefined,
    });
  };

  const handleDragCancel = () => {
    setActiveId(null);
    setOverGroupId(null);
  };

  // Total columns: Drag handle + Name + Status + visible stats + Actions
  const totalColumns = 3 + visibleColumns.length;

  const handleGroupSelect = (group: ProfileGroup, ctrlKey: boolean) => {
    const groupProfileNames = group.profiles.map((p) => p.name);
    const allSelected = groupProfileNames.every((name) =>
      selectedProfiles.has(name),
    );

    const newSelected = new Set(ctrlKey ? selectedProfiles : []);
    if (allSelected) {
      // All are selected, deselect them
      for (const name of groupProfileNames) {
        newSelected.delete(name);
      }
    } else {
      // Select all in group
      for (const name of groupProfileNames) {
        newSelected.add(name);
      }
    }
    onSelectionChange(newSelected);
  };

  const renderGroupHeader = (group: ProfileGroup) => {
    const isExpanded = expandedGroups.has(group.name);
    const groupDropId = `${GROUP_DROP_PREFIX}${group.name}`;
    const isOver = overGroupId === groupDropId;
    const groupProfileNames = group.profiles.map((p) => p.name);
    const allSelected = groupProfileNames.every((name) =>
      selectedProfiles.has(name),
    );
    const someSelected = groupProfileNames.some((name) =>
      selectedProfiles.has(name),
    );

    return (
      <tr
        key={`group-${group.name}`}
        data-group-drop-id={groupDropId}
        className={clsx(
          "bg-zinc-800/50 cursor-pointer hover:bg-zinc-800 transition-colors",
          isOver && activeId && "bg-blue-900/30 ring-1 ring-blue-500/50",
          allSelected && "bg-blue-900/20",
          someSelected && !allSelected && "bg-blue-900/10",
        )}
        onClick={(e) => handleGroupSelect(group, e.ctrlKey || e.metaKey)}
      >
        <td colSpan={totalColumns} className="px-3 py-1.5">
          <div className="flex items-center gap-2">
            <button
              className="p-1 -m-1 text-zinc-400 hover:text-zinc-200"
              onClick={(e) => {
                e.stopPropagation();
                toggleGroup(group.name);
              }}
            >
              <ChevronDownIcon
                className={clsx(
                  "h-4 w-4 transition-transform duration-200",
                  !isExpanded && "-rotate-90",
                )}
              />
            </button>
            <span className="text-sm font-medium text-zinc-300">
              {group.name}
            </span>
            <span className="text-xs text-zinc-500">
              ({group.profiles.length})
            </span>
          </div>
        </td>
      </tr>
    );
  };

  const activeProfile = activeId
    ? profiles.find((p) => p.name === activeId)
    : null;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragStart={handleDragStart}
      onDragOver={handleDragOver}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
      <Table>
        <TableHead>
          <TableRow>
            <TableHeader className="w-px whitespace-nowrap">Name</TableHeader>
            <TableHeader className="w-full">Status</TableHeader>
            {visibleColumns.map((col, index) => (
              <TableHeader
                key={col.key}
                className={`${getColumnBreakpointClass(index)} w-px whitespace-nowrap text-right`}
              >
                {col.label}
              </TableHeader>
            ))}
            <TableHeader className="w-px whitespace-nowrap text-right">
              <div className="flex justify-end">
                <Dropdown
                  items={columnSelectorItems}
                  trigger={
                    <ViewColumnsIcon className="h-5 w-5" aria-hidden="true" />
                  }
                />
              </div>
            </TableHeader>
          </TableRow>
        </TableHead>
        <TableBody>
          <SortableContext items={allProfileIds}>
            {/* Ungrouped profiles first - no header, always visible */}
            {ungroupedProfiles.length > 0 && (
              <>
                {ungroupedProfiles.map((profile) => (
                  <SortableProfileRow
                    key={profile.name}
                    profile={profile}
                    status={statuses[profile.name]}
                    visibleColumns={visibleColumns}
                    actions={actions}
                    isSelected={selectedProfiles.has(profile.name)}
                    onClone={onClone}
                    onDelete={onDelete}
                    onSelect={handleSelect}
                    onNavigate={handleNavigate}
                  />
                ))}
              </>
            )}

            {/* Grouped profiles with collapsible headers */}
            {groupedProfiles.map((group) => (
              <Fragment key={group.name}>
                {renderGroupHeader(group)}
                {expandedGroups.has(group.name) &&
                  group.profiles.map((profile) => (
                    <SortableProfileRow
                      key={profile.name}
                      profile={profile}
                      status={statuses[profile.name]}
                      visibleColumns={visibleColumns}
                      actions={actions}
                      isSelected={selectedProfiles.has(profile.name)}
                      onClone={onClone}
                      onDelete={onDelete}
                      onSelect={handleSelect}
                      onNavigate={handleNavigate}
                    />
                  ))}
              </Fragment>
            ))}
          </SortableContext>
        </TableBody>
      </Table>

      <DragOverlay>
        {activeProfile && (
          <table className="w-full">
            <tbody>
              <SortableProfileRow
                profile={activeProfile}
                status={statuses[activeProfile.name]}
                visibleColumns={visibleColumns}
                actions={actions}
                isSelected={selectedProfiles.has(activeProfile.name)}
                onClone={onClone}
                onDelete={onDelete}
                onSelect={handleSelect}
                onNavigate={handleNavigate}
                isDragOverlay
              />
            </tbody>
          </table>
        )}
      </DragOverlay>
    </DndContext>
  );
}
