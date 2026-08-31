/**
 * ItemTooltip component
 *
 * Displays a tooltip with item details styled with the item's quality color.
 * The panel is a `position: fixed` element portalled to `document.body` — it escapes the scroll
 * containers and overflow clipping of the grids it is triggered from — placed at viewport
 * coordinates measured off the trigger, flipping below it when there is no room above.
 */

import {
  memo,
  type ReactNode,
  useCallback,
  useMemo,
  useRef,
  useState,
  useEffect,
  useLayoutEffect,
} from "react";
import { createPortal } from "react-dom";
import clsx from "clsx";
import { useHeldKey } from "@/hooks/useHeldKey";
import { useSettings } from "@/stores/event-store";
import { ItemSprite } from "@/lib/rendering";
import {
  isEthereal,
  parseD2ColoredText,
  type ColoredTextSegment,
  type RenderableItem,
} from "./item-utils";
import { TooltipLine, useTooltipTextStyle } from "./TooltipText";

/** Extra margin when calculating tooltip position (in pixels) */
const TOOLTIP_MARGIN = 16;

/** Literal backslash-n for splitting lines */
const ESCAPED_NEWLINE = String.raw`\n`;

export interface ItemTooltipProps {
  /** The item to display details for */
  item: RenderableItem;
  /** The trigger element */
  children: ReactNode;
  /**
   * Whether to show the item sprite in the tooltip. Required, unlike on the content panel: a
   * trigger is itself something the reader is already looking at — usually the sprite — so which
   * of the two draws it is a decision the call site has to make rather than inherit.
   */
  showSprite: boolean;
  /**
   * Hover, when the caller already tracks it. Left out, this watches its own wrapper.
   *
   * It exists for the call site that has a second reason to know — a grid cell dims or decorates
   * the item under the pointer — because two states for one pointer is not merely redundant when
   * the decoration CHANGES THE TRIGGER'S SIZE. A cell that overlays socket markers on hover
   * re-renders the sprite at the item's full grid footprint rather than the artwork's own bounds,
   * which moves the wrapper this would otherwise be measuring, under the pointer that opened it.
   */
  open?: boolean;
}

/**
 * The tooltip content panel - can be used standalone or within ItemTooltip
 */
