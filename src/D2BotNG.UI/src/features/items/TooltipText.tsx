/**
 * How the game's tooltip TEXT is drawn, shared by everything that draws it.
 *
 * The hover tooltip and the search results render the same rows from the same source, so they have
 * to agree on centring, line height, font and what a blank row is. They did not: the results were
 * left-aligned sans-serif while the tooltip was centred Exocet, and the same item read as two
 * different things depending on where you looked at it.
 */

import type { ReactNode } from "react";
import clsx from "clsx";
import { ItemFont } from "@/generated/settings_pb";
import { useSettings } from "@/stores/event-store";
import type { ColoredTextSegment } from "./item-utils";

const fontFamilyMap: Record<ItemFont, string> = {
  [ItemFont.EXOCET]: '"Exocet Blizzard OT Light", "Exocet", monospace',
  [ItemFont.CONSOLAS]: 'Consolas, "Courier New", monospace',
  [ItemFont.SYSTEM]: "system-ui, -apple-system, sans-serif",
};

/**
 * The style a block of tooltip text is drawn in — the reader's chosen font, and the small caps the
 * game's own lettering has. Applied to the BLOCK rather than per line, so the rows inside it cannot
 * drift apart.
 */
export function useTooltipTextStyle() {
  const settings = useSettings();
  const font = settings?.display?.itemFont ?? ItemFont.EXOCET;
  return {
    fontFamily: fontFamilyMap[font] ?? fontFamilyMap[ItemFont.EXOCET],
    fontVariantCaps: "small-caps" as const,
  };
}

/**
 * One row, as coloured runs.
 *
 * A row with no glyphs renders a non-breaking space rather than collapsing: the game's blank is a
 * real text line — it appends a row that happens to have no characters — so anything shorter
 * reflows the block. That is why a rune's tooltip used to resize when Ctrl was held.
 *
 * `children` is for anything attached to a row that is not part of its text — the sort arrow on a
 * search result. It is dropped on a blank row, which has nothing to attach to.
 */
export function TooltipLine({
  segments,
  className,
  onClick,
  title,
  children,
}: {
  segments: ColoredTextSegment[];
  className?: string;
  onClick?: () => void;
  title?: string;
  children?: ReactNode;
}) {
  const hasContent = segments.some((s) => s.text.length > 0);

  return (
    <div
      className={clsx("text-center leading-5", className)}
      onClick={onClick}
      title={title}
    >
      {hasContent ? (
        <>
          {segments.map((segment, i) => (
            <span key={i} style={{ color: segment.color }}>
              {segment.text}
            </span>
          ))}
          {children}
        </>
      ) : (
        <span>&nbsp;</span>
      )}
    </div>
  );
}
