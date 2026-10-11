import { createContext, useContext } from "react";
import { SpriteStyle } from "@/generated/settings_pb";
import { useSettings } from "@/stores/event-store";

/**
 * An artwork style a subtree must draw in, whatever the app-wide preference says.
 *
 * A D2R character's items are D2R items: some have no classic art at all, and the ones that do
 * should look the way the game that owns them draws them. So the subtree showing one — a D2R
 * character's view, a search result that sits on one — sets this, and every `ItemSprite` under it
 * follows. Null (the default) leaves the preference in charge, which is right for 1.14d.
 *
 * A context rather than a prop for the reason `ItemSprite` reads the setting itself: the grids,
 * the paperdoll and the tooltip have no opinion about artwork, and threading it through them would
 * put the same prop in a dozen signatures. React carries context through portals, so a tooltip
 * opened from inside the subtree follows too.
 */
export const ForcedSpriteStyle = createContext<SpriteStyle | null>(null);

/** The style to draw in here: the subtree's forced one, else the app-wide preference. */
export function useSpriteStyle(): SpriteStyle {
  const forced = useContext(ForcedSpriteStyle);
  const preferred = useSettings()?.display?.spriteStyle ?? SpriteStyle.CLASSIC;
  return forced ?? preferred;
}
