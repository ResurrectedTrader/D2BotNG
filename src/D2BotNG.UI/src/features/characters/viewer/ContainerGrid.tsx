/**
 * ContainerGrid - renders a D2 storage grid (inventory/stash/cube/belt) with
 * items absolutely positioned at their x/y and spanning width/height in cells.
 * Mod-agnostic: the grid dimensions come from the container itself.
 */

import { memo } from "react";
import { ItemCell } from "./ItemCell";
import type { DisplayContainer } from "./contracts";

/** Pixels per inventory grid cell (matches the DC6 renderer's grid unit). */
const CELL = 29;

/**
 * Memoised on the container, which is the whole grid's worth of work: a page can hold a few hundred
 * items and the viewer re-renders on every capture change from any profile. The container object is
 * rebuilt only when that character's own capture is, so a grid is reconciled when its contents
 * actually moved and not when a bot three profiles away picked up gold.
 */
export const ContainerGrid = memo(function ContainerGrid({
  container,
}: {
  container: DisplayContainer;
}) {
  const cols = Math.max(container.width, 1);
  const rows = Math.max(container.height, 1);

  return (
    <div
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
      {container.items.map((item, i) => {
        const w = Math.max(item.width, 1) * CELL;
        const h = Math.max(item.height, 1) * CELL;
        return (
          <ItemCell
            key={`${item.gid}-${item.x}-${item.y}-${i}`}
            item={item}
            className="absolute"
            style={{
              left: item.x * CELL,
              top: item.y * CELL,
              width: w,
              height: h,
            }}
            spriteStyle={{ width: w, height: h }}
          />
        );
      })}
    </div>
  );
});
