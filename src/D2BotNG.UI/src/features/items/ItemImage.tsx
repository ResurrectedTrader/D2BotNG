/**
 * ItemImage component
 *
 * Displays a Diablo 2 item image rendered client-side from DC6 sprites.
 * Uses the frontend rendering library instead of server-side rendering.
 */

import { memo } from "react";
import clsx from "clsx";
import type { Item } from "@/generated/items_pb";
import { ItemSprite } from "@/lib/rendering";

export interface ItemImageProps {
  /** The item to display */
  item: Item;
  /** Additional CSS classes */
  className?: string;
  /** Size variant */
  size?: "sm" | "md" | "lg";
  /** Whether to show socketed items (default: true) */
  showSockets?: boolean;
}

const sizeClasses = {
  sm: "min-h-6 min-w-6",
  md: "min-h-12 min-w-12",
  lg: "min-h-16 min-w-16",
};

export const ItemImage = memo(function ItemImage({
  item,
  className,
  size = "md",
  showSockets = true,
}: ItemImageProps) {
  return (
    <div
      className={clsx(
        "flex items-center justify-center",
        sizeClasses[size],
        className,
      )}
    >
      <ItemSprite
        code={item.code}
        colorShift={item.itemColor}
        ethereal={item.description?.includes("Ethereal")}
        sockets={showSockets ? item.sockets : []}
        alt={item.name}
      />
    </div>
  );
});
