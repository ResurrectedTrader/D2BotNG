import type { TooltipEngine } from "d2itemtoolkit";

/**
 * The game's own item tables, for the things a capture cannot answer about itself: which sprite
 * an item draws with, what palette shift it takes, and the tooltip the game would have written.
 *
 * Loaded through a dynamic import, and that is not incidental. The package embeds the shipped
 * tables as a ~735KB source blob so it needs no game install, which is exactly what we want and
 * exactly what must not sit in the initial chunk. This keeps it in a lazy chunk that only the
 * characters route pulls, and only once a v2 character is actually selected.
 *
 * Building the tables is the expensive part, so the engine is cached after the first load — the
 * library says as much about its own construction, and it is immutable once built.
 */
let engine: TooltipEngine | null = null;
let loading: Promise<TooltipEngine> | null = null;
let packedStat: ((statId: number) => boolean) | null = null;

/**
 * The engine, loading the tables on first use. Concurrent callers share one import: several item
 * tiles mounting at once is the normal case, and each awaiting its own copy would parse the
 * tables several times over.
 */
export function loadToolkit(): Promise<TooltipEngine> {
  if (engine) return Promise.resolve(engine);

  loading ??= import("d2itemtoolkit").then((module) => {
    engine = module.TooltipEngine.embedded;
    packedStat = module.isPackedStat;
    return engine;
  });

  return loading;
}

/** The engine if it is already loaded, else null. For render paths that cannot await. */
export function toolkitIfLoaded(): TooltipEngine | null {
  return engine;
}

/**
 * Whether a stat's value is a packed encoding — charges, the by-time triples — rather than a
 * magnitude that can be summed or compared against a bound.
 *
 * Taken from the library rather than re-derived from `descFunc` here, because the library declares
 * itself the owner of that rule and it is the same rule its merged totals are built on: a packed
 * stat is left OUT of them, so a merged search for one matches nothing at all. Reached through the
 * lazy handle for the usual reason — a value import of the package would drag its ~735KB of tables
 * into the initial chunk. Every caller runs from the search tab, which cannot render an option or
 * a result line before the tables have loaded.
 */
export function isPackedStat(statId: number): boolean {
  return packedStat?.(statId) ?? false;
}
