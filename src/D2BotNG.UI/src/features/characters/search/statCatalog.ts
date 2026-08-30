/**
 * The searchable-stat list, derived from the game's own tables.
 *
 * This is not a curated list of "stats we support". ItemStatCost.txt already says how every stat
 * is DESCRIBED — `descFunc` picks the phrasing, and for the layered ones it also says what the
 * layer means — so the catalogue is generated from it. Add a stat to the game and it appears
 * here; nothing to maintain.
 *
 * Modelled on ResurrectedTrade's search-stat-selector, which does the same derivation, with two
 * deliberate differences:
 *
 *  - **Groups come from the game.** RT hand-curates `BASE_STAT_GROUPS` for things like "All
 *    Resistances". ItemStatCost has `descGrp` for exactly that, and the toolkit exposes it, so
 *    the group membership and its wording are the game's rather than ours.
 *  - **An unknown descFunc is skipped, not thrown on.** RT throws, which turns one modded or
 *    newly added stat into a blank search tab.
 *
 * Two things every entry has to get right, because a wrong one silently returns wrong items:
 *
 *  - **The value scale.** Stats are stored RAW — pre-`valShift`, pre-op. So "+100 life" is
 *    `100 << 8`, and the mapping is per stat, which is why each term carries its own.
 *  - **The layer.** For class skills the layer IS the class; for a skill tab it is
 *    `(class << 3) + tab`; for chance-to-cast and charges it PACKS the skill and the level, with
 *    the level in the low `skillIdShift` bits. That packing is why those entries put the user's
 *    number on the layer instead of the value.
 */

import type { TooltipEngine } from "d2itemtoolkit";

/** A number as the user types it, mapped onto the raw value the capture stores. */
type StatScale = (display: number) => number;

/**
 * One stat source within an option: a set of stat ids that share a value scale, optionally
 * pinned to a layer.
 */
export interface StatTerm {
  /** OR-ed together. Only ever grouped here when they share a scale — the contract requires it. */
  statIds: number[];
  layer?: number;
  toRaw: StatScale;
}

export interface StatFilterOption {
  /** Stable identity, for React keys and for de-duplicating the list. */
  key: string;
  label: string;
  terms: StatTerm[];
  /**
   * Every term must match, rather than any one of them. True for a `descGrp` stat: "+15 to All
   * Resistances" means all four, not whichever happens to be highest.
   */
  requireAll: boolean;
  /**
   * Whether the stat's VALUE is worth bounding.
   *
   * False for a flag, whose presence is the whole mod (descFunc 12 — "Freezes Target"), and for
   * charges, whose value is a packed current/maximum pair rather than a magnitude.
   */
  valueBound: boolean;
  /**
   * Whether a SKILL LEVEL can be bounded, which is a separate number living in the layer.
   *
   * Chance-to-cast and charges pack the skill id and the level into one layer, level in the low
   * bits. Because the level is the low end of a contiguous field, bounding it is a layer RANGE —
   * which is also the only form that stays inside the (stat_id, layer, value) index; an AND-mask
   * would express the same thing and could not use it.
   *
   * Both bounds can apply at once, and on chance-to-cast they mean different things: the value is
   * the chance, the level is the level. ResurrectedTrade collapses these into one box and filters
   * the level only; keeping them apart costs one input and answers "20%+ chance of a level 10+
   * cast", which that cannot express.
   */
  levelBound: boolean;
  /**
   * The UPPER stat of a damage range, bounded independently by the row's second pair.
   *
   * The game prints "Adds 15-25 damage" as ONE line built from TWO stats (mindamage 21 and
   * maxdamage 22, and the same shape per element). Deriving the catalogue from ItemStatCost rows
   * gives each of those its own entry and no entry for the line a reader actually sees — so a
   * paired option is added for the merged form, with the first bound pair applying to the low
   * stat and this one to the high stat. Both must hold.
   */
  rangeMaxStatIds?: number[];
  /** Captions for the bound pairs, when an option exposes two and they mean different things. */
  firstLabel?: string;
  secondLabel?: string;
  /** How wide the packed level field is, when `levelBound`. */
  levelMask: number;
  /** ItemStatCost descPriority, so the list reads in the order a tooltip would print. */
  priority: number;
}

