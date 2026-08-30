/**
 * The search panel's own controls.
 *
 * A search form is dozens of small fields, and the app's general-purpose `Input`/`Select` are sized
 * for a settings page — `py-2`, `rounded-lg`, a `text-sm` label above each. At this density that is
 * what made the panel sprawl: every bound pair was taller than the answer it collects. These are the
 * same controls at form density, defined once so the panel is consistent by construction rather
 * than by every call site repeating a class string.
 *
 * Modelled on the Path of Exile trade site and ResurrectedTrade, which both put the label beside a
 * short control rather than above a wide one.
 */

import {
  useMemo,
  useRef,
  useState,
  type FocusEvent,
  type ReactNode,
} from "react";
import {
  Combobox,
  ComboboxInput,
  ComboboxOption,
  ComboboxOptions,
} from "@headlessui/react";
import { CheckIcon, ChevronDownIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";

/** Cap on how many matches a dropdown renders. Typing narrows; scrolling 1,200 rows does not. */
const MAX_SUGGESTIONS = 60;

/**
 * The bordered box every control here shares, so a row of mixed fields lines up.
 *
 * The padding and the ring are the easy half. SIZE is the part that has bitten this file in both
 * axes, for one reason: `clsx` concatenates classes, it does not resolve Tailwind conflicts
 * (`tailwind-merge` is not a dependency), so an `h-6` or `w-14` handed in by a call site sits at
 * equal specificity with an `h-8`/`w-full` baked in here and stylesheet order decides. The baked
 * one wins — silently, and always in the direction of the bigger box, so a field asking to be
 * compact came out full size however narrow its content.
 *
 * So neither axis is baked. Height is the `size` prop, resolved through `SIZE`: a fixed pair
 * rather than a free class, because the reason a shared height exists at all is that a native
 * select, a text input and a div sized by their own padding come out at three different heights in
 * the same row, which is what made the panel look assembled from spare parts. Width is an inline
 * length, which no utility class can outrank.
 */
const BOX =
  "rounded bg-zinc-900 px-2 text-sm text-zinc-100 ring-1 ring-inset ring-zinc-700 " +
  "focus-within:ring-d2-gold focus:ring-d2-gold focus:outline-none placeholder:text-zinc-600";

/** Form density, and one step down for controls that sit INSIDE a header bar or a row. */
const SIZE = {
  sm: "h-6",
  md: "h-8",
} as const;

type Size = keyof typeof SIZE;

/** The floating list every dropdown here drops. */
const MENU = "z-50 rounded-lg bg-zinc-800 shadow-lg ring-1 ring-zinc-700";

/** The scrollable body of one — separate because `MultiSelect` puts a footer below it. */
const MENU_BODY = "max-h-72 overflow-y-auto p-1";

/** Capped against the viewport: a 30rem list of item names must not push a laptop page sideways. */
const menuWidth = (width: string) => ({ width: `min(${width}, 90vw)` });

/** A number field with steppers is a filter bound, not a quantity anyone nudges. */
const NO_SPINNERS =
  "[appearance:textfield] [&::-webkit-inner-spin-button]:m-0 " +
  "[&::-webkit-inner-spin-button]:appearance-none " +
  "[&::-webkit-outer-spin-button]:m-0 [&::-webkit-outer-spin-button]:appearance-none";

/**
 * The matches a query narrows an option list to.
 *
 * Every word has to appear, so "cast rate" and "rate cast" find the same thing. Stops at the cap
 * rather than filtering the whole list and slicing, since the tail is never rendered.
 */
function filterOptions<T>(
  options: T[],
  query: string,
  labelOf: (value: T) => string,
): T[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const matches: T[] = [];
  for (const option of options) {
    if (words.some((w) => !labelOf(option).toLowerCase().includes(w))) continue;
    matches.push(option);
    if (matches.length === MAX_SUGGESTIONS) break;
  }
  return matches;
}

/**
 * Open/close for the two dropdowns that are not HeadlessUI components.
 *
 * The containment check is the whole of it: clicking an option moves focus within the control, and
 * a plain `onBlur` would read that as leaving and close the list out from under the click. The
 * options suppress the mousedown as well, which is what keeps a typable box typable across several
 * picks — but the check is what makes keyboard focus behave too.
 */
function usePopover(onClose?: () => void) {
  const [open, setOpen] = useState(false);
  const boxRef = useRef<HTMLDivElement>(null);

  return {
    open,
    setOpen,
    /** Spread onto the positioning wrapper the panel is anchored to. */
    anchor: {
      ref: boxRef,
      className: "relative",
      onBlur: (e: FocusEvent<HTMLDivElement>) => {
        if (boxRef.current?.contains(e.relatedTarget)) return;
        setOpen(false);
        onClose?.();
      },
    },
  };
}

/**
 * One row of a dropdown.
 *
 * The tick is `invisible` rather than absent so the labels do not shift sideways as picks are made.
 */
function OptionRow({
  selected,
  onSelect,
  children,
}: {
  selected: boolean;
  onSelect: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      // Keeps focus inside the control, so the list stays open — and stays typable where the box is
      // an input rather than a button.
      onMouseDown={(e) => e.preventDefault()}
      onClick={onSelect}
      className={clsx(
        "flex w-full items-center gap-2 rounded px-2 py-1 text-left text-sm hover:bg-zinc-700",
        selected ? "text-zinc-100" : "text-zinc-400",
      )}
    >
      <CheckIcon
        className={clsx(
          "h-4 w-4 shrink-0",
          selected ? "text-d2-gold" : "invisible",
        )}
      />
      <span className="min-w-0 flex-1">{children}</span>
    </button>
  );
}

/**
 * A titled sub-panel: a tinted header bar over a body.
 *
 * The halves of the filter form were previously separated by nothing but a heading with a rule
 * under it, which left one flat expanse with no edges — the reason it read as unfinished rather
 * than as two things side by side.
 */
export function Panel({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  // NOT overflow-hidden, however tempting it is for the header's corners: the pickers inside drop
  // an absolutely positioned menu, and clipping the panel clipped every one of them to the panel's
  // own edge. The header rounds its own top corners instead, which is all the clipping was doing.
  return (
    <section className="rounded-lg bg-zinc-900/20 ring-1 ring-zinc-800">
      <header className="flex h-8 items-center rounded-t-lg border-b border-zinc-800 bg-zinc-900/60 px-3">
        <h3 className="select-none text-[11px] font-semibold uppercase tracking-wider text-zinc-400">
          {title}
        </h3>
      </header>
      <div className="p-3">{children}</div>
    </section>
  );
}

/**
 * A cluster of fields under a quiet caption.
 *
 * Twelve fields in one grid is a wall; the same twelve in three named clusters is a form. Lighter
 * chrome than `Panel` on purpose — these are divisions WITHIN a panel, and giving them the same
 * weight would flatten the hierarchy again.
 */
export function Group({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  return (
    <div>
      <div className="mb-2 flex items-center gap-2">
        <span className="select-none text-[10px] font-medium uppercase tracking-wider text-zinc-600">
          {label}
        </span>
        <span className="h-px flex-1 bg-zinc-800" />
      </div>
      <div className="grid grid-cols-2 gap-x-3 gap-y-2">{children}</div>
    </div>
  );
}

/** One labelled row: a short caption above a control, both compact. */
export function Field({
  label,
  hint,
  children,
  className,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div className={className}>
      <div
        className="mb-1 select-none text-[11px] font-medium uppercase tracking-wide text-zinc-500"
        title={hint}
      >
        {label}
      </div>
      {children}
    </div>
  );
}

export function NumberBox({
  value,
  onChange,
  placeholder,
  min,
  max,
  size = "md",
  width = "4rem",
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  min?: number;
  max?: number;
  size?: Size;
  /** A CSS length, sized for the digits the field actually collects. */
  width?: string;
}) {
  return (
    <input
      type="number"
      inputMode="numeric"
      min={min}
      max={max}
      value={value}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value)}
      style={{ width }}
      className={clsx(BOX, SIZE[size], NO_SPINNERS)}
    />
  );
}

/** The min/max pair that every numeric filter is, captioned where a row carries more than one. */
export function RangeBox({
  label,
  min,
  max,
  onMin,
  onMax,
  size,
  width,
}: {
  label?: string;
  min: string;
  max: string;
  onMin: (value: string) => void;
  onMax: (value: string) => void;
  size?: Size;
  width?: string;
}) {
  return (
    <div className="flex items-center gap-1">
      {label && (
        <span className="text-[10px] uppercase tracking-wide text-zinc-600">
          {label}
        </span>
      )}
      <NumberBox
        value={min}
        onChange={onMin}
        placeholder="min"
        size={size}
        width={width}
      />
      <NumberBox
        value={max}
        onChange={onMax}
        placeholder="max"
        size={size}
        width={width}
      />
    </div>
  );
}

/**
 * A multi-select you type into, the way ResurrectedTrade's autocomplete and the trade site's
 * filters behave.
 *
 * The box IS the search field: focus it and type to narrow, click to toggle, and the list stays
 * open so several picks cost one visit. When you are not typing, the same box states the selection
 * — so the field says what it is filtering on without being opened.
 *
 * Toggling lives in the LIST rather than on removable chips in the box. A chip's × sat inside the
 * control's own pointer handling and was swallowed about half the time; a tick in the list has one
 * place it happens and nothing to intercept it.
 */
export function MultiSelect<T>({
  values,
  options,
  keyOf,
  labelOf,
  onChange,
  placeholder = "Any",
  renderOption,
  width = "18rem",
}: {
  values: T[];
  options: T[];
  keyOf: (value: T) => string;
  labelOf: (value: T) => string;
  onChange: (values: T[]) => void;
  placeholder?: string;
  /** Richer option markup — a kind column, a sprite. Falls back to the label. */
  renderOption?: (value: T) => ReactNode;
  width?: string;
}) {
  const [query, setQuery] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  // The query has done its job once the list closes; leaving it would start the next visit from a
  // list still narrowed by a search the reader has finished with.
  const { open, setOpen, anchor } = usePopover(() => setQuery(""));
  const chosen = new Set(values.map(keyOf));

  // Only while the list is on screen: this control re-renders with the rest of the form, and
  // filtering hundreds of options to render none of them is the whole cost of a keystroke
  // elsewhere on the panel.
  const matches = open ? filterOptions(options, query, labelOf) : [];

  const toggle = (option: T) => {
    onChange(
      chosen.has(keyOf(option))
        ? values.filter((v) => keyOf(v) !== keyOf(option))
        : [...values, option],
    );
    setQuery("");
  };

  const summary = values.map(labelOf).join(", ");

  return (
    <div {...anchor}>
      <div
        className={clsx(BOX, SIZE.md, "flex w-full items-center gap-1")}
        onMouseDown={() => inputRef.current?.focus()}
      >
        <input
          ref={inputRef}
          value={query}
          onFocus={() => setOpen(true)}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onKeyDown={(e) => {
            if (e.key === "Escape") setOpen(false);
          }}
          // The selection shows as the placeholder while nothing is typed, so one input serves
          // both jobs — no second box appearing beneath the first to type into.
          placeholder={summary || placeholder}
          title={summary}
          className={clsx(
            "min-w-0 flex-1 border-0 bg-transparent p-0 text-sm focus:outline-none focus:ring-0",
            values.length > 0
              ? "placeholder:text-zinc-200"
              : "placeholder:text-zinc-600",
          )}
        />
        <ChevronDownIcon className="h-4 w-4 shrink-0 text-zinc-500" />
      </div>

      {open && (
        <div
          style={menuWidth(width)}
          className={clsx(MENU, "absolute left-0 top-full mt-1")}
        >
          <div className={MENU_BODY}>
            {matches.map((option) => (
              <OptionRow
                key={keyOf(option)}
                selected={chosen.has(keyOf(option))}
                onSelect={() => toggle(option)}
              >
                {renderOption ? renderOption(option) : labelOf(option)}
              </OptionRow>
            ))}
            {matches.length === 0 && (
              <p className="px-2 py-1 text-sm text-zinc-500">
                Nothing matches.
              </p>
            )}
          </div>
          {values.length > 0 && (
            <div className="border-t border-zinc-700 p-1">
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => onChange([])}
                className="w-full rounded px-2 py-1 text-left text-xs text-zinc-400 hover:bg-zinc-700 hover:text-zinc-200"
              >
                Clear {values.length} selected
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * One pick from a short list, drawn exactly like `MultiSelect`.
 *
 * A native `<select>` cannot be made to match: it renders its own text metrics and its own arrow,
 * so a long option clipped where the custom boxes wrapped and the control read as a different kind
 * of thing sitting in the same row. This is the same box, the same chevron and the same list, with
 * the tick marking one value instead of several.
 */
export function SingleSelect({
  value,
  onChange,
  options,
  size = "md",
  boxWidth = "100%",
  width = "14rem",
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  size?: Size;
  /** The box's own width; by default it fills the field it sits in. */
  boxWidth?: string;
  /** The dropped list's width, which the longest option needs rather than the box does. */
  width?: string;
  className?: string;
}) {
  const { open, setOpen, anchor } = usePopover();
  const current = options.find((o) => o.value === value);

  return (
    <div {...anchor}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        // Escape closes, as it does on the other two pickers here. Without it this was the one
        // control a keyboard could open and not close.
        onKeyDown={(e) => e.key === "Escape" && setOpen(false)}
        style={{ width: boxWidth }}
        className={clsx(
          BOX,
          SIZE[size],
          "flex items-center gap-1 text-left",
          className,
        )}
      >
        <span className="min-w-0 flex-1 truncate" title={current?.label}>
          {current?.label ?? ""}
        </span>
        <ChevronDownIcon className="h-4 w-4 shrink-0 text-zinc-500" />
      </button>

      {open && (
        <div
          style={menuWidth(width)}
          className={clsx(MENU, MENU_BODY, "absolute left-0 top-full mt-1")}
        >
          {options.map((o) => (
            <OptionRow
              key={o.value}
              selected={o.value === value}
              onSelect={() => {
                onChange(o.value);
                setOpen(false);
              }}
            >
              {o.label}
            </OptionRow>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * The single-pick counterpart, for a list too long to be a `<select>`.
 *
 * Typing is the only way through 1,230 modifiers, so this one stays a combobox. `MultiSelect` is
 * typable too, so the box is not the difference: this one takes ONE pick, which closes the list and
 * fills the box, where a multi-select has to stay open and keep stating a set. And it is HeadlessUI
 * doing the work a list this long needs and the hand-rolled popovers above do not attempt:
 * arrow-key navigation through a filtered list, and a panel anchored outside the DOM it sits in.
 */
export function SearchSelect<T>({
  value,
  options,
  keyOf,
  labelOf,
  onChange,
  placeholder,
  width = "24rem",
}: {
  value: T | null;
  options: T[];
  keyOf: (value: T) => string;
  labelOf: (value: T) => string;
  onChange: (value: T | null) => void;
  placeholder?: string;
  width?: string;
}) {
  const [query, setQuery] = useState("");
  // Held across renders: the catalogue is ~1,230 entries and this control re-renders with every
  // keystroke its neighbours receive, none of which change what it would match.
  const matches = useMemo(
    () => filterOptions(options, query, labelOf),
    [options, query, labelOf],
  );

  return (
    <Combobox
      value={value}
      onChange={(next: T | null) => onChange(next)}
      onClose={() => setQuery("")}
      immediate
    >
      <div className="relative">
        <ComboboxInput
          className={clsx(BOX, SIZE.md, "w-full")}
          placeholder={placeholder}
          displayValue={(v: T | null) => (v ? labelOf(v) : "")}
          onChange={(e) => setQuery(e.target.value)}
        />
        <ComboboxOptions
          anchor="bottom start"
          style={menuWidth(width)}
          className={clsx(MENU, MENU_BODY, "empty:invisible")}
        >
          {matches.map((option) => (
            <ComboboxOption
              key={keyOf(option)}
              value={option}
              className="cursor-pointer rounded px-2 py-1 text-sm text-zinc-300 data-[focus]:bg-zinc-700 data-[focus]:text-zinc-100"
            >
              {labelOf(option)}
            </ComboboxOption>
          ))}
        </ComboboxOptions>
      </div>
    </Combobox>
  );
}
