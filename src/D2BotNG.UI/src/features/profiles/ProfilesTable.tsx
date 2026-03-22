/**
 * ProfilesTable component
 *
 * Displays a table of profiles with status and actions.
 * Supports grouping profiles with expandable group headers.
 * Supports drag-and-drop reordering within and across groups.
 * On desktop (hover devices), hides inline action buttons in favor of right-click context menu.
 */

import { useState, useMemo, useRef, useCallback, Fragment } from "react";
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
  type DragMoveEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
} from "@dnd-kit/sortable";

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
import { useProfileActions, useHasHover, useIsLocalhost } from "@/hooks";
import type { ProfileColumnKey } from "@/hooks";
import { RunState } from "@/generated/common_pb";
import type { Profile, ProfileState } from "@/generated/profiles_pb";
import { canStart, canStop, isActive } from "./profile-states";
import { ProfileStatusBadge } from "./ProfileStatusBadge";
import {
  useProfileActionItems,
  buildSingleProfileActionItems,
  buildMultiProfileActionItems,
} from "./ProfileActions";
import {
  PlayIcon,
  StopIcon,
  EyeIcon,
  EyeSlashIcon,
  ChevronDownIcon,
  Bars2Icon,
  ArrowPathIcon,
} from "@heroicons/react/24/outline";

export interface ProfilesTableProps {
  profiles: Profile[];
  visibleColumns: { key: ProfileColumnKey; label: string }[];
  selectedProfiles: Set<string>;
  onSelectionChange: (selected: Set<string>) => void;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
  onDeleteMultiple: (names: string[]) => void;
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
  hasHover: boolean;
  onClone: (profile: Profile) => void;
  onDelete: (profile: Profile) => void;
  onSelect: (name: string, ctrlKey: boolean, shiftKey: boolean) => void;
  onNavigate: (name: string) => void;
  onRowContextMenu?: (e: React.MouseEvent, profileName: string) => void;
}

