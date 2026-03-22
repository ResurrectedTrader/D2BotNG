/**
 * ItemTooltip component
 *
 * Displays a tooltip with item details styled with the item's quality color.
 * Uses a simple hover-triggered div positioned relative to the parent.
 * Automatically flips to show below when near the top of the viewport.
 */

import {
  memo,
  type ReactNode,
  useMemo,
  useRef,
  useState,
  useCallback,
  useEffect,
} from "react";
import { createPortal } from "react-dom";
import clsx from "clsx";
import type { Item } from "@/generated/items_pb";
import { ItemFont } from "@/generated/settings_pb";
import { useSettings } from "@/stores/event-store";
import { ItemSprite } from "@/lib/rendering";
import { parseD2ColoredText, stripD2ColorCodes } from "./item-utils";

/** Maps ItemFont enum to CSS font family strings */
const fontFamilyMap: Record<ItemFont, string> = {
  [ItemFont.EXOCET]: '"Exocet Blizzard OT Light", "Exocet", monospace',
  [ItemFont.CONSOLAS]: 'Consolas, "Courier New", monospace',
  [ItemFont.SYSTEM]: "system-ui, -apple-system, sans-serif",
};

/** Gets CSS font family for the given ItemFont */
function getFontFamily(font: ItemFont): string {
  return fontFamilyMap[font] ?? fontFamilyMap[ItemFont.EXOCET];
}

/** Extra margin when calculating tooltip position (in pixels) */
const TOOLTIP_MARGIN = 16;

/** Literal backslash-n for splitting lines */
const ESCAPED_NEWLINE = String.raw`\n`;

export interface ItemTooltipProps {
  /** The item to display details for */
  item: Item;
  /** The trigger element */
  children: ReactNode;
  /** Whether to show the item sprite in the tooltip (default: true) */
  showSprite?: boolean;
}

/**
 * Renders a line of D2 description text with color codes parsed.
 * Styled to match D2's tooltip text rendering (16px line height, center-aligned).
 * Empty lines render with a non-breaking space to maintain spacing.
 */
function ColoredDescriptionLine({ text }: { text: string }) {
  const segments = useMemo(() => parseD2ColoredText(text), [text]);
  const hasContent = segments.some((s) => s.text.length > 0);

  return (
    <div className="text-center leading-5">
      {hasContent ? (
        segments.map((segment, i) => (
          <span key={i} style={{ color: segment.color }}>
            {segment.text}
          </span>
        ))
      ) : (
        <span>&nbsp;</span>
      )}
    </div>
  );
}

/**
 * The tooltip content panel - can be used standalone or within ItemTooltip
 */
export const ItemTooltipContent = memo(function ItemTooltipContent({
  item,
  showSprite = true,
}: {
  item: Item;
  showSprite?: boolean;
}) {
  const settings = useSettings();
  const fontFamily = getFontFamily(
    settings?.display?.itemFont ?? ItemFont.EXOCET,
  );
  const showHeader = settings?.display?.showItemHeader ?? false;

  const descriptionLines = useMemo(() => {
    if (!item.description) return [];
    const cleanDesc = item.description.split("$")[0];
    // Split on literal \n (escaped) or actual newlines
    const lines = cleanDesc.includes(ESCAPED_NEWLINE)
      ? cleanDesc.split(ESCAPED_NEWLINE)
      : cleanDesc.split("\n");
    let start = 0;
    let end = lines.length;
    while (start < end && !stripD2ColorCodes(lines[start]).trim()) {
      start++;
    }
    while (end > start && !stripD2ColorCodes(lines[end - 1]).trim()) {
      end--;
    }
    return lines.slice(start, end);
  }, [item.description]);

  return (
    <div
      className="whitespace-nowrap bg-zinc-900/95 p-3 shadow-xl ring-1 ring-zinc-700"
      style={{ fontFamily, fontVariantCaps: "small-caps" }}
    >
      {/* Item header */}
      {showHeader && item.header && (
        <div className="mb-1 text-center font-medium text-zinc-100">
          {item.header}
        </div>
      )}

      {/* Item name */}
      <div className="text-center text-lg font-semibold text-zinc-100">
        {item.name}
      </div>

      {/* Item sprite */}
      {showSprite && (
        <div className="mt-2 flex justify-center">
          <ItemSprite
            code={item.code}
            colorShift={item.itemColor}
            ethereal={item.description?.includes("Ethereal")}
            sockets={item.sockets}
            alt={item.name}
          />
        </div>
      )}

      {/* Item description with D2 color codes */}
      {descriptionLines.length > 0 && (
        <div className="mt-2 border-t border-zinc-700 pt-2">
          {descriptionLines.map((line, i) => (
            <ColoredDescriptionLine key={i} text={line} />
          ))}
        </div>
      )}
    </div>
  );
});

