/**
 * The modifier filter: groups of rows, each group saying how many of its rows must hold.
 *
 * Modelled on ResurrectedTrade's stat selector (which follows the Path of Exile trade site). A
 * group with no count is plain "all of these"; give it a count and it becomes "at least N of
 * these", which is the whole reason groups exist — "any 2 of these 3 skillers", say.
 *
 * Groups are AND-ed with each other and with the property filters, so several groups express
 * "at least 2 of these AND at least 1 of those".
 *
 * The shape is the reference's, and it is what keeps a group to one line per modifier: a CHOSEN
 * modifier is TEXT, not a combobox, and only the one picker at the bottom is an input. A picker per
 * row would give every chosen modifier a full-width text field, a dropdown anchor and its own focus
 * ring, so five modifiers would fill the panel.
 */

import { useMemo } from "react";
import { PlusIcon, XMarkIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";
import type { StatFilterOption } from "./statCatalog";
import {
  countedGroupRefused,
  countedRowOmits,
  countedRowRefusal,
  emptyStatGroup,
  emptyStatRow,
  type CountedRowRefusal,
  type StatFilterGroup,
  type StatFilterRow,
} from "./searchRequest";
import { NumberBox, RangeBox, SearchSelect, SingleSelect } from "./controls";

/** A bound is three digits at the outside, and a row holds up to two pairs of them. */
const BOUND_WIDTH = "3.25rem";

/**
 * Why a row makes a counted group unaskable, for the two modifiers such a group cannot hold.
 *
 * Both are more than one condition, and a group's members are a flat list. Leaving the row out is
 * not a way round it: the same count over fewer members is a different question, and an "at most"
 * group goes out negated, so it would be a WIDER one. So the group is refused whole, and these say
 * which row did it and how to get it back — its own group, or no count.
 */
const REFUSAL_NOTES: Record<CountedRowRefusal, string> = {
  all: "This row is what the group cannot count: the modifier needs ALL of its stats to hold, and a counted group counts either/or rows. Put it in a group with no count.",
  range:
    "This row is what the group cannot count: the second bound makes it two conditions, and a counted group counts one per row. Clear it, or put the row in a group with no count.",
};

/**
 * Holds the typed count inside the range the request will actually use.
 *
 * The builder clamps it too, because the store rejects a count outside 1..N rather than repairing
 * it — but clamping only there would let the header read "at least 3 of 2" while 2 was asked. An
 * empty box is left alone: blank means "all of them", which is a value rather than a mistake.
 */
function clampCount(
  typed: string,
  group: StatFilterGroup,
  rows: number,
): string {
  if (typed.trim() === "") return typed;
  const count = Number(typed);
  if (!Number.isFinite(count)) return typed;
  // "at most 0" is a real filter — none of these — where "at least 0" would constrain nothing.
  const lowest = group.atMost ? 0 : 1;
  return String(Math.max(lowest, Math.min(Math.trunc(count), rows)));
}

// Stated once rather than inline, so the picker's memoised match list is not thrown away by a new
// closure on every keystroke in a bound box.
const optionKey = (option: StatFilterOption) => option.key;
const optionLabel = (option: StatFilterOption) => option.label;

/** One chosen modifier: its wording, its bounds, and a way to drop it. */
function StatRow({
  row,
  counted,
  onChange,
  onRemove,
}: {
  row: StatFilterRow;
  counted: boolean;
  onChange: (row: StatFilterRow) => void;
  onRemove: () => void;
}) {
  const option = row.option;
  if (!option) return null;

  // All three are conditions of the CONTRACT rather than advice, so they are stated on the row —
  // but they apply rarely, which is why the slot below is reserved rather than conditional.
  const refusal = counted ? countedRowRefusal(row) : undefined;
  const omitted = counted ? countedRowOmits(row) : 0;
  const note = refusal
    ? REFUSAL_NOTES[refusal]
    : option.requireAll
      ? "Every stat in the group must meet the bound, the way the game prints the combined line."
      : omitted > 0
        ? `Counted on its primary source; ${omitted} other source${omitted === 1 ? "" : "s"} of this modifier ${omitted === 1 ? "is" : "are"} not counted, because a counted group cannot hold an either/or.`
        : null;

  return (
    <div className="group flex items-center gap-2 rounded px-1 py-0.5 hover:bg-zinc-800/50">
      {/* A fixed slot, occupied or not. Rendering the warning only when it applies made rows with
          one wider than rows without, so the bound boxes beside them stopped lining up. */}
      <span
        className={clsx(
          "w-3 shrink-0 text-center text-[10px]",
          !note && "invisible",
          // Red rather than amber when the row is not in the request at all: amber says "read the
          // small print", and this says "this line is not being searched on".
          note &&
            (refusal
              ? "cursor-help text-red-400"
              : "cursor-help text-amber-500/80"),
        )}
        title={note ?? undefined}
      >
        ⚠
      </span>

      <span
        className={clsx(
          "min-w-0 flex-1 truncate text-xs",
          refusal ? "text-zinc-500 line-through" : "text-zinc-300",
        )}
        title={option.label}
      >
        {option.label}
      </span>

      {option.valueBound && (
        <RangeBox
          label={option.firstLabel}
          size="sm"
          width={BOUND_WIDTH}
          min={row.min}
          max={row.max}
          onMin={(min) => onChange({ ...row, min })}
          onMax={(max) => onChange({ ...row, max })}
        />
      )}
      {/* A second, independent pair: the packed skill LEVEL for chance-to-cast and charges, or
          the HIGH stat of a damage range — two numbers the game prints on one line. */}
      {(option.levelBound || option.rangeMaxStatIds) && (
        <RangeBox
          label={option.secondLabel}
          size="sm"
          width={BOUND_WIDTH}
          min={row.min2}
          max={row.max2}
          onMin={(min2) => onChange({ ...row, min2 })}
          onMax={(max2) => onChange({ ...row, max2 })}
        />
      )}

      <button
        type="button"
        onClick={onRemove}
        title="Remove this modifier"
        className="shrink-0 rounded p-0.5 text-zinc-600 hover:bg-zinc-800 hover:text-red-400"
      >
        <XMarkIcon className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}

function GroupCard({
  catalog,
  group,
  onChange,
  onRemove,
  canRemove,
}: {
  catalog: StatFilterOption[];
  group: StatFilterGroup;
  onChange: (group: StatFilterGroup) => void;
  onRemove: () => void;
  canRemove: boolean;
}) {
  const rows = group.rows.filter((r) => r.option);
  const counted = group.minMatches.trim() !== "";
  // Said on the GROUP as well as on the row, because it is the group that cannot be sent: the row
  // alone is fine, and the Search button is disabled until one of the two is changed.
  const refused = countedGroupRefused(group);

  // Keyed on WHICH modifiers are taken rather than on the rows: editing a bound replaces the rows
  // array without changing that, and this is a pass over the whole ~1,230-entry catalogue that
  // would otherwise re-run on every keystroke in a bound box.
  const chosenKeys = rows.map((r) => r.option!.key).join("\n");
  const available = useMemo(() => {
    const chosen = new Set(chosenKeys.split("\n"));
    return catalog.filter((o) => !chosen.has(o.key));
  }, [catalog, chosenKeys]);

  const add = (option: StatFilterOption) =>
    onChange({
      ...group,
      rows: [
        ...rows,
        {
          ...emptyStatRow(
            group.rows.reduce((max, r) => Math.max(max, r.id), 0) + 1,
          ),
          option,
        },
      ],
    });

  // Not overflow-hidden, for the same reason `Panel` is not: the count picker in the header drops
  // a menu, and clipping the card cut it off at the header's own edge.
  return (
    <div
      className={clsx(
        "rounded-lg bg-zinc-900/20 ring-1",
        refused ? "ring-red-500/40" : "ring-zinc-800",
      )}
    >
      {/* The same header bar the panels use, one level in — so a group reads as a thing with a
          lid rather than as a fenced-off patch of the form. */}
      <div className="flex h-8 items-center gap-2 rounded-t-lg border-b border-zinc-800 bg-zinc-900/60 px-2">
        <span className="select-none text-[10px] font-medium uppercase tracking-wider text-zinc-500">
          Match
        </span>
        {/* At MOST is not a second bound but the complement of at least: the contract has no
            max_matches, because a count-based upper bound can never see the items that satisfy
            NOTHING — exactly the ones "at most 1 of these" is meant to include. Negating "at least
            N+1" does see them, since the negation is over every item. */}
        <SingleSelect
          size="sm"
          boxWidth="5.5rem"
          className="text-[11px]"
          width="9rem"
          value={group.atMost ? "most" : "least"}
          onChange={(mode) => onChange({ ...group, atMost: mode === "most" })}
          options={[
            { value: "least", label: "at least" },
            { value: "most", label: "at most" },
          ]}
        />
        {/* Blank means all of them, which is what the contract reads an absent count as — so the
            placeholder is the count rather than a hint. */}
        <NumberBox
          size="sm"
          width="2.5rem"
          min={group.atMost ? 0 : 1}
          max={rows.length}
          placeholder={group.atMost ? "—" : String(rows.length || 0)}
          value={group.minMatches}
          onChange={(typed) =>
            onChange({
              ...group,
              minMatches: clampCount(typed, group, rows.length),
            })
          }
        />
        <span className="select-none text-[11px] text-zinc-500">
          of {rows.length || "—"}
        </span>

        {canRemove && (
          <button
            type="button"
            onClick={onRemove}
            title="Remove this group"
            className="ml-auto rounded p-0.5 text-zinc-600 hover:bg-zinc-800 hover:text-red-400"
          >
            <XMarkIcon className="h-4 w-4" />
          </button>
        )}
      </div>

      <div className="space-y-0.5 p-1.5">
        {rows.map((row) => (
          <StatRow
            key={row.id}
            row={row}
            counted={counted}
            onChange={(next) =>
              onChange({
                ...group,
                rows: group.rows.map((r) => (r.id === next.id ? next : r)),
              })
            }
            onRemove={() => {
              // The count comes down with the rows. The request clamps it anyway, so leaving it
              // alone would only let the header read "at least 3 of 2" while 2 is what was asked.
              const remaining = group.rows.filter((r) => r.id !== row.id);
              const typed = Number(group.minMatches);
              const tooHigh =
                Number.isFinite(typed) && typed > remaining.length;
              onChange({
                ...group,
                rows: remaining,
                minMatches: tooHigh
                  ? String(remaining.length)
                  : group.minMatches,
              });
            }}
          />
        ))}

        {refused && (
          <p className="px-1 pb-0.5 text-[11px] text-red-400">
            This group cannot be searched while it is counted — the struck-out
            row is more than one condition and a count is over one condition per
            row. Clear the count, or move that row to a group without one.
          </p>
        )}

        {/* One picker for the group, always at the bottom: choosing appends a row and the picker
            resets. It holds no value of its own — the rows above ARE the value — and it does not
            offer a modifier this group already has. */}
        <SearchSelect
          value={null}
          options={available}
          keyOf={optionKey}
          labelOf={optionLabel}
          placeholder="+ Add a modifier…"
          width="34rem"
          onChange={(option) => option && add(option)}
        />
      </div>
    </div>
  );
}

export function StatGroups({
  catalog,
  groups,
  onChange,
}: {
  catalog: StatFilterOption[];
  groups: StatFilterGroup[];
  onChange: (groups: StatFilterGroup[]) => void;
}) {
  return (
    <div className="space-y-2">
      {groups.map((group) => (
        <GroupCard
          key={group.id}
          catalog={catalog}
          group={group}
          canRemove={groups.length > 1}
          onChange={(next) =>
            onChange(groups.map((g) => (g.id === next.id ? next : g)))
          }
          onRemove={() => onChange(groups.filter((g) => g.id !== group.id))}
        />
      ))}

      <button
        type="button"
        onClick={() =>
          onChange([
            ...groups,
            emptyStatGroup(
              groups.reduce((max, g) => Math.max(max, g.id), 0) + 1,
            ),
          ])
        }
        title='Groups are AND-ed, so a second one expresses "at least 2 of these AND at least 1 of those"'
        className="flex items-center gap-1 rounded px-1 text-xs text-zinc-500 hover:text-zinc-300"
      >
        <PlusIcon className="h-3.5 w-3.5" />
        Add another group
      </button>
    </div>
  );
}
