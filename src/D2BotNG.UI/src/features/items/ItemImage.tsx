/**
 * ItemImage component
 *
 * Displays a Diablo 2 item image rendered client-side from DC6 sprites.
 * Uses the frontend rendering library instead of server-side rendering.
 */

import { memo } from "react";
import { ItemSprite } from "@/lib/rendering";
import { isEthereal, type RenderableItem } from "./item-utils";

export interface ItemImageProps {
  /** The item to display */
  item: RenderableItem;
  /** Whether to show socketed items (default: true) */
  showSockets?: boolean;
}

export const ItemImage = memo(function ItemImage({
  item,
  showSockets = true,
}: ItemImageProps) {
  return (
    <div className="flex min-h-16 min-w-16 items-center justify-center">
      <ItemSprite
        code={item.code}
        colorShift={item.itemColor}
        invTrans={item.invTrans}
        ethereal={isEthereal(item)}
        sockets={showSockets ? item.sockets : []}
        hd={item.hd}
        alt={item.name}
      />
    </div>
  );
});