export const ItemTooltipContent = memo(function ItemTooltipContent({
  item,
  showSprite = true,
  breakdown,
}: {
  item: RenderableItem;
  showSprite?: boolean;
  /**
   * Which view to draw, for a caller that has already decided. Left out, it follows the Ctrl key
   * live, which is what a tooltip on screen should do.
   *
   * It is set when this is rendered off-screen to be captured as an image. That render takes the
   * best part of a second — fonts, sprite, rasterisation — so following the key live would decide
   * the contents of the PNG by whether the reader happened to still be holding Ctrl when the
   * rasteriser got to it, rather than by what they right-clicked on.
   */
  breakdown?: boolean;
}) {
  const settings = useSettings();
  const textStyle = useTooltipTextStyle();
  const showHeader = settings?.display?.showItemHeader ?? false;

  // The alternate view, while Ctrl is down and this item can produce one. Computed inside the
  // memo so holding the key costs one pass, not one per render, and releasing it throws the
  // result away rather than keeping a breakdown alive for every hovered item.
  const ctrlHeld = useHeldKey("Control");
  const wantsDetail = breakdown ?? ctrlHeld;
  const detailLines = useMemo(
    () => (wantsDetail && item.detail ? item.detail.lines() : null),
    [wantsDetail, item.detail],
  );

  // Parsed once per line, here, rather than once to decide whether the line is blank and again to
  // draw it: the colour runs ARE the answer to both questions, since a line with nothing but
  // markers has no text in any of its runs.
  //
  // Null while the breakdown is up, because the description is not drawn then and building it is
  // not free: a source that renders its own text does a full pass over the item's stats against
  // the game tables, so hovering with Ctrl already held paid for two renders to show one.
  const descriptionLines = useMemo<ColoredTextSegment[][] | null>(() => {
    if (detailLines !== null) return null;
    // `describe` for a source that renders its own text (a v2 capture, which carries none);
    // `description` for one that arrived with it. Asked for here rather than up front, because
    // this component is mounted only while the tooltip is on screen.
    const text = item.describe?.() ?? item.description;
    if (!text) return [];
    const cleanDesc = text.split("$")[0];
    // Split on literal \n (escaped) or actual newlines
    const raw = cleanDesc.includes(ESCAPED_NEWLINE)
      ? cleanDesc.split(ESCAPED_NEWLINE)
      : cleanDesc.split("\n");
    const lines = raw.map((line) => parseD2ColoredText(line));
    const blank = (segments: ColoredTextSegment[]) =>
      segments.every((s) => !s.text.trim());
    let start = 0;
    let end = lines.length;
    while (start < end && blank(lines[start])) {
      start++;
    }
    while (end > start && blank(lines[end - 1])) {
      end--;
    }
    return lines.slice(start, end);
  }, [detailLines, item]);

  return (
    <div
      className="whitespace-nowrap bg-zinc-900/95 p-3 shadow-xl ring-1 ring-zinc-700"
      style={textStyle}
    >
      {/* Item header */}
      {showHeader && item.header && (
        <div className="mb-1 text-center font-medium text-zinc-100">
          {item.header}
        </div>
      )}

      {/* Item name — only when nothing below will name it. Both the description and the Ctrl
          breakdown are the full game tooltip and already lead with the name, so showing the
          title too would duplicate it. */}
      {descriptionLines?.length === 0 && (
        <div className="text-center text-lg font-semibold text-zinc-100">
          {item.name}
        </div>
      )}

      {/* Item sprite (with sockets overlaid) */}
      {showSprite && (
        <div className="mt-2 flex justify-center">
          <ItemSprite
            code={item.code}
            colorShift={item.itemColor}
            invTrans={item.invTrans}
            ethereal={isEthereal(item)}
            sockets={item.sockets}
            hd={item.hd}
            alt={item.name}
          />
        </div>
      )}

      {/* While Ctrl is held: the same item with its socket fillers drawn as their own blocks and
          each stat annotated with the span it could have rolled within. An item that offers this
          but has nothing to say still shows the alternate view — silently falling back would read
          as the key not working. */}
      {detailLines !== null && (
        <div
          className={clsx(
            "mt-2",
            showSprite && "border-t border-zinc-700 pt-2",
          )}
        >
          {detailLines.map((line, i) => (
            <TooltipLine key={i} segments={line.segments} />
          ))}
          {detailLines.length === 0 && (
            <div className="text-center text-xs text-zinc-500">
              Nothing to show for this item.
            </div>
          )}
        </div>
      )}

      {/* Item description with D2 color codes. The horizontal separator only
          divides the description from the sprite above, so drop it (and the
          divider padding) when the sprite is hidden. */}
      {descriptionLines !== null && descriptionLines.length > 0 && (
        <div
          className={clsx(
            "mt-2",
            showSprite && "border-t border-zinc-700 pt-2",
          )}
        >
          {descriptionLines.map((segments, i) => (
            <TooltipLine key={i} segments={segments} />
          ))}
        </div>
      )}
    </div>
  );
});

/** Where the panel ended up, once it existed to be measured. */
interface Placement {
  top: number;
  left: number;
  /** Distance from the panel's own left edge to the trigger's centre, for the arrow. */
  arrowLeft: number;
  /** Flipped under the trigger because there was no room above it. */
  below: boolean;
}

