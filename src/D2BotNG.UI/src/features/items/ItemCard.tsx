/**
 * ItemCard component
 *
 * Displays a single item in a card format.
 * Shows item image, name with quality color.
 * Hovering shows the full ItemTooltip.
 * Right-click reveals a context menu with copy/save/remove actions.
 */

import { memo, useCallback, useMemo, useState } from "react";
import clsx from "clsx";
import type { Item } from "@/generated/items_pb";
import { useRemoveItem } from "@/hooks/useRemoveItem";
import { DeleteConfirmationDialog } from "@/components/ui/DeleteConfirmationDialog";
import { ItemImage } from "./ItemImage";
import { ItemTooltip } from "./ItemTooltip";
import { useItemContextMenu } from "./useItemContextMenu";
import { isEthereal } from "./item-utils";

export interface ItemCardProps {
  /** The item to display */
  item: Item;
  /**
   * Path of the mule file this item came from. Required to enable the Remove
   * action; if absent, Remove is hidden from the right-click menu.
   */
  entityPath?: string;
  /** Additional CSS classes */
  className?: string;
}

/**
 * Extract everything after the first `$` in an item's description.
 * Mule items embed `gid:classid:loc:x:y:base64info:` after the `$` separator;
 * the backend matches lines via `StartsWith` against this string.
 */
function getDescriptionId(item: Item): string {
  const desc = item.description ?? "";
  const idx = desc.indexOf("$");
  if (idx < 0) return "";
  return desc.slice(idx + 1);
}

export const ItemCard = memo(function ItemCard({
  item,
  entityPath,
  className,
}: ItemCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const removeItem = useRemoveItem();

  const descriptionId = useMemo(() => getDescriptionId(item), [item]);
  const canRemove =
    !!entityPath && entityPath.length > 0 && descriptionId.length > 0;

  const handleRemoveRequested = useCallback(() => {
    setConfirmOpen(true);
  }, []);

  const handleConfirmRemove = useCallback(() => {
    if (!entityPath) return;
    removeItem.mutate(
      { entityPath, descriptionId },
      { onSettled: () => setConfirmOpen(false) },
    );
  }, [entityPath, descriptionId, removeItem]);

  const { contextMenu, onContextMenu } = useItemContextMenu({
    item,
    onRemove: canRemove ? handleRemoveRequested : undefined,
  });

  return (
    <div
      className={clsx(
        "flex min-w-0 items-center gap-3 rounded-lg bg-zinc-900 p-3 ring-1 ring-zinc-800 transition-colors hover:ring-zinc-700",
        className,
      )}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onContextMenu={onContextMenu}
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
      {(item.sockets.length > 0 || isEthereal(item)) && (
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
          {isEthereal(item) && (
            <div
              className="flex h-6 w-6 items-center justify-center rounded-full bg-cyan-900/60 text-xs font-bold text-cyan-300"
              title="Ethereal"
            >
              E
            </div>
          )}
        </div>
      )}
      {contextMenu}
      {canRemove && (
        <DeleteConfirmationDialog
          open={confirmOpen}
          entityType="Item"
          entityName={item.name}
          warningMessage="This will remove the item from the mule file on disk. The action cannot be undone."
          isPending={removeItem.isPending}
          onConfirm={handleConfirmRemove}
          onCancel={() => setConfirmOpen(false)}
        />
      )}
    </div>
  );
});
