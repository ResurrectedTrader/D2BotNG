/**
 * Search results, as rows rather than hover cards.
 *
 * Modelled on ResurrectedTrade's result list (which follows the Path of Exile trade site): a narrow
 * sprite column on the left with the item's provenance beneath it, and the item's own tooltip
 * beside it, centred at its natural width. A tooltip is taller than a sprite and narrower than a
 * page, so stretching it across the row left a band of empty space on every line and put the sprite
 * alone in a column three times its height.
 *
 * The text is the tooltip the GAME drew, rendered through the same component the hover tooltip
 * uses, so an item reads the same wherever it is looked at. Holding Ctrl over a row swaps it for
 * the breakdown — the socket contributions separated and every stat annotated with the span it
 * could have rolled within — which is the same gesture the character viewer has.
 */

import { memo, useCallback, useEffect, useMemo } from "react";
import type { TooltipEngine } from "d2itemtoolkit";
import type { ItemColumn, ItemMatch } from "@/generated/captures_pb";
import { Owner } from "@/generated/captures_pb";
import { ItemSprite } from "@/lib/rendering";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useHeldKey } from "@/hooks/useHeldKey";
import { useVirtualScroll } from "@/hooks/useVirtualScroll";
import clsx from "clsx";
import {
  isEthereal,
  useItemContextMenu,
  TooltipLine,
  useTooltipTextStyle,
} from "@/features/items";
import {
  damageKinds,
  renderLines,
  toDisplayItem,
  trimBlankRows,
  type RenderedLine,
} from "../viewer/capturedItem";
import { CONTAINER_LABELS } from "../viewer/contracts";
import {
  DAMAGE_COLUMN_BY_KIND,
  SORTABLE_SECTIONS,
  type SortChoice,
} from "./searchRequest";

/**
 * The set block, which a result has no business showing.
 *
 * All of it is about a WEARER, and a search result has none — the item may sit on a mule of any
 * class. The piece list renders every sibling in "not owned" red, which is a claim about a
 * character nobody named; the bonus blocks are the tiers that character has earned; and the set's
 * NAME is the heading for those two, so with them gone it labels nothing. The piece's own name
 * already says which set it belongs to. The character viewer, which does have a wearer, keeps the
 * whole block.
 */
const WEARER_SECTIONS = new Set([
  "SetName",
  "SetPieceList",
  "PartialSetBonus",
  "FullSetBonus",
]);

/**
 * The tooltip without its set block, re-trimmed.
 *
 * Trimming again matters: the game separates the block with a blank row, and dropping the block
 * leaves that blank as the last row — a gap at the bottom of every set item's card that reads as a
 * rendering fault.
 */
function withoutSetBlock(lines: RenderedLine[]): RenderedLine[] {
  return trimBlankRows(lines.filter((l) => !WEARER_SECTIONS.has(l.section)));
}

function locationOf(match: ItemMatch): string {
  const container = CONTAINER_LABELS[match.container] ?? match.container;
  const page = match.container === "stash" ? ` ${match.page + 1}` : "";
  const owner = match.owner === Owner.MERC ? " · Mercenary" : "";
  return `${container}${page}${owner}`;
}

/**
 * What makes two sort keys the same modifier. The stat SET, not the first of it — an
 * all-attributes line and a plain strength line both lead with stat 0 and are not the same thing to
 * rank by. The layer is part of it in full, zero included: 0 is the Amazon and her Bow tab, so
 * folding it in with "no layer" made every class's +skills line read as the ranked one.
 */
function lineKey(statIds: readonly number[], layer: number): string {
  return `${statIds.join(",")}:${layer}`;
}

/**
 * One result. Memoised, because nothing about a row depends on the page it sits in.
 *
 * The page re-renders on every `CaptureChanged` from any running profile — the character list on
 * the sibling tab rides the same store — and each row is a sprite plus ~20 tooltip lines, so a
 * background report was re-reconciling a thousand elements that could not have changed. Its props
 * are stable by construction: the match objects come from the query cache and the callback below
 * is held across renders.
 */