export const ItemTooltip = memo(function ItemTooltip({
  item,
  children,
  showSprite = true,
}: ItemTooltipProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const [showBelow, setShowBelow] = useState(false);
  const [isHovered, setIsHovered] = useState(false);
  const [isVisible, setIsVisible] = useState(false);
  const [position, setPosition] = useState({ top: 0, left: 0, arrowLeft: 0 });

  const visible = isHovered || isVisible;

  /** Calculate optimal tooltip position using fixed coordinates */
  const updatePosition = useCallback(() => {
    if (!containerRef.current || !tooltipRef.current) return;

    const triggerRect = containerRef.current.getBoundingClientRect();
    const tooltipWidth = tooltipRef.current.offsetWidth;
    const tooltipHeight = tooltipRef.current.offsetHeight;
    const viewportWidth = window.innerWidth;
    // Use 0 margin on mobile (< 640px), otherwise use TOOLTIP_MARGIN
    const margin = viewportWidth < 640 ? 0 : TOOLTIP_MARGIN;
    const availableWidth = viewportWidth - margin * 2;

    // Vertical: flip below if not enough space above
    const below = triggerRect.top < tooltipHeight + TOOLTIP_MARGIN;
    setShowBelow(below);
    const top = below ? triggerRect.bottom + 8 : triggerRect.top - tooltipHeight - 8;

    // Horizontal: center on trigger, constrain to viewport
    const triggerCenterX = triggerRect.left + triggerRect.width / 2;
    let left: number;
    if (tooltipWidth >= availableWidth) {
      left = (viewportWidth - tooltipWidth) / 2;
    } else {
      left = triggerCenterX - tooltipWidth / 2;
      if (left < margin) left = margin;
      if (left + tooltipWidth > viewportWidth - margin)
        left = viewportWidth - margin - tooltipWidth;
    }

    setPosition({ top, left, arrowLeft: triggerCenterX - left });
  }, []);

  const handleMouseEnter = useCallback(() => {
    setIsHovered(true);
    updatePosition();
  }, [updatePosition]);

  const handleMouseLeave = useCallback(() => {
    setIsHovered(false);
  }, []);

  const handleTouchStart = useCallback(() => {
    setIsVisible((prev) => {
      if (!prev) {
        updatePosition();
      }
      return !prev;
    });
  }, [updatePosition]);

  // Close tooltip when touching outside
  useEffect(() => {
    if (!isVisible) return;

    const handleTouchOutside = (e: TouchEvent) => {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setIsVisible(false);
      }
    };

    document.addEventListener("touchstart", handleTouchOutside);
    return () => document.removeEventListener("touchstart", handleTouchOutside);
  }, [isVisible]);

  return (
    <div
      ref={containerRef}
      className="inline-block"
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      onTouchStart={handleTouchStart}
    >
      {children}

      {/* Tooltip rendered via portal to escape scroll container clipping */}
      {createPortal(
        <div
          ref={tooltipRef}
          className={clsx(
            "fixed z-[60] pointer-events-none transition-opacity",
            visible ? "opacity-100" : "opacity-0",
          )}
          style={{ top: position.top, left: position.left }}
          role="tooltip"
        >
          <ItemTooltipContent item={item} showSprite={showSprite} />

          {/* Tooltip arrow - points to trigger center */}
          <div
            className={clsx(
              "absolute border-4 border-transparent",
              showBelow
                ? "bottom-full border-b-zinc-700"
                : "top-full border-t-zinc-700",
            )}
            style={{ left: position.arrowLeft, transform: "translateX(-50%)" }}
          />
        </div>,
        document.body,
      )}
    </div>
  );
});