/**
 * What an option is unless it says otherwise: one plain, value-bounded modifier.
 *
 * Spread rather than written out at each site, so what a particular kind of option does differently
 * — a `descGrp` demanding all its members, a charges entry bounding a level instead of a value — is
 * the only thing visible there.
 */
const OPTION_DEFAULTS = {
  requireAll: false,
  valueBound: true,
  levelBound: false,
  levelMask: 0,
};

/** ItemStatCost descFunc values. Named after what they print; the numbers are the game's. */
const Func = {
  FleeFrames: 5,
  RepairDurability: 11,
  Flag: 12,
  ClassAllSkills: 13,
  SkillTab: 14,
  SkillOnEvent: 15,
  SkillAura: 16,
  ByTime: 17,
  ByTimePercent: 18,
  MonsterTypeDamage: 22,
  MonsterDamage: 23,
  Charges: 24,
  SkillClassOnly: 27,
  Skill: 28,
} as const;

/** The locale sentinel the string table returns for an unresolved key. */
export const MISSING_STRING = "an evil force";

const identity: StatScale = (n) => n;

/** `x << shift`. The default scale for every stat that has no special descFunc. */
function shiftScale(shift: number): StatScale {
  return shift === 0 ? identity : (display) => display << shift;
}

/**
 * The descFuncs whose stored number is not the one the game prints.
 *
 * Everything else is `valShift` and nothing more, which `shiftScale` covers; these are the rows
 * where the arithmetic is the description's rather than the stat's.
 */
const SPECIAL_SCALES: Record<number, StatScale> = {
  // "Hit Causes Monster to Flee" — stored in 128ths of a percent.
  [Func.FleeFrames]: (d) => Math.floor((d * 128) / 100),
  // "Repairs 1 Durability in N Seconds" — the stat holds repairs per 100 seconds, so the displayed
  // period is its RECIPROCAL.
  [Func.RepairDurability]: (d) => (d === 0 ? 0 : Math.floor(100 / d)),
  // Life/mana/stamina "by time" — stored in 128ths.
  [Func.ByTime]: (d) => d * 128,
};

/**
 * Substitutes the printf placeholders the string table uses (`%d`, `%+d`, `%i`, `%s`, and the
 * positional `%1`/`%2` forms) with the supplied arguments, left to right. `%%` is a literal
 * percent and consumes no argument.
 *
 * The game's own formatter is richer than this; the difference does not matter here because
 * these strings only ever become a LABEL, with "#" standing in for the number the user supplies.
 */
