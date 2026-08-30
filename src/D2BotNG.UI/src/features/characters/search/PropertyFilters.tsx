/**
 * The non-stat half of the filter: what the item IS and where it sits.
 *
 * The axes here are genuinely distinct and are a documented trap, so they are labelled to say
 * which is which — an ITEM (a base item, a unique, a set piece or a runeword, all from one list),
 * a TYPE (one ItemTypes.txt row matched through its descendants, "any sword"), and a TIER (normal
 * / exceptional / elite, orthogonal to quality).
 *
 * Every field is the same shape — a short caption over a compact control — because they are all
 * the same kind of question. The controls come from `./controls` rather than the app's settings-page
 * `Input`/`Select`, which are sized for one field per row.
 */

import { useMemo } from "react";
import type { TooltipEngine } from "d2itemtoolkit";
import { ItemSprite } from "@/lib/rendering";
import { colorForQuality } from "@/features/items/item-utils";
import { SocketScope, Tier } from "@/generated/captures_pb";
import {
  IDENTIFIED_FLAG,
  RUNEWORD_FLAG,
  SOCKETED_FLAG,
  ETHEREAL_FLAG,
} from "../viewer/capturedItem";
import {
  identityFieldSpan,
  type PropertyFilters as Filters,
  type SpecificPick,
} from "./searchRequest";
import { MISSING_STRING } from "./statCatalog";
import { Field, Group, MultiSelect, RangeBox, SingleSelect } from "./controls";

interface NamedId {
  id: number;
  name: string;
}

/** Quality ids, as the capture stores them. */
const QUALITIES: NamedId[] = [
  { id: 1, name: "Inferior" },
  { id: 2, name: "Normal" },
  { id: 3, name: "Superior" },
  { id: 4, name: "Magic" },
  { id: 5, name: "Set" },
  { id: 6, name: "Rare" },
  { id: 7, name: "Unique" },
  { id: 8, name: "Crafted" },
  { id: 9, name: "Tempered" },
];

const TIERS: { id: Tier; name: string }[] = [
  { id: Tier.NORMAL, name: "Normal" },
  { id: Tier.EXCEPTIONAL, name: "Exceptional" },
  { id: Tier.ELITE, name: "Elite" },
];

const CONTAINERS: { id: string; name: string }[] = [
  { id: "equipped", name: "Equipped" },
  { id: "inventory", name: "Inventory" },
  { id: "stash", name: "Stash" },
  { id: "cube", name: "Horadric Cube" },
  { id: "belt", name: "Belt" },
];

/** The four `dwFlags` bits worth filtering on, each offered in both directions. */
const FLAG_OPTIONS: {
  key: string;
  label: string;
  mask: number;
  require: boolean;
}[] = [
  { mask: RUNEWORD_FLAG, label: "Runeword" },
  { mask: ETHEREAL_FLAG, label: "Ethereal" },
  { mask: SOCKETED_FLAG, label: "Socketed" },
  { mask: IDENTIFIED_FLAG, label: "Identified" },
].flatMap(({ mask, label }) => [
  { key: `${label}:yes`, label, mask, require: true },
  {
    key: `${label}:no`,
    label: `Not ${label.toLowerCase()}`,
    mask,
    require: false,
  },
]);

/** The filter fields that are a numeric bound pair rather than a list or a flag. */
type RangeKey = {
  [K in keyof Filters]: Filters[K] extends { min: string; max: string }
    ? K
    : never;
}[keyof Filters];

/**
 * Every numeric bound the panel offers, in the order they read.
 *
 * The three requirements are grouped the way ResurrectedTrade groups them: a MAX is the useful end
 * — "what can this character actually wear" — which is why they are ranges rather than minima. All
 * three are resolved at ingest, and required level takes the conservative maximum of an affix's
 * general and class-specific requirement, so a bound is never optimistic about what a character
 * can equip.
 */
const RANGE_FIELDS: { key: RangeKey; label: string; hint?: string }[] = [
  {
    key: "sockets",
    label: "Sockets",
    hint: "How many sockets the item has. The game writes this once, so the bound is exact.",
  },
  { key: "requiredLevel", label: "Required level" },
  { key: "requiredStrength", label: "Required strength" },
  { key: "requiredDexterity", label: "Required dexterity" },
  {
    key: "itemLevel",
    label: "Item level",
    hint: "What the item rolled at — which decides the affixes it could have. Not what it demands of a wearer.",
  },
];

