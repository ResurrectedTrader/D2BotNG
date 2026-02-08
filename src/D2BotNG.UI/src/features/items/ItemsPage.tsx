/**
 * ItemsPage component
 *
 * Main page for viewing items from the event stream.
 * Shows items as they arrive via the real-time event connection.
 */

import { useState, useCallback, useMemo } from "react";
import { EmptyState } from "@/components/ui";
import { useItems } from "@/stores/event-store";
import { ItemCard } from "./ItemCard";
import { MagnifyingGlassIcon, CubeIcon } from "@heroicons/react/24/outline";

export function ItemsPage() {
  const [search, setSearch] = useState("");
  const items = useItems();

  // Filter items by search
  const filteredItems = useMemo(() => {
    if (!search.trim()) return items;
    const searchLower = search.toLowerCase();
    return items.filter(
      (item) =>
        item.name.toLowerCase().includes(searchLower) ||
        item.description.toLowerCase().includes(searchLower) ||
        item.code.toLowerCase().includes(searchLower),
    );
  }, [items, search]);

  const handleSearchChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setSearch(e.target.value);
    },
    [],
  );

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-100">Items</h1>
          <p className="mt-1 text-sm text-zinc-400">
            Items received during this session.
            {items.length > 0 && (
              <span className="ml-1">({items.length} total)</span>
            )}
          </p>
        </div>
      </div>

      {/* Search */}
      <div className="flex-1">
        <div className="relative">
          <MagnifyingGlassIcon className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-zinc-500" />
          <input
            type="text"
            value={search}
            onChange={handleSearchChange}
            placeholder="Search items..."
            className="block w-full rounded-lg border-0 bg-zinc-800 py-2 pl-10 pr-3 text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm sm:leading-6"
          />
        </div>
      </div>

      {/* Items grid */}
      {filteredItems.length > 0 ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {filteredItems.map((item, index) => (
            <ItemCard key={item.code + "-" + index} item={item} />
          ))}
        </div>
      ) : (
        <EmptyState
          icon={CubeIcon}
          title="No items found"
          description={
            search
              ? "No items match your search criteria."
              : "Items will appear here as they drop during bot runs."
          }
        />
      )}
    </div>
  );
}
