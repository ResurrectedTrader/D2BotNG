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

/**
 * The wash the game paints behind an item (d2r.exe `sub_1401DF810` for a grid, `sub_1401DAEF0`
 * for a slot): a solid rect over its footprint at alpha 63/255, in a colour picked by the item's
 * state. The colours are palette entries read from the running game (state -> index
 * `byte_141C813C4`, index -> RGB `dword_141D27238`).
 */
const CELL_WASH = {
  hovered: "rgba(24, 100, 8, 0.247)", // state 1, palette 118
  unusable: "rgba(144, 0, 0, 0.247)", // state 0, palette 8
  unidentified: "rgba(140, 64, 64, 0.247)", // state 4, palette 77
  normal: "rgba(24, 0, 104, 0.247)", // state 2, palette 234
} as const;

/**
 * Which wash an item gets. A grid paints every item — blue when nothing is wrong with it; a slot
 * paints only the exceptions, since an equipped item the wearer can use needs no marking.
 */
function washFor(
  item: DisplayItem,
  hovered: boolean,
  kind: "grid" | "slot",
): string | null {
  if (hovered) return CELL_WASH.hovered;
  if (item.usable === false) return CELL_WASH.unusable;
  if (item.identified === false) return CELL_WASH.unidentified;
  return kind === "grid" ? CELL_WASH.normal : null;
}

export function ItemCell({
  item,
  className,
  style,
  spriteClassName,
  spriteStyle,
  empty,
  cellKind = "grid",
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
  /** A grid cell washes every item; a slot only an item that needs marking, as the game does. */
  cellKind?: "grid" | "slot";
}) {
  const [hovered, setHovered] = useState(false);
  const { contextMenu, onContextMenu } = useItemContextMenu({ item });
  const wash = item ? washFor(item, hovered, cellKind) : null;

  return (
    <>
      <div
        // The wash is positioned against this box, which a grid already positions absolutely.
        className={clsx(
          className,
          !(className ?? "").split(" ").includes("absolute") && "relative",
        )}
        style={style}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onContextMenu={item ? onContextMenu : undefined}
      >
        {wash && (
          <div
            className="pointer-events-none absolute inset-0"
            style={{ backgroundColor: wash }}
          />
        )}
        {item ? (
          <ItemTooltip item={item} showSprite={false} open={hovered}>
            <div
              // Positioned, so it paints over the wash rather than under it.
              className={clsx(
                "relative flex items-center justify-center",
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
                hd={item.hd}
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