function format(template: string | null, ...args: string[]): string {
  if (!template) return "";
  let next = 0;
  // One pass, with `%%` in the same alternation as the placeholders. Two passes would need a
  // sentinel to stop the literal percents being re-scanned, and any sentinel is a character some
  // shipped string might legitimately contain.
  return template
    .replace(/%%|%[+\-#0-9]*[dis]|%[0-9]/g, (match) =>
      match === "%%" ? "%" : (args[next++] ?? ""),
    )
    .replace(/\s+/g, " ")
    .trim();
}

/** descFuncs whose line the game renders with a percent sign. */
const PERCENT_FUNCS = new Set([2, 4, 5, 7, 8, 10, 18, 20, 21]);

/**
 * Puts the value back into a label that has nowhere for it.
 *
 * Most ItemStatCost descriptions are only the NOUN — "to Dexterity", "Cold Resist" — because the
 * game prepends the sign and the number itself rather than substituting them. Left alone those
 * read as if they took no value, which is both confusing and the exact shape of a real flag mod.
 * The distinction matters: `valueBound` is decided by the descFunc, never by whether a "#" showed
 * up in the text.
 */
function withValuePlaceholder(text: string, descFunc: number): string {
  if (/#/.test(text)) return text;
  return PERCENT_FUNCS.has(descFunc) ? `#% ${text}` : `+# ${text}`;
}

interface Tables {
  statCost: TooltipEngine["data"]["itemStatCost"];
  string: (index: number) => string | null;
  classCount: number;
  classOnly: (cls: number) => string;
  allSkills: (cls: number) => string;
  skillTab: (cls: number, tab: 0 | 1 | 2) => string;
  skillName: (skill: number) => string | null;
  skillClass: (skill: number) => number;
  isAura: (skill: number) => boolean;
  hasItemEffect: (skill: number) => boolean;
  skillCount: number;
  skillIdShift: number;
}

function tablesOf(engine: TooltipEngine): Tables {
  const data = engine.data;
  const charStats = data.charStats;
  const skillRows = data.skillRows;
  const string = (index: number) => {
    const text = data.strings.getByIndex(index);
    return text && text !== MISSING_STRING ? text : null;
  };
  const keyed = (row: number, column: string) => {
    if (!charStats) return "";
    const key = charStats.getString(row, column);
    if (!key) return "";
    return string(data.strings.getIndexByKey(key)) ?? "";
  };
  return {
    statCost: data.itemStatCost,
    string,
    classCount: charStats?.rowCount ?? 0,
    classOnly: (cls) => keyed(cls, "strclassonly"),
    allSkills: (cls) => keyed(cls, "strallskills"),
    skillTab: (cls, tab) => keyed(cls, `strskilltab${tab + 1}`),
    skillName: (skill) => {
      const name = data.skills.getSkillName(skill);
      return name && name !== MISSING_STRING ? name : null;
    },
    skillClass: (skill) => data.skills.getSkillClass(skill),
    isAura: (skill) => skillRows?.getBool(skill, "aura") ?? false,
    hasItemEffect: (skill) =>
      (skillRows?.getInt(skill, "itemeffect") ?? 0) !== 0,
    skillCount: data.skills.rowCount,
    skillIdShift: data.itemStatCost.skillIdShift,
  };
}

/**
 * Expands one ItemStatCost row into the options it offers.
 *
 * A layered stat becomes MANY options — one per class, per skill tab, per castable skill — because
 * the layer is what the user is really choosing when they pick "+3 to Fire Skills". Returning one
 * option per row would leave the layer unspecified, which searches for "+3 to any skill tab".
 */
function optionsForStat(
  statId: number,
  t: Tables,
): Omit<StatFilterOption, "key">[] {
  const row = t.statCost.tryGetStat(statId);
  if (!row || row.descFunc === 0) return [];

  const scale = shiftScale(row.valShift);
  const priority = row.descPriority;
  // The game prints the negative wording when the value is below zero; either reads the same as a
  // label, and the positive one is missing on some rows.
  const desc = t.string(row.descStrPos) ?? t.string(row.descStrNeg);
  const suffix = t.string(row.descStr2);
  const decorate = (label: string) => (suffix ? `${label} ${suffix}` : label);

  const plain = (
    label: string,
    extra: Partial<Omit<StatFilterOption, "key" | "label">> = {},
    layer?: number,
    termScale: StatScale = scale,
  ): Omit<StatFilterOption, "key"> => {
    const valueBound = extra.valueBound ?? true;
    return {
      // Only a bounded mod gets a placeholder put back: writing "+# Freezes target" next to no
      // input box would promise a number the option does not take.
      label: decorate(
        valueBound ? withValuePlaceholder(label, row.descFunc) : label,
      ),
      terms: [{ statIds: [statId], layer, toRaw: termScale }],
      ...OPTION_DEFAULTS,
      valueBound,
      priority,
      ...extra,
    };
  };

  switch (row.descFunc) {
    // Fully unusable in practice: no shipped item carries either, and both need a monster table
    // to name what they apply to. RT skips them for the same reason.
    case Func.MonsterTypeDamage:
    case Func.MonsterDamage:
      return [];

    // One line with one number, and only the arithmetic behind it differs — see `SPECIAL_SCALES`.
    case Func.FleeFrames:
    case Func.RepairDurability:
    case Func.ByTime:
      return [
        plain(format(desc, "#"), {}, undefined, SPECIAL_SCALES[row.descFunc]),
      ];

    // A flag: the description IS the mod ("Freezes Target"), and its magnitude is not something
    // anyone searches on.
    case Func.Flag:
      return desc ? [plain(desc, { valueBound: false })] : [];

    // The layer is the class.
    case Func.ClassAllSkills:
      return range(t.classCount).flatMap((cls) => {
        const label = t.allSkills(cls);
        return label ? [plain(format(label, "#"), {}, cls)] : [];
      });

    // The layer packs the class and which of its three tabs.
    case Func.SkillTab:
      return range(t.classCount).flatMap((cls) =>
        ([0, 1, 2] as const).flatMap((tab) => {
          const label = t.skillTab(cls, tab);
          if (!label) return [];
          const only = t.classOnly(cls);
          return [
            plain(
              `${format(label, "#")}${only ? ` ${only}` : ""}`,
              {},
              (cls << 3) + tab,
            ),
          ];
        }),
      );

    // Chance-to-cast and charges: the layer is `skill << shift | level`. Only skills the game can
    // actually put on an item.
    case Func.SkillOnEvent:
    case Func.Charges: {
      const mask = (1 << t.skillIdShift) - 1;
      return range(t.skillCount).flatMap((skill) => {
        if (t.skillClass(skill) < 0) return [];
        if (row.descFunc === Func.SkillOnEvent && !t.hasItemEffect(skill)) {
          return [];
        }
        const name = t.skillName(skill);
        if (!name) return [];
        return [
          plain(
            // The charges description is only the tail — "(%d/%d Charges)" — because the game
            // prints the level and skill ahead of it from other fragments. Rebuilt here so the
            // label names the skill; the two counts are not searchable and show as X.
            row.descFunc === Func.Charges
              ? `Level # ${name} ${format(desc, "X", "X")}`
              : format(desc, "#", "#", name),
            {
              // A charges value is a packed current/maximum pair, so there is no magnitude to
              // bound — only the level, which lives in the layer. Chance-to-cast keeps both: its
              // value IS the chance.
              valueBound: row.descFunc === Func.SkillOnEvent,
              levelBound: true,
              levelMask: mask,
              firstLabel: "chance",
              secondLabel: "level",
            },
            skill << t.skillIdShift,
          ),
        ];
      });
    }

    // The layer is the skill, restricted to skills that can be an aura.
    case Func.SkillAura:
      return range(t.skillCount).flatMap((skill) => {
        if (!t.isAura(skill)) return [];
        const name = t.skillName(skill);
        return name ? [plain(format(desc, "#", name), {}, skill)] : [];
      });

    // The layer is the skill. 27 is class-restricted and names its class; 28 is an oskill, which
    // any class can use. Neither carries a description string of its own — the game builds the
    // line from its "+"/"to" fragments — so the label is assembled here.
    case Func.SkillClassOnly:
    case Func.Skill:
      return range(t.skillCount).flatMap((skill) => {
        const name = t.skillName(skill);
        if (!name) return [];
        const cls = t.skillClass(skill);
        if (row.descFunc === Func.SkillClassOnly) {
          if (cls < 0) return [];
          const only = t.classOnly(cls);
          return [plain(`+# to ${name}${only ? ` ${only}` : ""}`, {}, skill)];
        }
        return [plain(`+# to ${name}`, {}, skill)];
      });

    case Func.ByTimePercent:
      return desc ? [plain(format(desc, "#"))] : [];

    // Everything else is "<value> <string>" in some arrangement. The arrangement only affects the
    // wording, and the wording is the string itself.
    default:
      return desc ? [plain(format(desc, "#", "#"))] : [];
  }
}

/**
 * A `descGrp` stat becomes ONE option covering every member, using the group's own wording.
 *
 * The members do not necessarily share a value scale, so each is its own term. `requireAll` is
 * what makes it mean what the tooltip means: the game prints "All Resistances +15" only when all
 * four hold, so the filter demands all four.
 */
function optionForGroup(
  descGrp: number,
  t: Tables,
): Omit<StatFilterOption, "key"> | null {
  const members = t.statCost.getStatsInDescGroup(descGrp);
  if (members.length < 2) return null;
  const first = t.statCost.tryGetStat(members[0]);
  if (!first) return null;
  const desc =
    t.string(first.descGrpStrPos) ?? t.string(first.descGrpStrNeg) ?? null;
  if (!desc) return null;
  return {
    label: withValuePlaceholder(format(desc, "#", "#", "X"), first.descGrpFunc),
    terms: members.flatMap((statId) => {
      const row = t.statCost.tryGetStat(statId);
      return row
        ? [{ statIds: [statId], toRaw: shiftScale(row.valShift) }]
        : [];
    }),
    ...OPTION_DEFAULTS,
    requireAll: true,
    // The group has no priority of its own; it prints where its members would.
    priority: first.descPriority,
  };
}

function range(n: number): number[] {
  return Array.from({ length: n }, (_, i) => i);
}

/**
 * The damage RANGES the game prints as one line from two stats.
 *
 * `[low, high, stringId]`, with the string id being the game's own "Adds %d-%d …" wording so the
 * option reads exactly as the tooltip does. These are game constants — the library names the same
 * ids in `DamageStatIds` / `DamageStringIds` — and the pairing is what the description engine does
 * rather than anything ItemStatCost declares, which is why deriving from descFunc alone misses it.
 *
 * Poison is deliberately absent: its line folds in a DURATION (stat 59 over divisor 326) and its
 * stored values are per-frame, so "Adds 15-25 poison damage" is not two bounds over two stats and
 * would need its own arithmetic to mean anything.
 */
const DAMAGE_RANGES: readonly (readonly [number, number, number])[] = [
  [21, 22, 3623], // physical
  [48, 49, 3613], // fire
  [54, 55, 3615], // cold
  [50, 51, 3617], // lightning
  [52, 53, 3619], // magic
];

/**
 * One option per merged damage line, on top of the per-stat ones.
 *
 * The single-stat entries stay: "+# to Maximum Damage" is a real modifier a reader may want on its
 * own. This adds the form they actually see on the item.
 */
function damageRangeOptions(t: Tables): StatFilterOption[] {
  return DAMAGE_RANGES.flatMap(([low, high, stringId]) => {
    const wording = t.string(stringId);
    const lowRow = t.statCost.tryGetStat(low);
    const highRow = t.statCost.tryGetStat(high);
    if (!wording || !lowRow || !highRow) return [];

    const label = format(wording, "#", "#");
    return [
      {
        key: `range:${low}-${high}`,
        label,
        terms: [{ statIds: [low], toRaw: shiftScale(lowRow.valShift) }],
        rangeMaxStatIds: [high],
        firstLabel: "min",
        secondLabel: "max",
        ...OPTION_DEFAULTS,
        // Just above the pair's own lines, so the merged form is offered before its halves.
        priority: Math.max(lowRow.descPriority, highRow.descPriority) + 1,
      },
    ];
  });
}

/**
 * Every option, ordered the way a tooltip would print (highest descPriority first).
 *
 * Options that end up with the SAME label are merged rather than shown twice: several stat ids
 * genuinely share a description — the game prints one line for them — and a user picking that
 * line means any of them. Merging keeps them as separate terms, so the OR is a group of
 * conditions rather than a single condition pooling ids that may not share a scale.
 */
export function buildStatCatalog(engine: TooltipEngine): StatFilterOption[] {
  const t = tablesOf(engine);

  const raw: Omit<StatFilterOption, "key">[] = [];
  const groupsSeen = new Set<number>();
  for (let statId = 0; statId < t.statCost.rowCount; statId++) {
    const row = t.statCost.tryGetStat(statId);
    if (!row) continue;
    if (row.descGrp !== 0 && !groupsSeen.has(row.descGrp)) {
      groupsSeen.add(row.descGrp);
      const group = optionForGroup(row.descGrp, t);
      if (group) raw.push(group);
    }
    raw.push(...optionsForStat(statId, t));
  }

  const merged = new Map<string, StatFilterOption>();
  for (const option of raw) {
    if (!option.label) continue;
    // The SHAPE is part of the key, not just the wording. Two sources that print the same line
    // but are bounded differently — one a group demanding all its members, one a lone stat — are
    // not interchangeable, and pooling them would quietly demand stats the user never picked.
    const key = [
      option.label,
      option.requireAll ? "all" : "any",
      option.valueBound ? "v" : "-",
      option.levelBound ? "l" : "-",
    ].join("|");
    const existing = merged.get(key);
    if (!existing) {
      merged.set(key, { ...option, key });
      continue;
    }
    // Same wording AND same shape, so the sources are interchangeable: pool the terms and let
    // the OR find whichever one an item actually carries.
    existing.terms = [...existing.terms, ...option.terms];
    existing.priority = Math.max(existing.priority, option.priority);
    existing.levelMask = Math.max(existing.levelMask, option.levelMask);
  }

  // Appended rather than merged: a damage range has its own key and cannot collide with a
  // per-stat entry, since no ItemStatCost row prints the merged wording.
  return [...merged.values(), ...damageRangeOptions(t)].sort(
    (a, b) => b.priority - a.priority || a.label.localeCompare(b.label),
  );
}
