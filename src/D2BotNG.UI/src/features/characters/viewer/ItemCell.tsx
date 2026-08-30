/**
 * One item where it sits: its sprite, its hover tooltip and its right-click menu.
 *
 * Shared by the storage grids and the paperdoll slots, which are the same thing to a reader and
 * differ only in how the box around the sprite is sized — a cell footprint in a grid, a fixed
 * square in a paperdoll. Everything else was spelled out twice, and the two had already begun to
 * disagree about when sockets are drawn.
 *
 * Hover is tracked once, here, on the cell — a box whose size is fixed by the layout — and handed
 * to the tooltip rather than let it watch its own wrapper. The wrapper is not a fixed box: turning
 * the socket overlay on re-renders the sprite at the item's full grid footprint instead of the
 * artwork's own bounds, so the element the tooltip would be measuring changes size in response to
 * being hovered.
 */

import { useState, type CSSProperties, type ReactNode } from "react";
import clsx from "clsx";
import { ItemSprite } from "@/lib/rendering";
import { ItemTooltip, isEthereal, useItemContextMenu } from "@/features/items";
import type { DisplayItem } from "./contracts";

export function ItemCell({
  item,
  className,
  style,
  spriteClassName,
  spriteStyle,
  empty,
}: {
  item: DisplayItem | undefined;
  /** The box the item sits in — a grid footprint, or a slot in the paperdoll. */
  className?: string;
  style?: CSSProperties;
  /** The sprite's own box inside it, which is what the tooltip anchors to. */
  spriteClassName?: string;
  spriteStyle?: CSSProperties;
  /** Drawn instead when there is no item; a paperdoll slot names itself. */
  empty?: ReactNode;
}) {
  const [hovered, setHovered] = useState(false);
  const { contextMenu, onContextMenu } = useItemContextMenu({ item });

  return (
    <>
      <div
        className={className}
        style={style}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onContextMenu={item ? onContextMenu : undefined}
      >
        {item ? (
          <ItemTooltip item={item} showSprite={false} open={hovered}>
            <div
              className={clsx(
                "flex items-center justify-center",
                spriteClassName,
              )}
              style={spriteStyle}
            >
              <ItemSprite
                code={item.code}
                colorShift={item.itemColor}
                invTrans={item.invTrans}
                ethereal={isEthereal(item)}
                // Only under the pointer: a full stash drawn with every socket marked is a wall of
                // dots, and the sprite is small enough that they hide the item itself.
                sockets={hovered ? item.sockets : undefined}
                alt={item.name}
              />
            </div>
          </ItemTooltip>
        ) : (
          empty
        )}
      </div>
      {/* Outside the box above, and that placement is the whole point.
          React synthesises `mouseenter`/`mouseleave` from the REACT tree rather than the DOM one,
          so a portal rendered as a child of that div counts as inside it however far away it is
          drawn. Moving the pointer from the item onto the open menu fired no leave at all, and the
          tooltip stayed up underneath it until something else dismissed it. As a sibling, the menu
          is somewhere else in both trees and the leave arrives when it should. */}
      {item && contextMenu}
    </>
  );
}
