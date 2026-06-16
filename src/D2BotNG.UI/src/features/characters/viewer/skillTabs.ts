/**
 * Skill-tree tabs per character class: skill ids grouped by their in-game skill
 * page (tab), in tab display order. Ids are the raw D2 skill ids (see data/skillNames.ts).
 * Used to group a character's invested skills the way the in-game skill screen does.
 */

export interface SkillTab {
  name: string;
  skillIds: number[];
}

// Keyed by D2 class id (0 Amazon … 6 Assassin).
export const CLASS_SKILL_TABS: Record<number, SkillTab[]> = {
  // Amazon (6-35)
  0: [
    {
      name: "Javelin & Spear",
      skillIds: [10, 14, 15, 19, 20, 24, 25, 30, 34, 35],
    },
    {
      name: "Passive & Magic",
      skillIds: [8, 9, 13, 17, 18, 23, 28, 29, 32, 33],
    },
    {
      name: "Bow & Crossbow",
      skillIds: [6, 7, 11, 12, 16, 21, 22, 26, 27, 31],
    },
  ],
  // Sorceress (36-65)
  1: [
    { name: "Cold", skillIds: [39, 40, 44, 45, 50, 55, 59, 60, 64, 65] },
    { name: "Lightning", skillIds: [38, 42, 43, 48, 49, 53, 54, 57, 58, 63] },
    { name: "Fire", skillIds: [36, 37, 41, 46, 47, 51, 52, 56, 61, 62] },
  ],
  // Necromancer (66-95)
  2: [
    { name: "Summoning", skillIds: [69, 70, 75, 79, 80, 85, 89, 90, 94, 95] },
    {
      name: "Poison & Bone",
      skillIds: [67, 68, 73, 74, 78, 83, 84, 88, 92, 93],
    },
    { name: "Curses", skillIds: [66, 71, 72, 76, 77, 81, 82, 86, 87, 91] },
  ],
  // Paladin (96-125)
  3: [
    {
      name: "Combat",
      skillIds: [96, 97, 101, 106, 107, 111, 112, 116, 117, 121],
    },
    {
      name: "Offensive Auras",
      skillIds: [98, 102, 103, 108, 113, 114, 118, 120, 122, 123],
    },
    {
      name: "Defensive Auras",
      skillIds: [99, 100, 104, 105, 109, 110, 115, 119, 124, 125],
    },
  ],
  // Barbarian (126-155)
  4: [
    {
      name: "Warcries",
      skillIds: [130, 131, 137, 138, 142, 146, 149, 150, 154, 155],
    },
    {
      name: "Combat Masteries",
      skillIds: [127, 128, 129, 134, 135, 136, 141, 145, 148, 153],
    },
    {
      name: "Combat Skills",
      skillIds: [126, 132, 133, 139, 140, 143, 144, 147, 151, 152],
    },
  ],
  // Druid (221-250)
  5: [
    {
      name: "Elemental",
      skillIds: [225, 229, 230, 234, 235, 240, 244, 245, 249, 250],
    },
    {
      name: "Shape Shifting",
      skillIds: [223, 224, 228, 232, 233, 238, 239, 242, 243, 248],
    },
    {
      name: "Summoning",
      skillIds: [221, 222, 226, 227, 231, 236, 237, 241, 246, 247],
    },
  ],
  // Assassin (251-280)
  6: [
    {
      name: "Martial Arts",
      skillIds: [254, 255, 259, 260, 265, 269, 270, 274, 275, 280],
    },
    {
      name: "Shadow Disciplines",
      skillIds: [252, 253, 258, 263, 264, 267, 268, 273, 278, 279],
    },
    {
      name: "Traps",
      skillIds: [251, 256, 257, 261, 262, 266, 271, 272, 276, 277],
    },
  ],
};
