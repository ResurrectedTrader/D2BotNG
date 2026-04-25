/**
 * CharactersPage component
 *
 * View character inventories with dropdown entity selector,
 * game mode filters, and item search.
 */

import { useState, useEffect, useMemo, useRef, useCallback } from "react";
import { create } from "@bufbuild/protobuf";
import { useVirtualizer } from "@tanstack/react-virtual";
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

const PAGE_SIZE = 200;

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

function computeColumns(): number {
  if (typeof window === "undefined") return 1;
  if (window.matchMedia("(min-width: 1280px)").matches) return 4;
  if (window.matchMedia("(min-width: 1024px)").matches) return 3;
  if (window.matchMedia("(min-width: 640px)").matches) return 2;
  return 1;
}

function useResponsiveColumns(): number {
  const [columns, setColumns] = useState(computeColumns);

  useEffect(() => {
    const queries = [
      window.matchMedia("(min-width: 640px)"),
      window.matchMedia("(min-width: 1024px)"),
      window.matchMedia("(min-width: 1280px)"),
    ];
    const update = () => setColumns(computeColumns());
    queries.forEach((q) => q.addEventListener("change", update));
    return () =>
      queries.forEach((q) => q.removeEventListener("change", update));
  }, []);

  return columns;
}

interface VirtualItemsGridProps {
  items: Item[];
  hasMore: boolean;
  onLoadMore: () => void;
}