/**
 * Type, quality, tier and location are one question asked of four lists: which of these ids.
 *
 * Generic over the id because a container's is its name and the rest are table rows, which is a
 * difference of what the store keys on rather than of what the reader is being asked.
 */
function IdSelect<Id extends string | number>({
  options,
  chosen,
  onChange,
  placeholder,
  width,
}: {
  options: { id: Id; name: string }[];
  chosen: Id[];
  onChange: (ids: Id[]) => void;
  placeholder: string;
  width?: string;
}) {
  return (
    <MultiSelect
      values={options.filter((o) => chosen.includes(o.id))}
      options={options}
      keyOf={(o) => String(o.id)}
      labelOf={(o) => o.name}
      onChange={(next) => onChange(next.map((o) => o.id))}
      placeholder={placeholder}
      width={width}
    />
  );
}

/**
 * Base items, types and named items, read out of the tables once.
 *
 * A base item is named through items.txt's `namestr`, which is a STRING-TABLE key, rather than by
 * the row's own `name`. That column is the designer's internal label and is not what the game
 * draws — worse, for the charms it is actively misleading: cm2 is `Charm Medium` where the game
 * says "Large Charm", and cm3 is `Charm Large` where the game says "Grand Charm". So a reader
 * picking "Charm Large" got grand charms. `name` remains the fallback for a row with no key.
 */
function useItemTaxonomy(engine: TooltipEngine | null) {
  return useMemo(() => {
    if (!engine) return { items: [], types: [] };
    // Everything a reader means by "which item", in one list. The contract splits these across
    // three fields; which one is a detail of the request rather than of the question.
    const items: SpecificPick[] = [];

    const displayName = (classId: number): string => {
      const key = engine.items.getString(classId, "namestr").trim();
      if (key) {
        const resolved = (
          engine.data.strings.getByIndex(
            engine.data.strings.getIndexByKey(key),
          ) ?? ""
        ).trim();
        if (resolved && resolved !== MISSING_STRING) return resolved;
      }
      return engine.items.getString(classId, "name").trim();
    };

    for (let classId = 0; classId < engine.items.rowCount; classId++) {
      if (!engine.items.tryResolve(classId)) continue;
      const name = displayName(classId);
      const code = engine.items.code(classId).trim();
      if (!name || !code) continue;
      items.push({ kind: "base", id: classId, name });
    }

    // Uniques: the row IS the file_index, paired with quality 7 at build time. Only the enabled
    // ones — the rest cannot exist, so offering them offers a search that never matches.
    const uniques = engine.data.uniqueItems;
    for (let row = 0; row < (uniques?.rowCount ?? 0); row++) {
      if (!uniques!.getInt(row, "enabled", 0)) continue;
      const key = uniques!.getString(row, "index");
      if (!key) continue;
      const name = engine.data.strings.getByIndex(
        engine.data.strings.getIndexByKey(key),
      );
      if (!name || name === MISSING_STRING) continue;
      items.push({ kind: "unique", id: row, name });
    }

    // Set items: `setItemId` is the setitems.txt row, which is the file_index for quality 5.
    for (let piece = 0; piece < engine.sets.pieceCount; piece++) {
      const record = engine.sets.pieceAt(piece);
      if (!record?.name || record.name === MISSING_STRING) continue;
      items.push({ kind: "set", id: record.setItemId, name: record.name });
    }

    const types: NamedId[] = [];
    const itemTypes = engine.data.itemTypes;
    for (let row = 0; row < engine.types.rowCount; row++) {
      const code = engine.types.codeAt(row).trim();
      if (!code) continue;
      const name = itemTypes?.getString(row, "ItemType").trim() ?? "";
      types.push({ id: row, name: name || code });
    }

    // Runewords, keyed by the STRING-table index rather than the runes.txt row — because that is
    // what the game stores. On a runeword it reuses `magic_prefix[0]` for it (confirmed against
    // real captures: Sanctuary 20627, Call to Arms 20519, Treachery 20653), which is why the
    // filter needs no extra capture and no extra table at query time.
    const runes = engine.data.runes;
    const seenRunewords = new Set<number>();
    for (let row = 0; row < (runes?.rowCount ?? 0); row++) {
      // Only the ones the game actually enables — 78 of 169 in vanilla. Offering the rest would be
      // offering searches that can never match anything.
      if (!runes!.getInt(row, "complete", 0)) continue;
      const key = runes!.getString(row, "name");
      if (!key) continue;
      const id = engine.data.strings.getIndexByKey(key);
      // Two rows share a name and an id ("Passion"), and the id is what we filter on, so they are
      // the same filter rather than two.
      if (seenRunewords.has(id)) continue;
      const name = engine.data.strings.getByIndex(id);
      if (!name || name === MISSING_STRING) continue;
      seenRunewords.add(id);
      items.push({ kind: "runeword", id, name });
    }

    return {
      items: items.sort((a, b) => a.name.localeCompare(b.name)),
      types: types.sort((a, b) => a.name.localeCompare(b.name)),
    };
  }, [engine]);
}