export const ItemTooltip = memo(function ItemTooltip({
  item,
  children,
  showSprite,
  open,
}: ItemTooltipProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const [isHovered, setIsHovered] = useState(false);
  const [isVisible, setIsVisible] = useState(false);
  const [placement, setPlacement] = useState<Placement | null>(null);

  const controlled = open !== undefined;
  const visible = (controlled ? open : isHovered) || isVisible;

  const measure = useCallback(() => {
    const trigger = containerRef.current;
    const tooltip = tooltipRef.current;
    if (!trigger || !tooltip) return;

    const triggerRect = trigger.getBoundingClientRect();
    const tooltipWidth = tooltip.offsetWidth;
    const tooltipHeight = tooltip.offsetHeight;
    const viewportWidth = window.innerWidth;
    // Use 0 margin on mobile (< 640px), otherwise use TOOLTIP_MARGIN
    const margin = viewportWidth < 640 ? 0 : TOOLTIP_MARGIN;
    const availableWidth = viewportWidth - margin * 2;

    // Vertical: flip below if not enough space above
    const below = triggerRect.top < tooltipHeight + TOOLTIP_MARGIN;
    const unclamped = below
      ? triggerRect.bottom + 8
      : triggerRect.top - tooltipHeight - 8;
    // Flipping only chooses a side; it does not guarantee the panel fits on that side. The Ctrl
    // breakdown is the tallest thing this ever renders, and near the bottom of the window it ran
    // off the end of the screen — which a pointer-events-none panel offers no way to scroll to.
    const top = Math.max(
      margin,
      Math.min(unclamped, window.innerHeight - margin - tooltipHeight),
    );

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

    const next: Placement = {
      top,
      left,
      arrowLeft: triggerCenterX - left,
      below,
    };
    // Same position in, same object out: an observed re-measure that changed nothing must not
    // re-render, or every resize notification would schedule another one.
    setPlacement((prev) =>
      prev &&
      prev.top === next.top &&
      prev.left === next.left &&
      prev.arrowLeft === next.arrowLeft &&
      prev.below === next.below
        ? prev
        : next,
    );
  }, []);

  /**
   * Measured after mount rather than before, which is what lets the panel exist only while it is
   * on screen.
   *
   * Its own width and height decide everything here — whether it flips below the trigger, and how
   * far it has to be nudged back inside the viewport — and neither can be read until it has been
   * laid out. So the first pass renders it off-screen and this reads it there; a layout effect,
   * because it must land before the browser paints or the reader sees it at the wrong place.
   *
   * Every item used to keep a hidden panel mounted for exactly this measurement, which put a live
   * tooltip — sprite, description, key listener and all — behind every cell of every grid on the
   * page.
   *
   * The observer is what keeps it right afterwards. The panel's size is not fixed for the life of
   * a hover: holding Ctrl swaps the description for the breakdown, which is both taller and wider,
   * and a placement computed from the old height anchors the taller panel over the item it
   * describes.
   *
   * The listeners cover the other way a placement goes stale — the panel is fixed to the viewport
   * while the trigger moves within it, so scrolling with the cursor parked on the same item leaves
   * the panel behind by the whole delta, arrow pointing at nothing. Capture phase, because what
   * scrolls here is the layout's inner `main` element rather than the document, and a scroll event
   * does not bubble out of it.
   */
  useLayoutEffect(() => {
    if (!visible) {
      setPlacement(null);
      return;
    }
    measure();

    window.addEventListener("scroll", measure, true);
    window.addEventListener("resize", measure);

    const tooltip = tooltipRef.current;
    const observer = new ResizeObserver(measure);
    if (tooltip) observer.observe(tooltip);

    return () => {
      window.removeEventListener("scroll", measure, true);
      window.removeEventListener("resize", measure);
      observer.disconnect();
    };
  }, [visible, measure]);

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
      onMouseEnter={controlled ? undefined : () => setIsHovered(true)}
      onMouseLeave={controlled ? undefined : () => setIsHovered(false)}
      onTouchStart={() => setIsVisible((prev) => !prev)}
    >
      {children}

      {/* Portalled to escape the scroll container's clipping, and mounted only while shown. Until
          it has been measured it sits off-screen rather than at the origin, so the measuring pass
          cannot flash a panel in the top-left corner. */}
      {visible &&
        createPortal(
          <div
            ref={tooltipRef}
            className={clsx(
              "fixed z-[60] pointer-events-none transition-opacity",
              placement ? "opacity-100" : "opacity-0",
            )}
            style={
              placement
                ? { top: placement.top, left: placement.left }
                : { top: 0, left: -9999 }
            }
            role="tooltip"
          >
            <ItemTooltipContent item={item} showSprite={showSprite} />

            {/* Tooltip arrow - points to trigger center */}
            <div
              className={clsx(
                "absolute border-4 border-transparent",
                placement?.below
                  ? "bottom-full border-b-zinc-700"
                  : "top-full border-t-zinc-700",
              )}
              style={{
                left: placement?.arrowLeft ?? 0,
                transform: "translateX(-50%)",
              }}
            />
          </div>,
          document.body,
        )}
    </div>
  );
});
