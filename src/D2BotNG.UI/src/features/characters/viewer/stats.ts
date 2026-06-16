/**
 * D2 stat ids surfaced in the character viewer, with display labels.
 * Ids are ItemStatCost ids (see kolbot sdk/txt/stats.txt — same data D2MOO uses).
 * Only ids present in a character's stats are shown; the engine sends a curated
 * set (see the character-state contract). Unrecognized ids show as "Stat <id>".
 */
export const STAT_LABELS: Record<number, string> = {
  0: "Strength",
  1: "Energy",
  2: "Dexterity",
  3: "Vitality",
  7: "Max Life",
  9: "Max Mana",
  12: "Level",
  13: "Experience",
  14: "Gold",
  15: "Stash Gold",
  19: "Attack Rating",
  31: "Defense",
  39: "Fire Resist",
  41: "Lightning Resist",
  43: "Cold Resist",
  45: "Poison Resist",
  79: "Gold Find",
  80: "Magic Find",
  93: "Increased Attack Speed",
  96: "Faster Run/Walk",
  99: "Faster Hit Recovery",
  102: "Faster Block Rate",
  105: "Faster Cast Rate",
  127: "All Skills",
};
