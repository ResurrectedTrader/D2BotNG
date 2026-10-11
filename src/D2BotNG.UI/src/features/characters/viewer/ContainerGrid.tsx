/**
 * ContainerGrid - renders a D2 storage grid (inventory/stash/cube/belt) with
 * items absolutely positioned at their x/y and spanning width/height in cells.
 * Mod-agnostic: the grid dimensions come from the container itself.
 */

import { memo } from "react";
import { SpriteStyle } from "@/generated/settings_pb";
import { useSpriteStyle } from "@/lib/rendering";
import { useUiSprite } from "@/lib/rendering/hdUi";
import { ItemCell } from "./ItemCell";
import type { DisplayContainer } from "./contracts";

/** Pixels per inventory grid cell (matches the DC6 renderer's grid unit). */
const CELL = 29;

/**
 * The D2R art's cell pitch: the panels and the cell tile are generated at 30 pixels a cell, so a
 * grid drawn on the tile uses the same pitch as one drawn on a panel.
 */
const GAME_CELL = 30;

/**
 * The grid's surface: the wash behind the cells and the edge around them. Exported so the
 * readouts drawn beside a grid (the gold box) take the same paint from the same place.
 */
export const GRID_SURFACE_STYLE = {
  backgroundColor: "rgba(0, 0, 0, 0.4)",
  border: "1px solid rgba(255,255,255,0.12)",
} as const;

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

  // In the D2R art style a grid no panel draws (the belt, a 1.14d or PlugY stash page) is tiled
  // with a cell cut from the game's own grid, so it reads as the panels do.
  const gameCell = useUiSprite(
    useSpriteStyle() === SpriteStyle.D2R ? "cell" : undefined,
  );
  const cell = gameCell ? GAME_CELL : CELL;
  const surface = gameCell
    ? {
        width: cols * cell,
        height: rows * cell,
        backgroundImage: `url(${gameCell})`,
        backgroundSize: `${cell}px ${cell}px`,
      }
    : {
        width: cols * cell + 1,
        height: rows * cell + 1,
        ...GRID_SURFACE_STYLE,
        backgroundImage:
          "linear-gradient(to right, rgba(255,255,255,0.08) 1px, transparent 1px)," +
          "linear-gradient(to bottom, rgba(255,255,255,0.08) 1px, transparent 1px)",
        backgroundSize: `${cell}px ${cell}px`,
      };

  return (
    <div style={{ position: "relative", ...surface }}>
      {container.items.map((item, i) => {
        const w = Math.max(item.width, 1) * cell;
        const h = Math.max(item.height, 1) * cell;
        return (
          <ItemCell
            key={`${item.gid}-${item.x}-${item.y}-${i}`}
            item={item}
            className="absolute"
            style={{
              left: item.x * cell,
              top: item.y * cell,
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
