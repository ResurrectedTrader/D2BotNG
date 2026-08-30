/**
 * The standing hint that holding Ctrl over an item opens its breakdown.
 *
 * Pinned to the viewport rather than placed at the end of the page, because both views it appears
 * in scroll: a stash is several pages of grids and a search returns 48 rows, so a hint in the
 * document flow is off-screen at exactly the moment someone is hovering an item. It is also
 * `pointer-events-none`, so it can never take a click meant for what is underneath it, and it sits
 * below the tooltip layer on purpose — the tooltip it describes should cover it rather than fight
 * it for the corner.
 *
 * Only the v2 views render it. The breakdown is derived from the item's own stat lists against the
 * game tables, and only a capture carries those; on a v1 character, or a mule item, holding Ctrl
 * does nothing and saying otherwise would be worse than saying nothing.
 */
export function CtrlBreakdownHint() {
  return (
    <div className="pointer-events-none fixed bottom-3 right-4 z-40 select-none text-[11px] text-zinc-600">
      Hold{" "}
      <kbd className="rounded border border-zinc-700/60 bg-zinc-800/40 px-1 py-px font-sans text-[10px] text-zinc-500">
        Ctrl
      </kbd>{" "}
      over an item for its breakdown
    </div>
  );
}
