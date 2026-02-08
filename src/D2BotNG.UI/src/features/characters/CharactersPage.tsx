/**
 * CharactersPage component
 *
 * View character inventories with dropdown entity selector,
 * game mode filters, and item search.
 */

import { useState, useEffect, useMemo, useRef } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptyState, LoadingSpinner } from "@/components/ui";
import { itemClient } from "@/lib/grpc-client";
import { useEntitiesVersion } from "@/stores/event-store";
import {
  ListEntitiesRequestSchema,
  SearchItemsRequestSchema,
  ModeFilterSchema,
  type Entity,
  type Item,
} from "@/generated/items_pb";
import { ItemCard } from "@/features/items/ItemCard";
import {
  MagnifyingGlassIcon,
  CubeIcon,
  ChevronUpDownIcon,
  FolderIcon,
  UserIcon,
  CheckIcon,
} from "@heroicons/react/24/outline";
import clsx from "clsx";

interface TreeNode {
  path: string;
  displayName: string;
  fullDisplayName: string; // For dropdown display: "Realm > Account > CharName"
  isLeaf: boolean;
  depth: number;
  mode?: {
    hardcore: boolean;
    expansion: boolean;
    ladder: boolean;
  };
}

function buildFlatTree(entities: Entity[]): TreeNode[] {
  // Sort entities by path
  const sorted = [...entities].sort((a, b) => a.path.localeCompare(b.path));

  const nodes: TreeNode[] = [];

  for (const entity of sorted) {
    const parts = entity.path.split("/");
    const depth = parts.length - 1;

    // Build full display name showing hierarchy
    const fullDisplayName = parts.join(" > ");

    nodes.push({
      path: entity.path,
      displayName: entity.displayName,
      fullDisplayName,
      isLeaf: entity.isLeaf,
      depth,
      mode: entity.mode
        ? {
            hardcore: entity.mode.hardcore,
            expansion: entity.mode.expansion,
            ladder: entity.mode.ladder,
          }
        : undefined,
    });
  }

  return nodes;
}

interface EntitySelectorProps {
  nodes: TreeNode[];
  selectedPath: string | null;
  onSelect: (path: string | null) => void;
}