const ResultRow = memo(function ResultRow({
  match,
  engine,
  sort,
  onSortByLine,
  ctrlHeld,
}: {
  match: ItemMatch;
  engine: TooltipEngine;
  sort: SortChoice;
  onSortByLine: (line: RenderedLine, column?: ItemColumn) => void;
  ctrlHeld: boolean;
}) {
  const textStyle = useTooltipTextStyle();

  const unit = match.item!;
  // Derived here rather than by the list, because `ctrlHeld` is the LIST's state: built up there,
  // every press and release rebuilt all 48, each costing an appearance lookup for the item and one
  // per socket, to produce exactly what was already on screen.
  const item = useMemo(() => toDisplayItem(unit, engine), [unit, engine]);
  const { contextMenu, onContextMenu } = useItemContextMenu({ item });

  const plain = useMemo(
    // No wearer: a result may sit on a mule of any class, so there is no viewer whose requirements
    // would be the right ones to colour against.
    () => withoutSetBlock(renderLines(unit, engine, {})),
    [unit, engine],
  );

  /**
   * The breakdown, for every row at once while Ctrl is down — not just the one under the pointer.
   *
   * This list is the one place a reader has several items side by side, and the breakdown's whole
   * content is comparative: what each stat COULD have rolled, and at what item level. Revealing it
   * a row at a time answers "how good is this one" and never "which of these is better", which is
   * what a search result page is being read for. Hovering also made the gesture two-handed — hold
   * the key, keep the pointer still — where the key alone is a mode.
   *
   * The cost was the reason it was hover-gated, so it was measured rather than assumed: a
   * breakdown is ~3.5ms an item against ~1.5ms for the plain text every row already renders on
   * mount, so a full page of 48 is a single ~200ms pass on the first press. Cached below, so
   * further presses are free.
   *
   * Held in a closure that keeps its rows rather than a memo on `ctrlHeld`, which would be thrown
   * away on every release — and this is a key the reader taps repeatedly to compare. (`DisplayItem
   * .detail` caches too, but its rows are the renderer's narrow shape: no `section` to drop the set
   * block by, and no `statIds` to sort on.)
   */
  const detailLines = useMemo(() => {
    let rows: RenderedLine[] | null = null;
    return () =>
      (rows ??= withoutSetBlock(
        renderLines(unit, engine, {}, { breakdown: true }),
      ));
  }, [unit, engine]);

  const detail = ctrlHeld ? detailLines() : null;

  const lines = detail ?? plain;

  /**
   * The sort column each line offers, or undefined for the ones that rank by stat (or not at all).
   *
   * Damage rows are resolved positionally: they all carry the same section and no stat id, so the
   * only thing that says which is the one-hand line and which the throw line is their ORDER, which
   * the library guarantees matches `damage().lines`.
   */
  const columns = useMemo(() => {
    const kinds = damageKinds(unit, engine);
    let seen = 0;
    return lines.map((line) => {
      if (line.section === "WeaponDamage") {
        return DAMAGE_COLUMN_BY_KIND[kinds[seen++] ?? ""];
      }
      return SORTABLE_SECTIONS[line.section]?.column;
    });
  }, [lines, unit, engine]);

  const sortedKey =
    sort.key.kind === "line" ? lineKey(sort.key.statIds, sort.key.layer) : null;

  return (
    // Three columns rather than a flex row with a spacer element: the third column is empty, so
    // the tooltip's centre is the row's centre, and `minmax(0, 1fr)` lets the middle column shrink
    // instead of pushing the row wider than the card. A spacer DIV did the same centring but added
    // its width to the row, and `main` is `overflow-y-auto` — which makes overflow-x compute to
    // auto — so on a narrow window that bought a horizontal scrollbar for the whole page.
    <div
      className="grid grid-cols-[10rem_minmax(0,1fr)_10rem] items-center gap-4 rounded-lg bg-zinc-900/30 p-3 ring-1 ring-zinc-800 transition-colors hover:bg-zinc-900/60 hover:ring-zinc-700"
      onContextMenu={onContextMenu}
    >
      {/* The sprite, with where the item lives underneath it — a tooltip is taller than a sprite,
          so that column has the room and the two belong together anyway.

          Fixed width and FIRST in the row, so it sits at the same place on every result: the
          tooltip is as wide as its longest line, so anything that let the sprite's position depend
          on it would make the column drift from row to row. Centred against the tooltip's height
          rather than pinned to its top, since it is the shorter of the two.

          The frame is 8rem tall because an inventory cell is 28px and the tallest item is four of
          them — a two-handed sword filled the old 6rem box edge to edge. */}
      <div className="space-y-2">
        <div className="flex h-32 items-center justify-center rounded bg-zinc-950/50 ring-1 ring-zinc-800">
          <ItemSprite
            code={item.code}
            colorShift={item.itemColor}
            invTrans={item.invTrans}
            ethereal={isEthereal(item)}
            sockets={item.sockets}
            hd={item.hd}
            alt={item.name}
          />
        </div>
        {/* Provenance, as a small stack of labelled facts rather than three loose lines. Wrapped
            rather than truncated: a profile name is how the reader knows WHICH character this came
            from, so an ellipsis hides the part that distinguishes "Sorc-mule-03" from
            "Sorc-mule-08". */}
        <dl className="space-y-1 text-center text-[11px] leading-tight">
          <div>
            <dt className="sr-only">Character</dt>
            <dd className="break-words font-medium text-zinc-300">
              {match.profile}
            </dd>
          </div>
          <div>
            <dt className="sr-only">Location</dt>
            <dd className="break-words text-zinc-500">{locationOf(match)}</dd>
          </div>
          {/* No item level: it rides the held-Ctrl breakdown, beside the roll spans it explains. */}
        </dl>
      </div>

      {/* Centred on the ROW, not on what is left of it — which is what the empty column after this
          one buys: it matches the sprite column's width, so the space either side of the tooltip is
          equal and the panel sits on the row's own centre line. Without it every tooltip was pushed
          right by half the sprite column. */}
      <div className="flex min-w-0 justify-center">
        <div
          className="w-fit rounded bg-zinc-950/50 px-4 py-2 text-sm ring-1 ring-zinc-800"
          style={textStyle}
        >
          {lines.map((line, i) => {
            // Two axes, one gesture: a modifier ranks by its stat, and a requirement or a damage
            // line by the item column that holds it — resolved above, since which column a damage
            // row means is a question of which line it is.
            const column = columns[i];
            const sortable = line.statIds.length > 0 || column !== undefined;
            const active =
              column !== undefined
                ? sort.key.kind === "column" && sort.key.column === column
                : line.statIds.length > 0 &&
                  sortedKey === lineKey(line.statIds, line.layer);
            return (
              <TooltipLine
                key={i}
                segments={line.segments}
                onClick={
                  sortable ? () => onSortByLine(line, column) : undefined
                }
                title={sortable ? "Sort by this" : undefined}
                className={clsx(
                  "whitespace-nowrap",
                  sortable && "cursor-pointer",
                  // No underline: it moves the baseline and, on a page of results, striped every
                  // ranked row. An arrow in the margin says the same thing and disturbs nothing.
                  active && "relative",
                )}
              >
                {active && (
                  <span className="absolute -left-3 text-[10px] text-d2-gold">
                    {sort.descending ? "▼" : "▲"}
                  </span>
                )}
              </TooltipLine>
            );
          })}
        </div>
      </div>

      {contextMenu}
    </div>
  );
});