/**
 * How many inventory graphics each base item can roll.
 *
 * Rings, amulets, jewels and charms have several appearances chosen by the item's `gfxIndex`; every
 * other base has one. The sprite name is the code with a 1-based suffix, so gfxIndex 0 on `cm1` is
 * `cm11` — which is also what `appearance()` returns, and how the DC6 files we ship are named.
 *
 * Stated rather than discovered. The tables will keep naming variants past the last one that
 * exists (they offer `rin7`), and a missing sprite renders as a box rather than failing, so the
 * count cannot be read back from either — and probing for it at runtime is a fetch per candidate
 * to learn a constant. ResurrectedTrade lists the same set.
 */
const SPRITE_VARIANTS: Record<string, number> = {
  rin: 5,
  amu: 3,
  cm1: 3,
  cm2: 3,
  cm3: 3,
  jew: 6,
};

/** The variants a base offers, as `{ gfxIndex, sprite }`, or empty when it has only one look. */
function spriteVariantsOf(
  engine: TooltipEngine | null,
  classId: number | null,
): { gfxIndex: number; sprite: string }[] {
  if (!engine || classId === null) return [];
  const code = engine.items.code(classId).trim().toLowerCase();
  const count = SPRITE_VARIANTS[code] ?? 0;
  return Array.from({ length: count }, (_, gfxIndex) => ({
    gfxIndex,
    sprite: `${code}${gfxIndex + 1}`,
  }));
}

/**
 * Which base item the appearance filter should offer variants for, or null.
 *
 * Appearance is only a question once the item is narrowed to ONE base that has variants — "show me
 * a small charm that looks like this". Both routes to that count: naming the base directly, and
 * naming a TYPE whose descendants resolve to a single base.
 */
function variantBaseOf(
  engine: TooltipEngine | null,
  items: SpecificPick[],
  itemTypes: number[],
): number | null {
  if (!engine) return null;

  const bases = new Set(
    items.flatMap((i) => (i.kind === "base" ? [i.id] : [])),
  );
  for (const row of itemTypes) {
    for (const classId of engine.classIdsOfType(engine.types.codeAt(row))) {
      bases.add(classId);
    }
  }

  return bases.size === 1 ? [...bases][0] : null;
}

function GraphicPicker({
  engine,
  filters,
  onChange,
}: {
  engine: TooltipEngine | null;
  filters: Filters;
  onChange: (next: Filters) => void;
}) {
  // Keyed on the two lists it reads rather than on `filters`, which is a new object after every
  // keystroke in any bound box: resolving a type means walking every items.txt row and testing it
  // against the type's descendants, once per type chosen.
  const variants = useMemo(
    () =>
      spriteVariantsOf(
        engine,
        variantBaseOf(engine, filters.items, filters.itemTypes),
      ),
    [engine, filters.items, filters.itemTypes],
  );
  if (variants.length === 0) return null;

  return (
    <Field
      label="Appearance"
      hint="Which inventory graphic the item rolled. Only meaningful once one base item is named."
    >
      {/* A list like every other field's, with the sprite as the option's label — which is what
          ResurrectedTrade does. A row of sprite buttons was a control shaped like nothing else on
          the panel, and three tall charm graphics side by side dominated it. */}
      <MultiSelect
        values={variants.filter((v) => filters.gfxIndexes.includes(v.gfxIndex))}
        options={variants}
        keyOf={(v) => String(v.gfxIndex)}
        labelOf={(v) => `Variant ${v.gfxIndex + 1}`}
        onChange={(next) =>
          onChange({ ...filters, gfxIndexes: next.map((v) => v.gfxIndex) })
        }
        placeholder="Any appearance"
        width="12rem"
        renderOption={(v) => (
          <span className="flex items-center gap-2">
            <span className="flex h-10 w-8 items-center justify-center">
              <ItemSprite code={v.sprite} colorShift={-1} invTrans={0} alt="" />
            </span>
            Variant {v.gfxIndex + 1}
          </span>
        )}
      />
    </Field>
  );
}