function VirtualItemsGrid({
  items,
  hasMore,
  onLoadMore,
}: VirtualItemsGridProps) {
  const columns = useResponsiveColumns();
  const parentRef = useRef<HTMLDivElement>(null);
  const [scrollElement, setScrollElement] = useState<HTMLElement | null>(null);
  const [scrollMargin, setScrollMargin] = useState(0);

  useEffect(() => {
    setScrollElement(document.querySelector("main"));
  }, []);

  useEffect(() => {
    if (!parentRef.current || !scrollElement) return;
    const measure = () => {
      if (!parentRef.current || !scrollElement) return;
      const parentRect = parentRef.current.getBoundingClientRect();
      const scrollRect = scrollElement.getBoundingClientRect();
      setScrollMargin(
        parentRect.top - scrollRect.top + scrollElement.scrollTop,
      );
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(scrollElement);
    ro.observe(parentRef.current);
    // Also observe the sticky header sibling: when filters wrap onto another
    // line at narrow viewports, the header height changes but this component's
    // own size doesn't, so we'd otherwise miss the layout shift.
    const sibling = parentRef.current.previousElementSibling;
    if (sibling instanceof HTMLElement) {
      ro.observe(sibling);
    }
    return () => ro.disconnect();
  }, [scrollElement]);

  const rowCount = Math.ceil(items.length / columns);

  const rowVirtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => scrollElement,
    estimateSize: () => 96,
    overscan: 4,
    scrollMargin,
  });

  const virtualRows = rowVirtualizer.getVirtualItems();
  const lastVisibleIndex = virtualRows.length
    ? virtualRows[virtualRows.length - 1].index
    : -1;

  useEffect(() => {
    if (hasMore && lastVisibleIndex >= rowCount - 3) {
      onLoadMore();
    }
  }, [hasMore, lastVisibleIndex, rowCount, onLoadMore]);

  return (
    <div ref={parentRef}>
      <div
        style={{
          height: rowVirtualizer.getTotalSize(),
          position: "relative",
          width: "100%",
        }}
      >
        {virtualRows.map((virtualRow) => {
          const startIndex = virtualRow.index * columns;
          const rowItems = items.slice(startIndex, startIndex + columns);
          return (
            <div
              key={virtualRow.key}
              data-index={virtualRow.index}
              ref={rowVirtualizer.measureElement}
              className="grid gap-3 pb-3"
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${virtualRow.start - scrollMargin}px)`,
                gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
              }}
            >
              {rowItems.map((item, i) => (
                <ItemCard
                  key={`${item.code}-${item.name}-${startIndex + i}`}
                  item={item}
                />
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
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

  // Items state - paginated, appends as user scrolls
  const [items, setItems] = useState<Item[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loadingItems, setLoadingItems] = useState(false);
  const fetchTokenRef = useRef(0);
  // Synchronous flag dedupes rapid load-more triggers from the virtualizer.
  const loadingMoreRef = useRef(false);

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

  // Build search request body shared by initial fetch and load-more
  const buildSearchRequest = useCallback(
    (offset: number) => {
      const hasModeFilter =
        hardcoreFilter !== undefined ||
        expansionFilter !== undefined ||
        ladderFilter !== undefined;

      return create(SearchItemsRequestSchema, {
        entityPath: selectedPath ?? "",
        query: debouncedSearch,
        modeFilter: hasModeFilter
          ? create(ModeFilterSchema, {
              hardcore: hardcoreFilter,
              expansion: expansionFilter,
              ladder: ladderFilter,
            })
          : undefined,
        offset,
        limit: PAGE_SIZE,
      });
    },
    [
      selectedPath,
      debouncedSearch,
      hardcoreFilter,
      expansionFilter,
      ladderFilter,
    ],
  );

  // Reset and fetch first page when filters or entities change
  useEffect(() => {
    const token = ++fetchTokenRef.current;
    // Clear any in-flight load-more flag so a stuck guard from an earlier
    // filter doesn't block fresh load-mores on the new dataset.
    loadingMoreRef.current = false;
    async function fetchFirstPage() {
      setLoadingItems(true);
      try {
        const response = await itemClient.search(buildSearchRequest(0));
        if (fetchTokenRef.current !== token) return;
        setItems(response.items);
        setTotalCount(response.total);
      } catch (error) {
        if (fetchTokenRef.current !== token) return;
        console.error("Failed to fetch items:", error);
        setItems([]);
        setTotalCount(0);
      } finally {
        if (fetchTokenRef.current === token) {
          setLoadingItems(false);
        }
      }
    }
    fetchFirstPage();
  }, [buildSearchRequest, entitiesVersion]);

  // Append next page when virtualizer requests more
  const handleLoadMore = useCallback(async () => {
    // Sync ref guards against rapid re-triggers before React commits state.
    if (loadingMoreRef.current || loadingItems) return;
    loadingMoreRef.current = true;
    const currentToken = fetchTokenRef.current;
    try {
      const response = await itemClient.search(
        buildSearchRequest(items.length),
      );
      if (fetchTokenRef.current !== currentToken) return;
      setItems((prev) => [...prev, ...response.items]);
    } catch (error) {
      console.error("Failed to load more items:", error);
    } finally {
      if (fetchTokenRef.current === currentToken) {
        loadingMoreRef.current = false;
      }
    }
  }, [buildSearchRequest, items.length, loadingItems]);

  const hasMore = items.length < totalCount;

  // Get title based on selection
  const pageTitle = useMemo(() => {
    if (!selectedPath) return "All Characters";
    const node = treeNodes.find((n) => n.path === selectedPath);
    return node?.displayName ?? "Characters";
  }, [selectedPath, treeNodes]);

  if (loadingEntities) {
    return <LoadingSpinner fullPage />;
  }

  if (entities.length === 0) {
    return (
      <EmptyState
        icon={UserIcon}
        title="No characters yet"
        description="This view shows characters that have been logged with the mule logger. Start the mule logger on a profile to begin tracking character inventories."
      />
    );
  }

  return (
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-3 border-b border-zinc-800/50">
        <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
          <h1 className="text-lg font-bold text-zinc-100">{pageTitle}</h1>
          <span className="text-sm text-zinc-400">
            {totalCount} item{totalCount !== 1 && "s"}
            {debouncedSearch && " matching search"}
          </span>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
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
      </div>

      {/* Items grid */}
      {loadingItems ? (
        <LoadingSpinner />
      ) : items.length > 0 ? (
        <VirtualItemsGrid
          items={items}
          hasMore={hasMore}
          onLoadMore={handleLoadMore}
        />
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
