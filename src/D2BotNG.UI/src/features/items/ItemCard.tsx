/**
 * ItemCard component
 *
 * Displays a single item in a card format.
 * Shows item image, name with quality color.
 * Hovering shows the full ItemTooltip.
 */

import { memo, useState } from "react";
import clsx from "clsx";
import type { Item } from "@/generated/items_pb";
import { ItemImage } from "./ItemImage";
import { ItemTooltip } from "./ItemTooltip";

export interface ItemCardProps {
  /** The item to display */
  item: Item;
  /** Additional CSS classes */
  className?: string;
}

export const ItemCard = memo(function ItemCard({
  item,
  className,
}: ItemCardProps) {
  const [isHovered, setIsHovered] = useState(false);

  return (
    <div
      className={clsx(
        "flex min-w-0 items-center gap-3 rounded-lg bg-zinc-900 p-3 ring-1 ring-zinc-800 transition-colors hover:ring-zinc-700",
        className,
      )}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      {/* Item image with tooltip */}
      <ItemTooltip item={item} showSprite={false}>
        <div className="flex-shrink-0 cursor-help">
          <ItemImage item={item} size="lg" showSockets={isHovered} />
        </div>
      </ItemTooltip>

      {/* Item details */}
      <div className="min-w-0 flex-1">
        {/* Item name */}
        <div className="truncate font-medium text-zinc-100" title={item.name}>
          {item.name}
        </div>

        {/* Item header if present (like "Superior" or "Ethereal") */}
        {item.header && (
          <div className="mt-0.5 text-xs text-zinc-400">{item.header}</div>
        )}
      </div>

      {/* Item badges */}
      {(item.sockets.length > 0 || item.description?.includes("Ethereal")) && (
        <div className="flex flex-shrink-0 flex-col items-center gap-1">
          {/* Socket count indicator */}
          {item.sockets.length > 0 && (
            <div
              className="flex h-6 w-6 items-center justify-center rounded-full bg-zinc-700 text-xs font-bold text-zinc-100"
              title={
                item.sockets.length +
                " socket" +
                (item.sockets.length > 1 ? "s" : "")
              }
            >
              {item.sockets.length}
            </div>
          )}

          {/* Ethereal indicator */}
          {item.description?.includes("Ethereal") && (
            <div
              className="flex h-6 w-6 items-center justify-center rounded-full bg-cyan-900/60 text-xs font-bold text-cyan-300"
              title="Ethereal"
            >
              E
            </div>
          )}
        </div>
      )}
    </div>
  );
});
