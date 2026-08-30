import { useEffect, useState } from "react";
import type { TooltipEngine } from "d2itemtoolkit";

import { loadToolkit, toolkitIfLoaded } from "@/lib/toolkit";

/**
 * The item tables, loaded on demand.
 *
 * Null until they arrive, which callers have to handle rather than block on: the tables are a
 * lazily imported chunk, so a v2 character can be selected before they are ready. The list and
 * every field that does not need them render immediately; the gear appears a beat later.
 *
 * `enabled` keeps a route that has no use for the tables from pulling them. It no longer separates
 * the two character schemas: a skill id and a monster id mean the same thing whichever stack
 * reported them, so `gameNames` asks for the tables on both — see the note there on why naming from
 * the game's own tables is worth the chunk. What `enabled` still buys is the pages that never name
 * anything, and the frames before a character is chosen.
 */
export function useToolkit(enabled: boolean): TooltipEngine | null {
  const [engine, setEngine] = useState<TooltipEngine | null>(toolkitIfLoaded);

  useEffect(() => {
    if (!enabled || engine) return;

    let cancelled = false;
    loadToolkit().then((loaded) => {
      if (!cancelled) setEngine(loaded);
    });

    return () => {
      cancelled = true;
    };
  }, [enabled, engine]);

  return engine;
}
