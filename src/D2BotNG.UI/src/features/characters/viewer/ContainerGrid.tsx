/**
 * ContainerGrid - renders a D2 storage grid (inventory/stash/cube/belt) with
 * items absolutely positioned at their x/y and spanning width/height in cells.
 * Mod-agnostic: the grid dimensions come from the container itself.
 * Sockets are drawn on an item's sprite only while it's hovered.
 */

import { useState } from "react";
import type { Container } from "@/generated/characters_pb";
import type { Item } from "@/generated/items_pb";
import { ItemSprite } from "@/lib/rendering";
import { ItemTooltip, isEthereal, useItemContextMenu } from "@/features/items";

/** Pixels per inventory grid cell (matches the DC6 renderer's grid unit). */
const CELL = 29;

interface ContainerGridProps {
  container: Container;
  className?: string;
}

function GridItem({ item }: { item: Item }) {
  const [hovered, setHovered] = useState(false);
  const { contextMenu, onContextMenu } = useItemContextMenu({ item });
  const w = Math.max(item.width, 1) * CELL;
  const h = Math.max(item.height, 1) * CELL;
  return (
    <div
      className="absolute"
      style={{ left: item.x * CELL, top: item.y * CELL, width: w, height: h }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onContextMenu={onContextMenu}
    >
      <ItemTooltip item={item} showSprite={false}>
        <div
          className="flex items-center justify-center"
          style={{ width: w, height: h }}
        >
          <ItemSprite
            code={item.code}
            colorShift={item.itemColor}
            invTrans={item.invTrans}
            ethereal={isEthereal(item)}
            sockets={hovered ? item.sockets : undefined}
            alt={item.name}
          />
        </div>
      </ItemTooltip>
      {contextMenu}
    </div>
  );
}

export function ContainerGrid({ container, className }: ContainerGridProps) {
  const cols = Math.max(container.width, 1);
  const rows = Math.max(container.height, 1);

  return (
    <div
      className={className}
      style={{
        position: "relative",
        width: cols * CELL + 1,
        height: rows * CELL + 1,
        backgroundColor: "rgba(0, 0, 0, 0.4)",
        backgroundImage:
          "linear-gradient(to right, rgba(255,255,255,0.08) 1px, transparent 1px)," +
          "linear-gradient(to bottom, rgba(255,255,255,0.08) 1px, transparent 1px)",
        backgroundSize: `${CELL}px ${CELL}px`,
        border: "1px solid rgba(255,255,255,0.12)",
      }}
    >
      {container.items.map((item, i) => (
        <GridItem key={`${item.gid}-${item.x}-${item.y}-${i}`} item={item} />
      ))}
    </div>
  );
}
