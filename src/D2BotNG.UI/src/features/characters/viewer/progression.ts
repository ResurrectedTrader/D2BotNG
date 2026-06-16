/**
 * D2 quest + waypoint id → name tables, matching the ids the d2bs sender emits:
 * raw game quest-array slots (0..40, sparse) and contiguous waypoint indices (0..38).
 *
 * Quest ids are from kolbot's `sdk.quests.id`; the display order within each act
 * matches the in-game quest log (per the d2s-editor reference), which is NOT id
 * order (e.g. Act 3 starts with The Golden Bird). The sender reports every
 * completed slot, including travel/intro flags (SpokeToWarriv=0, AbleToGotoActII=7,
 * SpokeToJerhyn=8, AbleToGotoActIII=15, SpokeToHratli=16, AbleToGotoActIV=23,
 * SpokeToTyrael=24, AbleToGotoActV=28) and padding (29-34); those are omitted so
 * only the 27 real quests show.
 *
 * Waypoints are the 39 standard waypoints in waypoint-menu order (town first per
 * act), the contiguous index the sender's HasWaypoint(0..38) walks.
 */

export interface Act<T> {
  act: number;
  entries: T[];
}

export interface NamedId {
  id: number;
  name: string;
}

export const QUEST_ACTS: Act<NamedId>[] = [
  {
    act: 1,
    entries: [
      { id: 1, name: "Den of Evil" },
      { id: 2, name: "Sisters' Burial Grounds" },
      { id: 4, name: "The Search for Cain" },
      { id: 5, name: "The Forgotten Tower" },
      { id: 3, name: "Tools of the Trade" },
      { id: 6, name: "Sisters to the Slaughter" },
    ],
  },
  {
    act: 2,
    entries: [
      { id: 9, name: "Radament's Lair" },
      { id: 10, name: "The Horadric Staff" },
      { id: 11, name: "The Tainted Sun" },
      { id: 12, name: "The Arcane Sanctuary" },
      { id: 13, name: "The Summoner" },
      { id: 14, name: "The Seven Tombs" },
    ],
  },
  {
    act: 3,
    entries: [
      { id: 20, name: "The Golden Bird" },
      { id: 19, name: "Blade of the Old Religion" },
      { id: 18, name: "Khalim's Will" },
      { id: 17, name: "Lam Esen's Tome" },
      { id: 21, name: "The Blackened Temple" },
      { id: 22, name: "The Guardian" },
    ],
  },
  {
    act: 4,
    entries: [
      { id: 25, name: "The Fallen Angel" },
      { id: 27, name: "Hell's Forge" },
      { id: 26, name: "Terror's End" },
    ],
  },
  {
    act: 5,
    entries: [
      { id: 35, name: "Siege on Harrogath" },
      { id: 36, name: "Rescue on Mount Arreat" },
      { id: 37, name: "Prison of Ice" },
      { id: 38, name: "Betrayal of Harrogath" },
      { id: 39, name: "Rite of Passage" },
      { id: 40, name: "Eve of Destruction" },
    ],
  },
];

export const WAYPOINT_ACTS: Act<NamedId>[] = [
  {
    act: 1,
    entries: [
      { id: 0, name: "Rogue Encampment" },
      { id: 1, name: "Cold Plains" },
      { id: 2, name: "Stony Field" },
      { id: 3, name: "Dark Wood" },
      { id: 4, name: "Black Marsh" },
      { id: 5, name: "Outer Cloister" },
      { id: 6, name: "Jail Level 1" },
      { id: 7, name: "Inner Cloister" },
      { id: 8, name: "Catacombs Level 2" },
    ],
  },
  {
    act: 2,
    entries: [
      { id: 9, name: "Lut Gholein" },
      { id: 10, name: "Sewers Level 2" },
      { id: 11, name: "Dry Hills" },
      { id: 12, name: "Halls of the Dead Level 2" },
      { id: 13, name: "Far Oasis" },
      { id: 14, name: "Lost City" },
      { id: 15, name: "Palace Cellar Level 1" },
      { id: 16, name: "Arcane Sanctuary" },
      { id: 17, name: "Canyon of the Magi" },
    ],
  },
  {
    act: 3,
    entries: [
      { id: 18, name: "Kurast Docks" },
      { id: 19, name: "Spider Forest" },
      { id: 20, name: "Great Marsh" },
      { id: 21, name: "Flayer Jungle" },
      { id: 22, name: "Lower Kurast" },
      { id: 23, name: "Kurast Bazaar" },
      { id: 24, name: "Upper Kurast" },
      { id: 25, name: "Travincal" },
      { id: 26, name: "Durance of Hate Level 2" },
    ],
  },
  {
    act: 4,
    entries: [
      { id: 27, name: "The Pandemonium Fortress" },
      { id: 28, name: "City of the Damned" },
      { id: 29, name: "River of Flame" },
    ],
  },
  {
    act: 5,
    entries: [
      { id: 30, name: "Harrogath" },
      { id: 31, name: "Frigid Highlands" },
      { id: 32, name: "Arreat Plateau" },
      { id: 33, name: "Crystalline Passage" },
      // NB: the sender's waypoint bit order puts Glacial Trail before Halls of
      // Pain here (matches the in-game menu), opposite to some save editors.
      { id: 34, name: "Glacial Trail" },
      { id: 35, name: "Halls of Pain" },
      { id: 36, name: "Frozen Tundra" },
      { id: 37, name: "The Ancients' Way" },
      { id: 38, name: "Worldstone Keep Level 2" },
    ],
  },
];