export function SearchResults({
  matches,
  engine,
  sort,
  onSort,
}: {
  matches: ItemMatch[];
  /** Null while the tables load; the sprite and colour cannot be resolved without them. */
  engine: TooltipEngine | null;
  sort: SortChoice;
  onSort: (sort: SortChoice) => void;
}) {
  // One listener for the page rather than one per row, and it applies to all of them: the
  // breakdown is comparative, so it is a mode over the list rather than a detail of one row.
  const ctrlHeld = useHeldKey("Control");

  const { parentRef, scrollElement, scrollMargin, width } =
    useVirtualScroll<HTMLDivElement>();

  /**
   * Only the results with an item, densely, because a virtualizer indexes by position: a null in
   * the middle would leave a measured row of nothing rather than be skipped.
   */
  const rows = useMemo(() => matches.filter((m) => m.item), [matches]);

  /**
   * Windowed, because this list has no ceiling and two costs that scale with its length.
   *
   * Results are paged in as the reader scrolls and nothing ever leaves, so a manager with a few
   * hundred profiles can accumulate thousands of rows in the DOM. Each one renders its tooltip from
   * the game tables — about 1.5ms — and holding Ctrl re-renders every loaded row as a breakdown at
   * about 3.5ms, which is a keypress that blocks for seconds once enough has been scrolled past.
   * Windowed, both are bounded by what fits on screen instead.
   *
   * Rows are measured rather than estimated: their height is their content, and a result may be
   * three lines or thirty. `estimateSize` is only what an unseen row is assumed to be until it has
   * been on screen once — near enough that the scrollbar does not lurch as they resolve.
   *
   * This also brings the scroll anchoring the Ctrl mode needs. When a measured row's height
   * changes, the virtualizer shifts the scroll position by the delta if that row began above the
   * viewport, which is exactly "keep what the reader is looking at where it is"; and the rows
   * further up are not rendered at all, so their heights cannot change to push anything down.
   */
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollElement,
    estimateSize: () => 220,
    overscan: 4,
    scrollMargin,
  });

  /**
   * Throw the measurements away when the list changes width.
   *
   * A row's height is decided by its width — the tooltip wraps — and the virtualizer only ever
   * re-measures the handful of rows it is rendering. Every other cached height silently becomes
   * wrong, and since those heights are what the scroll offsets are computed from, the list ends up
   * positioned against a total that no longer describes it: scrolling lands somewhere other than
   * where it was aimed, and jumping to the top can arrive at a stretch of nothing.
   *
   * Browser zoom is what makes this a real case rather than a theoretical one. It changes how many
   * CSS pixels the viewport is, so the whole page reflows without a single element resizing itself
   * — and Ctrl+scroll over a long list is an easy way to end up there. Resetting costs a re-measure
   * of what is on screen, against a jump the reader has just asked for anyway.
   */
  useEffect(() => {
    if (width > 0) virtualizer.measure();
  }, [width, virtualizer]);

  /**
   * Clicking the line already ranked by reverses it, the way a column header does.
   *
   * Held across renders so the memoised rows below stay memoised: a fresh closure here would be a
   * changed prop on all 48 of them every time the page re-rendered.
   */
  const sortByLine = useCallback(
    (line: RenderedLine, column?: ItemColumn) => {
      if (column !== undefined) {
        if (sort.key.kind === "column" && sort.key.column === column) {
          return onSort({ ...sort, descending: !sort.descending });
        }
        // Ascending for a requirement, because its useful end is the LOW one — the question is
        // which of these a character can wear. Descending for everything else, where more is
        // better.
        return onSort({
          key: { kind: "column", column, label: line.text },
          descending: SORTABLE_SECTIONS[line.section]?.descending ?? true,
        });
      }

      const same =
        sort.key.kind === "line" &&
        lineKey(sort.key.statIds, sort.key.layer) ===
          lineKey(line.statIds, line.layer);
      if (same) return onSort({ ...sort, descending: !sort.descending });
      onSort({
        key: {
          kind: "line",
          statIds: line.statIds,
          // As rendered, zero included: the store's layer column is NOT NULL DEFAULT 0, so 0 is
          // what an unlayered stat's rows carry — and it is also the Amazon, whose +skills would
          // otherwise rank alongside every other class's.
          layer: line.layer,
          label: line.text,
        },
        descending: true,
      });
    },
    [sort, onSort],
  );

  if (!engine) {
    return <p className="text-sm text-zinc-500">Loading the item tables…</p>;
  }

  return (
    <div ref={parentRef}>
      <div
        style={{
          height: virtualizer.getTotalSize(),
          position: "relative",
          width: "100%",
        }}
      >
        {virtualizer.getVirtualItems().map((virtual) => {
          const match = rows[virtual.index];
          const item = match.item!;
          return (
            <div
              // gid is unique only within one game, so it takes the profile and slot to be a key.
              key={`${match.profile}-${match.container}-${match.page}-${item.gid}-${item.x}-${item.y}`}
              data-index={virtual.index}
              ref={virtualizer.measureElement}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${virtual.start - scrollMargin}px)`,
              }}
            >
              {/* The gap belongs INSIDE what is measured. As margin it would collapse against
                  nothing — every row is its own absolutely positioned box — so the rows would
                  render flush and each measurement would be short by the gap. */}
              <div className="pb-2">
                <ResultRow
                  match={match}
                  engine={engine}
                  sort={sort}
                  onSortByLine={sortByLine}
                  ctrlHeld={ctrlHeld}
                />
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