function SortableProfileRow({
  profile,
  status,
  visibleColumns,
  actions,
  isSelected,
  hasHover,
  onClone,
  onDelete,
  onSelect,
  onNavigate,
  onRowContextMenu,
}: SortableProfileRowProps) {
  const { attributes, listeners, setNodeRef, isDragging } = useSortable({
    id: profile.name,
  });

  // For mobile: always build action items for the dropdown
  const actionItems = useProfileActionItems({
    profile,
    status,
    onClone,
    onDelete,
  });
  const state = status?.state ?? RunState.STOPPED;
  const windowVisible = status?.windowVisible ?? false;

  const titleParts = [profile.account, profile.character].filter(Boolean);
  const title = titleParts.length > 0 ? titleParts.join(" / ") : undefined;

  return (
    <tr
      ref={setNodeRef}
      className={clsx(
        "border-b border-zinc-800 cursor-pointer hover:bg-zinc-800/50 transition-colors",
        isDragging && "opacity-30",
        isSelected && "bg-blue-900/30",
      )}
      onClick={(e) =>
        !isDragging &&
        onSelect(profile.name, e.ctrlKey || e.metaKey, e.shiftKey)
      }
      onDoubleClick={() => !isDragging && onNavigate(profile.name)}
      onContextMenu={(e) => {
        if (!isDragging && onRowContextMenu) {
          onRowContextMenu(e, profile.name);
        }
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
              (state === RunState.STARTING || state === RunState.STOPPING) &&
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
      {!hasHover && (
        <TableCell
          className="w-px whitespace-nowrap"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="flex items-center justify-end gap-1">
            {canStart(state) && !canStop(state) ? (
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
                disabled={!canStop(state) || actions.stop.isPending}
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
            <span
              className={getColumnAlignedInlineClass(visibleColumns.length)}
            >
              {isActive(state) &&
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
      )}
    </tr>
  );
}

export function ProfilesTable({
  profiles,
  visibleColumns,
  selectedProfiles,
  onSelectionChange,
  onClone,
  onDelete,
  onDeleteMultiple,
}: ProfilesTableProps) {
  const navigate = useNavigate();
  const statuses = useAllProfileStates();
  const actions = useProfileActions();
  const hasHover = useHasHover();
  const isLocalhost = useIsLocalhost();

  const [activeId, setActiveId] = useState<string | null>(null);
  const [overGroupId, setOverGroupId] = useState<string | null>(null);
  const [insertIndicator, setInsertIndicator] = useState<{
    id: string;
    position: "before" | "after";
  } | null>(null);
  const anchorRef = useRef<string | null>(null);

  // Table-level context menu
  const [contextMenuItems, setContextMenuItems] = useState<DropdownItem[]>([]);
  const { contextMenu, onContextMenu: showContextMenu } =
    useContextMenu(contextMenuItems);

  const handleRowContextMenu = useCallback(
    (e: React.MouseEvent, profileName: string) => {
      // Determine effective selection for context menu
      const isInSelection =
        selectedProfiles.has(profileName) && selectedProfiles.size > 1;

      if (isInSelection) {
        // Right-clicked a profile that's part of a multi-selection: keep selection, show multi-menu
        const selectedProfileObjects = profiles.filter((p) =>
          selectedProfiles.has(p.name),
        );
        const items = buildMultiProfileActionItems({
          profiles: selectedProfileObjects,
          statuses,
          isLocalhost,
          actions,
          onDelete: onDeleteMultiple,
        });
        setContextMenuItems(items);
      } else {
        // Right-clicked an unselected profile or single-selected: select it, show single menu
        onSelectionChange(new Set([profileName]));
        const profile = profiles.find((p) => p.name === profileName);
        if (!profile) return;
        const items = buildSingleProfileActionItems({
          profile,
          status: statuses[profileName],
          isLocalhost,
          actions,
          onEdit: (p) => navigate(`/profiles/${encodeURIComponent(p.name)}`),
          onClone,
          onDelete,
        });
        setContextMenuItems(items);
      }

      showContextMenu(e);
    },
    [
      selectedProfiles,
      profiles,
      statuses,
      isLocalhost,
      actions,
      onSelectionChange,
      onClone,
      onDelete,
      onDeleteMultiple,
      navigate,
      showContextMenu,
    ],
  );

  const handleSelect = (name: string, ctrlKey: boolean, shiftKey: boolean) => {
    if (shiftKey && anchorRef.current) {
      // Shift+Click: select range from anchor to clicked item, replacing selection
      const anchorIndex = allProfileIds.indexOf(anchorRef.current);
      const targetIndex = allProfileIds.indexOf(name);
      if (anchorIndex !== -1 && targetIndex !== -1) {
        const start = Math.min(anchorIndex, targetIndex);
        const end = Math.max(anchorIndex, targetIndex);
        const rangeNames = allProfileIds.slice(start, end + 1);
        onSelectionChange(new Set(rangeNames));
        return;
      }
    }

    // Click or Ctrl+Click sets the anchor
    anchorRef.current = name;

    const newSelected = new Set(selectedProfiles);
    if (ctrlKey || !hasHover) {
      // Ctrl+Click or touch: toggle individual selection
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

  // All profile IDs in visual render order, plus section edge sets for indicator shifting
  const { allProfileIds, sectionStarts, sectionEnds } = useMemo(() => {
    const ids: string[] = [];
    const starts = new Set<string>();
    const ends = new Set<string>();

    function addSection(items: Profile[]) {
      if (items.length === 0) return;
      starts.add(items[0].name);
      ends.add(items[items.length - 1].name);
      for (const p of items) ids.push(p.name);
    }

    addSection(ungroupedProfiles);
    for (const group of groupedProfiles) addSection(group.profiles);

    return { allProfileIds: ids, sectionStarts: starts, sectionEnds: ends };
  }, [ungroupedProfiles, groupedProfiles]);

  // Track which groups the user has explicitly collapsed (all expanded by default)
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(
    new Set(),
  );

  const toggleGroup = (groupName: string) => {
    setCollapsedGroups((prev) => {
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
    const draggedId = event.active.id as string;
    setActiveId(draggedId);

    // If dragging an unselected profile, select only that one
    if (!selectedProfiles.has(draggedId) || selectedProfiles.size <= 1) {
      onSelectionChange(new Set([draggedId]));
    }
  };

  // Shared indicator computation — called from both handleDragOver and handleDragMove.
  // Determines which row to show the indicator on, shifting through consecutive
  // dragged items at section edges so the indicator can appear before the first
  // or after the last item even when they are being dragged.
  function updateInsertIndicator(
    over: NonNullable<DragOverEvent["over"]>,
    delta: { y: number },
    initialRect: { top: number; height: number } | null,
  ) {
    const overId = over.id as string;
    if (overId.startsWith(GROUP_DROP_PREFIX) || !initialRect) return;

    const activeCenterY = initialRect.top + delta.y + initialRect.height / 2;
    const isAfter = activeCenterY > over.rect.top + over.rect.height / 2;

    let targetId = overId;
    const position: "before" | "after" = isAfter ? "after" : "before";
    const overIdx = allProfileIds.indexOf(overId);

    if (overIdx !== -1) {
      if (position === "before") {
        let idx = overIdx - 1;
        while (idx >= 0 && selectedProfiles.has(allProfileIds[idx])) idx--;
        const firstDragged = allProfileIds[idx + 1];
        if (idx + 1 < overIdx && sectionStarts.has(firstDragged)) {
          targetId = firstDragged;
        }
      } else {
        let idx = overIdx + 1;
        while (
          idx < allProfileIds.length &&
          selectedProfiles.has(allProfileIds[idx])
        )
          idx++;
        const lastDragged = allProfileIds[idx - 1];
        if (idx - 1 > overIdx && sectionEnds.has(lastDragged)) {
          targetId = lastDragged;
        }
      }
    }

    setInsertIndicator({ id: targetId, position });
  }

  const handleDragOver = (event: DragOverEvent) => {
    const { over } = event;
    if (!over) {
      setOverGroupId(null);
      return;
    }

    const overId = over.id as string;
    if (overId.startsWith(GROUP_DROP_PREFIX)) {
      setOverGroupId(overId);
      setInsertIndicator(null);
      return;
    }

    // Update group highlight
    const overProfile = profiles.find((p) => p.name === overId);
    if (overProfile) {
      setOverGroupId(
        overProfile.group
          ? `${GROUP_DROP_PREFIX}${overProfile.group}`
          : UNGROUPED_DROP_ID,
      );
    }

    updateInsertIndicator(over, event.delta, event.active.rect.current.initial);
  };

  // Fires on every pointer move — updates the before/after position when the
  // pointer crosses an item's center without the `over` target changing.
  const handleDragMove = (event: DragMoveEvent) => {
    if (event.over) {
      updateInsertIndicator(
        event.over,
        event.delta,
        event.active.rect.current.initial,
      );
    }
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveId(null);
    setOverGroupId(null);
    setInsertIndicator(null);

    if (!over) return;

    const draggedId = active.id as string;
    const overId = over.id as string;

    // Determine which profiles are being dragged (preserving visual order)
    const draggedNames = allProfileIds.filter((id) => selectedProfiles.has(id));

    if (draggedNames.length === 1 && draggedId === overId) return;

    const draggedSet = new Set(draggedNames);

    // Determine before/after from pointer position
    const activeRect = active.rect.current.translated;
    const isAfter =
      !overId.startsWith(GROUP_DROP_PREFIX) &&
      activeRect != null &&
      activeRect.top + activeRect.height / 2 >
        over.rect.top + over.rect.height / 2;

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
      targetIndex = groupProfiles.filter((p) => !draggedSet.has(p.name)).length;
    } else {
      // Dropped on a profile
      const overProfile = profiles.find((p) => p.name === overId);
      if (!overProfile) return;

      targetGroup = overProfile.group || "";
      const groupProfiles =
        targetGroup === ""
          ? ungroupedProfiles
          : (groupedProfiles.find((g) => g.name === targetGroup)?.profiles ??
            []);

      const filteredProfiles = groupProfiles.filter(
        (p) => !draggedSet.has(p.name),
      );

      if (draggedSet.has(overId)) {
        // Over a dragged item — find the nearest non-dragged neighbor
        const overIdx = groupProfiles.findIndex((p) => p.name === overId);
        if (isAfter) {
          // Find next non-dragged item after over
          const next = groupProfiles
            .slice(overIdx + 1)
            .find((p) => !draggedSet.has(p.name));
          targetIndex = next
            ? filteredProfiles.indexOf(next)
            : filteredProfiles.length;
        } else {
          // Find previous non-dragged item before over
          const prev = groupProfiles
            .slice(0, overIdx)
            .reverse()
            .find((p) => !draggedSet.has(p.name));
          targetIndex = prev ? filteredProfiles.indexOf(prev) + 1 : 0;
        }
      } else {
        targetIndex = filteredProfiles.findIndex((p) => p.name === overId);
        if (targetIndex === -1) targetIndex = filteredProfiles.length;
        if (isAfter) targetIndex += 1;
      }
    }

    // Send newGroup if any dragged profile is moving to a different group
    const groupChanged = draggedNames.some((name) => {
      const p = profiles.find((pr) => pr.name === name);
      return p && (p.group || "") !== targetGroup;
    });

    actions.reorder.mutate({
      profileNames: draggedNames,
      newIndex: targetIndex,
      newGroup: groupChanged ? targetGroup : undefined,
    });
  };

  const handleDragCancel = () => {
    setActiveId(null);
    setOverGroupId(null);
    setInsertIndicator(null);
  };

  // Total columns: Name + Status + visible stats + (Actions on mobile only)
  const totalColumns = 2 + visibleColumns.length + (hasHover ? 0 : 1);

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

  const activeProfile = activeId
    ? profiles.find((p) => p.name === activeId)
    : null;

  const renderGroupHeader = (group: ProfileGroup) => {
    const isExpanded = !collapsedGroups.has(group.name);
    const groupDropId = `${GROUP_DROP_PREFIX}${group.name}`;
    const groupProfileNames = group.profiles.map((p) => p.name);
    const allSelected = groupProfileNames.every((name) =>
      selectedProfiles.has(name),
    );
    const someSelected = groupProfileNames.some((name) =>
      selectedProfiles.has(name),
    );
    const isDragFromOtherGroup =
      activeProfile != null && (activeProfile.group || "") !== group.name;
    const isOver = overGroupId === groupDropId && isDragFromOtherGroup;

    return (
      <tr
        key={`group-${group.name}`}
        data-group-drop-id={groupDropId}
        className={clsx(
          "bg-zinc-800/50 cursor-pointer hover:bg-zinc-800 transition-colors",
          isOver && "ring-1 ring-zinc-400/50",
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

  const insertIndicatorRow = (
    <tr aria-hidden>
      <td
        colSpan={100}
        style={{
          padding: 0,
          border: "none",
          height: "2px",
          lineHeight: 0,
          backgroundColor: "#a1a1aa",
        }}
      />
    </tr>
  );

  return (
    <>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragStart={handleDragStart}
        onDragOver={handleDragOver}
        onDragMove={handleDragMove}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
        <Table className="select-none">
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
              {!hasHover && (
                <TableHeader className="w-px whitespace-nowrap text-right">
                  Actions
                </TableHeader>
              )}
            </TableRow>
          </TableHead>
          <TableBody>
            <SortableContext items={allProfileIds}>
              {/* Ungrouped profiles first - no header, always visible */}
              {ungroupedProfiles.map((profile) => (
                <Fragment key={profile.name}>
                  {insertIndicator?.id === profile.name &&
                    insertIndicator.position === "before" &&
                    insertIndicatorRow}
                  <SortableProfileRow
                    profile={profile}
                    status={statuses[profile.name]}
                    visibleColumns={visibleColumns}
                    actions={actions}
                    isSelected={selectedProfiles.has(profile.name)}
                    hasHover={hasHover}
                    onClone={onClone}
                    onDelete={onDelete}
                    onSelect={handleSelect}
                    onNavigate={handleNavigate}
                    onRowContextMenu={handleRowContextMenu}
                  />
                  {insertIndicator?.id === profile.name &&
                    insertIndicator.position === "after" &&
                    insertIndicatorRow}
                </Fragment>
              ))}

              {/* Grouped profiles with collapsible headers */}
              {groupedProfiles.map((group) => (
                <Fragment key={group.name}>
                  {renderGroupHeader(group)}
                  {!collapsedGroups.has(group.name) &&
                    group.profiles.map((profile) => (
                      <Fragment key={profile.name}>
                        {insertIndicator?.id === profile.name &&
                          insertIndicator.position === "before" &&
                          insertIndicatorRow}
                        <SortableProfileRow
                          profile={profile}
                          status={statuses[profile.name]}
                          visibleColumns={visibleColumns}
                          actions={actions}
                          isSelected={selectedProfiles.has(profile.name)}
                          hasHover={hasHover}
                          onClone={onClone}
                          onDelete={onDelete}
                          onSelect={handleSelect}
                          onNavigate={handleNavigate}
                          onRowContextMenu={handleRowContextMenu}
                        />
                        {insertIndicator?.id === profile.name &&
                          insertIndicator.position === "after" &&
                          insertIndicatorRow}
                      </Fragment>
                    ))}
                </Fragment>
              ))}
            </SortableContext>
          </TableBody>
        </Table>

        <DragOverlay dropAnimation={null}>
          {activeProfile && (
            <table className="w-full opacity-80">
              <tbody>
                <tr className="bg-zinc-900 shadow-lg border border-zinc-700">
                  <TableCell className="w-px whitespace-nowrap">
                    <div className="flex items-center gap-2">
                      <Bars2Icon className="h-4 w-4 text-zinc-500" />
                      <span className="font-medium text-zinc-100">
                        {activeProfile.name}
                      </span>
                      {selectedProfiles.size > 1 && (
                        <span className="bg-blue-500 text-white text-xs font-bold rounded-full px-1.5 py-0.5 leading-none">
                          +{selectedProfiles.size - 1}
                        </span>
                      )}
                    </div>
                  </TableCell>
                </tr>
              </tbody>
            </table>
          )}
        </DragOverlay>
      </DndContext>

      {contextMenu}
    </>
  );
}