function EntitySelector({
  nodes,
  selectedPath,
  onSelect,
}: EntitySelectorProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);

  // Close on click outside
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Filter nodes by search
  const filteredNodes = useMemo(() => {
    if (!search.trim()) return nodes;
    const searchLower = search.toLowerCase();
    return nodes.filter(
      (n) =>
        n.displayName.toLowerCase().includes(searchLower) ||
        n.fullDisplayName.toLowerCase().includes(searchLower),
    );
  }, [nodes, search]);

  // Get display text for selected item
  const selectedDisplay = useMemo(() => {
    if (!selectedPath) return "All Characters";
    const node = nodes.find((n) => n.path === selectedPath);
    if (!node) return selectedPath;
    return node.fullDisplayName;
  }, [selectedPath, nodes]);

  const handleSelect = (path: string | null) => {
    onSelect(path);
    setOpen(false);
    setSearch("");
  };

  return (
    <div ref={containerRef} className="relative">
      {/* Trigger button */}
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center justify-between gap-2 rounded-lg bg-zinc-800 px-3 py-2 text-left text-sm text-zinc-100 ring-1 ring-inset ring-zinc-700 hover:bg-zinc-750 focus:outline-none focus:ring-2 focus:ring-d2-gold"
      >
        <span className="truncate">{selectedDisplay}</span>
        <ChevronUpDownIcon className="h-4 w-4 flex-shrink-0 text-zinc-400" />
      </button>

      {/* Dropdown */}
      {open && (
        <div className="absolute left-0 right-0 z-50 mt-1 max-h-80 overflow-hidden rounded-lg bg-zinc-800 ring-1 ring-zinc-700 shadow-lg">
          {/* Search input */}
          <div className="border-b border-zinc-700 p-2">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search characters..."
              className="block w-full rounded border-0 bg-zinc-900 px-2 py-1.5 text-sm text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:ring-1 focus:ring-d2-gold"
              autoFocus
            />
          </div>

          {/* Options list */}
          <div className="max-h-60 overflow-y-auto p-1">
            {/* All Characters option */}
            <button
              type="button"
              onClick={() => handleSelect(null)}
              className={clsx(
                "flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm transition-colors",
                selectedPath === null
                  ? "bg-d2-gold/20 text-d2-gold"
                  : "text-zinc-300 hover:bg-zinc-700",
              )}
            >
              <FolderIcon className="h-4 w-4 flex-shrink-0" />
              <span className="flex-1">All Characters</span>
              {selectedPath === null && (
                <CheckIcon className="h-4 w-4 flex-shrink-0" />
              )}
            </button>

            {/* Entity options */}
            {filteredNodes.map((node) => (
              <button
                key={node.path}
                type="button"
                onClick={() => handleSelect(node.path)}
                className={clsx(
                  "flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm transition-colors",
                  selectedPath === node.path
                    ? "bg-d2-gold/20 text-d2-gold"
                    : "text-zinc-300 hover:bg-zinc-700",
                )}
                style={{ paddingLeft: `${8 + node.depth * 12}px` }}
              >
                {node.isLeaf ? (
                  <UserIcon className="h-4 w-4 flex-shrink-0" />
                ) : (
                  <FolderIcon className="h-4 w-4 flex-shrink-0" />
                )}
                <span className="flex-1 truncate">{node.displayName}</span>

                {/* Mode badges */}
                {node.isLeaf && node.mode && (
                  <div className="flex gap-1">
                    {node.mode.hardcore && (
                      <span className="px-1 py-0.5 text-[10px] font-medium rounded bg-red-900/50 text-red-300">
                        HC
                      </span>
                    )}
                    {node.mode.ladder && (
                      <span className="px-1 py-0.5 text-[10px] font-medium rounded bg-green-900/50 text-green-300">
                        L
                      </span>
                    )}
                    {!node.mode.expansion && (
                      <span className="px-1 py-0.5 text-[10px] font-medium rounded bg-blue-900/50 text-blue-300">
                        C
                      </span>
                    )}
                  </div>
                )}

                {selectedPath === node.path && (
                  <CheckIcon className="h-4 w-4 flex-shrink-0" />
                )}
              </button>
            ))}

            {filteredNodes.length === 0 && search && (
              <p className="px-2 py-3 text-center text-sm text-zinc-500">
                No matches found
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

interface ModeToggleProps {
  label: string;
  value: boolean | undefined;
  onChange: (value: boolean | undefined) => void;
  activeColor: string;
}

function ModeToggle({ label, value, onChange, activeColor }: ModeToggleProps) {
  const handleClick = () => {
    // Cycle: undefined -> true -> false -> undefined
    if (value === undefined) onChange(true);
    else if (value === true) onChange(false);
    else onChange(undefined);
  };

  return (
    <button
      onClick={handleClick}
      className={clsx(
        "px-2 py-1 text-xs font-medium rounded transition-colors",
        value === undefined && "bg-zinc-800 text-zinc-400",
        value === true && activeColor,
        value === false && "bg-zinc-800 text-zinc-500 line-through",
      )}
    >
      {label}
    </button>
  );
}

export function CharactersPage() {
  // Entity state
  const [entities, setEntities] = useState<Entity[]>([]);
  const [loadingEntities, setLoadingEntities] = useState(true);
  const [selectedPath, setSelectedPath] = useState<string | null>(null);

  // Search and filter state
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [hardcoreFilter, setHardcoreFilter] = useState<boolean | undefined>();
  const [expansionFilter, setExpansionFilter] = useState<boolean | undefined>();
  const [ladderFilter, setLadderFilter] = useState<boolean | undefined>();

  // Items state
  const [items, setItems] = useState<Item[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loadingItems, setLoadingItems] = useState(false);

  // Listen for entity changes from server
  const entitiesVersion = useEntitiesVersion();

  // Build flat tree for dropdown
  const treeNodes = useMemo(() => buildFlatTree(entities), [entities]);

  // Debounce search
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  // Fetch entities on mount and when server notifies of changes
  useEffect(() => {
    async function fetchEntities() {
      try {
        const request = create(ListEntitiesRequestSchema, {});
        const response = await itemClient.listEntities(request);
        setEntities(response.entities);
      } catch (error) {
        console.error("Failed to fetch entities:", error);
      } finally {
        setLoadingEntities(false);
      }
    }
    fetchEntities();
  }, [entitiesVersion]);

  // Fetch items when selection, filters, or entities change
  useEffect(() => {
    async function fetchItems() {
      setLoadingItems(true);
      try {
        const hasModeFilter =
          hardcoreFilter !== undefined ||
          expansionFilter !== undefined ||
          ladderFilter !== undefined;

        const request = create(SearchItemsRequestSchema, {
          entityPath: selectedPath ?? "",
          query: debouncedSearch,
          modeFilter: hasModeFilter
            ? create(ModeFilterSchema, {
                hardcore: hardcoreFilter,
                expansion: expansionFilter,
                ladder: ladderFilter,
              })
            : undefined,
        });

        const response = await itemClient.search(request);
        setItems(response.items);
        setTotalCount(response.items.length);
      } catch (error) {
        console.error("Failed to fetch items:", error);
        setItems([]);
        setTotalCount(0);
      } finally {
        setLoadingItems(false);
      }
    }
    fetchItems();
  }, [
    selectedPath,
    debouncedSearch,
    hardcoreFilter,
    expansionFilter,
    ladderFilter,
    entitiesVersion,
  ]);

  // Get title based on selection
  const pageTitle = useMemo(() => {
    if (!selectedPath) return "All Characters";
    const node = treeNodes.find((n) => n.path === selectedPath);
    return node?.displayName ?? "Characters";
  }, [selectedPath, treeNodes]);

  if (loadingEntities) {
    return <LoadingSpinner fullPage />;
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-zinc-100">{pageTitle}</h1>
        <p className="mt-1 text-sm text-zinc-400">
          {totalCount} item{totalCount !== 1 && "s"}
          {debouncedSearch && " matching search"}
        </p>
      </div>

      {/* Controls */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        {/* Entity selector */}
        <div className="w-full sm:w-72">
          <EntitySelector
            nodes={treeNodes}
            selectedPath={selectedPath}
            onSelect={setSelectedPath}
          />
        </div>

        {/* Search */}
        <div className="flex-1">
          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-zinc-500" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search items (regex)..."
              className="block w-full rounded-lg border-0 bg-zinc-800 py-2 pl-10 pr-3 text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm sm:leading-6"
            />
          </div>
        </div>

        {/* Mode filters */}
        <div className="flex items-center gap-2">
          <span className="text-xs text-zinc-500">Filter:</span>
          <ModeToggle
            label="Hardcore"
            value={hardcoreFilter}
            onChange={setHardcoreFilter}
            activeColor="bg-red-900/50 text-red-300"
          />
          <ModeToggle
            label="Ladder"
            value={ladderFilter}
            onChange={setLadderFilter}
            activeColor="bg-green-900/50 text-green-300"
          />
          <ModeToggle
            label="Expansion"
            value={expansionFilter}
            onChange={setExpansionFilter}
            activeColor="bg-amber-900/50 text-amber-300"
          />
        </div>
      </div>

      {/* Items grid */}
      {loadingItems ? (
        <LoadingSpinner />
      ) : items.length > 0 ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {items.map((item, index) => (
            <ItemCard key={`${item.code}-${item.name}-${index}`} item={item} />
          ))}
        </div>
      ) : (
        <EmptyState
          icon={CubeIcon}
          title="No items found"
          description={
            debouncedSearch ||
            hardcoreFilter !== undefined ||
            ladderFilter !== undefined ||
            expansionFilter !== undefined
              ? "No items match your filters."
              : selectedPath
                ? "This character has no logged items."
                : "No items have been logged yet. Run the mule logger to populate character inventories."
          }
        />
      )}
    </div>
  );
}