/** How each kind of pick is labelled in the merged list. */
const PICK_KINDS: Record<SpecificPick["kind"], string> = {
  base: "Base",
  unique: "Unique",
  set: "Set",
  runeword: "Runeword",
};

/**
 * The colour each kind is drawn in — the GAME's, resolved through the same table the tooltips use.
 *
 * Not Tailwind classes. `text-d2-gold` is the app's brand accent (#c9a227), so uniques came out the
 * colour of the buttons, and `text-green-400` (#4ade80) is a pastel where the game's set green is
 * #00ff00. A runeword's name is drawn gold, the same as a unique's, which is why they share a
 * value here rather than each guessing one.
 */
const PICK_COLORS: Record<SpecificPick["kind"], string> = {
  base: colorForQuality(2),
  unique: colorForQuality(7),
  set: colorForQuality(5),
  runeword: colorForQuality(7),
};

export function PropertyFilterPanel({
  filters,
  onChange,
  profiles,
  engine,
}: {
  filters: Filters;
  onChange: (next: Filters) => void;
  profiles: string[];
  engine: TooltipEngine | null;
}) {
  const { items, types } = useItemTaxonomy(engine);
  const patch = (next: Partial<Filters>) => onChange({ ...filters, ...next });
  // A computed key widens to `string`, so the spread's type no longer names the field it set;
  // `RangeKey` is what checks that the key is one of the five, and the shape is fixed either way.
  const patchRange = (key: RangeKey, part: { min?: string; max?: string }) =>
    onChange({ ...filters, [key]: { ...filters[key], ...part } } as Filters);

  const identitySpan = identityFieldSpan(filters.items);
  // Named, because the change handler below has to know which entry was just added — the option
  // objects are module constants, so identity is a sound comparison.
  const chosenFlags = FLAG_OPTIONS.filter((o) =>
    o.require
      ? (filters.flagsAll & o.mask) !== 0
      : (filters.flagsNone & o.mask) !== 0,
  );

  return (
    <div className="space-y-4">
      <Group label="Identity">
        <Field
          label="Item"
          hint="A base item, a unique, a set piece or a runeword. Several of the SAME kind are alternatives; different kinds narrow each other."
          className="col-span-2"
        >
          <MultiSelect
            values={filters.items}
            options={items}
            keyOf={(p) => `${p.kind}:${p.id}`}
            labelOf={(p) => p.name}
            onChange={(picks) => patch({ items: picks })}
            placeholder="Any item"
            width="30rem"
            renderOption={(p) => (
              <span className="flex items-center gap-2">
                <span className="w-16 shrink-0 text-[10px] uppercase tracking-wide text-zinc-500">
                  {PICK_KINDS[p.kind]}
                </span>
                <span
                  className="truncate"
                  style={{ color: PICK_COLORS[p.kind] }}
                >
                  {p.name}
                </span>
              </span>
            )}
          />
          {/* The store answers a contradiction literally — with nothing — and cannot tell a
            deliberate pairing ("Treachery, on a Wire Fleece") from a mistaken one. Said here so an
            empty page reads as a filter to fix rather than as missing items. */}
          {identitySpan.length > 1 && (
            <p className="mt-1 text-[11px] text-amber-500/80">
              {identitySpan.join(" and ")} are separate filters and an item has
              to satisfy both — useful for &ldquo;this runeword on that
              base&rdquo;, but two unrelated picks match nothing. Search them
              one at a time.
            </p>
          )}
        </Field>

        <Field
          label="Type"
          hint="An ItemTypes.txt row, matched through its descendants — pick Sword and get every sword."
        >
          <IdSelect
            options={types}
            chosen={filters.itemTypes}
            onChange={(itemTypes) => patch({ itemTypes })}
            placeholder="Any type"
          />
        </Field>

        <Field
          label="Quality"
          hint="The roll — magic, rare, unique and so on. Independent of tier."
        >
          <IdSelect
            options={QUALITIES}
            chosen={filters.qualities}
            onChange={(qualities) => patch({ qualities })}
            placeholder="Any quality"
            width="14rem"
          />
        </Field>

        <Field
          label="Tier"
          hint="The BASE item's tier — normal, exceptional or elite. Independent of quality."
        >
          <IdSelect
            options={TIERS}
            chosen={filters.tiers}
            onChange={(tiers) => patch({ tiers })}
            placeholder="Any tier"
            width="14rem"
          />
        </Field>
      </Group>

      <Group label="Where to look">
        <Field
          label="Character"
          hint="Which characters to search. Clear it for all of them."
        >
          {/* Several, because comparing what a handful of mules hold is the normal question — and
              "all" is the empty selection rather than an option in the list, so there is no way to
              pick it alongside a name and mean two contradictory things. */}
          <IdSelect
            options={profiles.map((p) => ({ id: p, name: p }))}
            chosen={filters.profiles}
            onChange={(ids) => patch({ profiles: ids })}
            placeholder="All characters"
          />
        </Field>

        <Field
          label="Location"
          hint="Which containers to look in. Clear it for anywhere; drop Equipped to exclude worn items."
        >
          <IdSelect
            options={CONTAINERS}
            chosen={filters.containers}
            onChange={(containers) => patch({ containers })}
            placeholder="Anywhere"
            width="14rem"
          />
        </Field>
      </Group>

      <Group label="Numbers">
        {RANGE_FIELDS.map(({ key, label, hint }) => (
          <Field key={key} label={label} hint={hint}>
            <RangeBox
              min={filters[key].min}
              max={filters[key].max}
              onMin={(min) => patchRange(key, { min })}
              onMax={(max) => patchRange(key, { max })}
            />
          </Field>
        ))}
      </Group>

      <Group label="Other">
        {/* One question, not two. A modifier bound is always compared against the item's TOTAL —
          the number its tooltip prints — and the only thing left to decide is whether what is
          socketed into it counts towards that total. */}
        <Field
          label="Socketed gems and runes"
          hint="Whether what is socketed into an item counts towards its modifiers. Ignore them to find an item that is good on its own rather than one propped up by a rune."
        >
          <SingleSelect
            value={String(filters.socketScope)}
            onChange={(value) =>
              patch({ socketScope: Number(value) as SocketScope })
            }
            options={[
              {
                value: String(SocketScope.WITH_FILLERS),
                label: "Count towards the item",
              },
              {
                value: String(SocketScope.HOST_ONLY),
                label: "Ignore — the item's own",
              },
            ]}
          />
        </Field>

        {/* One list rather than four cycling chips. Negation is expressed by listing both
            directions — "Runeword" and "Not runeword" are separate entries — which is what lets a
            combo say a three-state thing at all, and picking one direction clears the other. */}
        <Field
          label="Flags"
          hint="Require or exclude the dwFlags bits worth filtering on."
        >
          <MultiSelect
            values={chosenFlags}
            options={FLAG_OPTIONS}
            keyOf={(o) => o.key}
            labelOf={(o) => o.label}
            placeholder="Any"
            width="14rem"
            onChange={(next) => {
              let all = 0;
              let none = 0;
              for (const o of next) {
                if (o.require) all |= o.mask;
                else none |= o.mask;
              }
              // A bit cannot be both required and excluded, and the direction just picked is the
              // one that wins. Resolving it by formula instead (`all & ~none`) always let the
              // negative win, so "Not runeword" could never be changed to "Runeword" — the click
              // landed, the list did not change, and nothing said why.
              for (const o of next.filter((o) => !chosenFlags.includes(o))) {
                if (o.require) none &= ~o.mask;
                else all &= ~o.mask;
              }
              patch({ flagsAll: all, flagsNone: none });
            }}
          />
        </Field>

        <GraphicPicker engine={engine} filters={filters} onChange={onChange} />
      </Group>
    </div>
  );
}
