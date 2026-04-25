/**
 * ConsoleOutput component
 *
 * Displays console log entries with timestamps and colored messages.
 * Uses MessageColor from the event stream for coloring.
 * Shows item tooltip on hover for messages with items.
 * Uses virtualized scrolling for performance with large message lists.
 */

import { useEffect, memo, useState, Fragment, useRef } from "react";
import { createPortal } from "react-dom";
import { useVirtualizer } from "@tanstack/react-virtual";
import clsx from "clsx";
import { MessageColor } from "@/generated/events_pb";
import type { Item } from "@/generated/items_pb";
import type { MessageEntry } from "@/stores/event-store";
import { ItemTooltipContent } from "@/features/items";
import { formatDateTimeParts } from "@/lib/format";

/** Message entry with optional source label for display */
export type MessageEntryWithLabel = MessageEntry & {
  sourceLabel?: string;
};

interface ConsoleOutputProps {
  /** Array of console entries to display */
  messages: MessageEntryWithLabel[];
  /** Whether to auto-scroll to bottom on new messages */
  autoScroll?: boolean;
  /** Optional CSS class */
  className?: string;
}

/**
 * Color classes for MessageColor enum values
 */
const messageColorClasses: Record<MessageColor, string> = {
  [MessageColor.COLOR_DEFAULT]: "text-zinc-100",
  [MessageColor.COLOR_BLUE]: "text-blue-400",
  [MessageColor.COLOR_GREEN]: "text-green-400",
  [MessageColor.COLOR_GOLD]: "text-d2-gold",
  [MessageColor.COLOR_BROWN]: "text-amber-700",
  [MessageColor.COLOR_ORANGE]: "text-orange-400",
  [MessageColor.COLOR_RED]: "text-red-400",
  [MessageColor.COLOR_GRAY]: "text-zinc-500",
};

/**
 * URL regex pattern for detecting hyperlinks
 */
const URL_REGEX = /(https?:\/\/[^\s<>"{}|\\^`[\]]+)/g;

/**
 * Parse text and convert URLs to clickable links
 */
function renderContentWithLinks(content: string): React.ReactNode {
  const parts = content.split(URL_REGEX);
  if (parts.length === 1) {
    return content;
  }

  return parts.map((part, index) => {
    if (URL_REGEX.test(part)) {
      // Reset lastIndex since we're reusing the regex
      URL_REGEX.lastIndex = 0;
      return (
        <a
          key={index}
          href={part}
          target="_blank"
          rel="noopener noreferrer"
          className="underline hover:text-blue-300"
        >
          {part}
        </a>
      );
    }
    return <Fragment key={index}>{part}</Fragment>;
  });
}

/** Margin from viewport edges (0 on mobile) */
const TOOLTIP_MARGIN = 16;

/**
 * Cursor-following tooltip rendered via portal with viewport bounds checking
 */
const CursorTooltip = memo(function CursorTooltip({
  item,
  cursorPos,
}: {
  item: Item;
  cursorPos: { x: number; y: number };
}) {
  const tooltipRef = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ x: 0, y: 0 });

  useEffect(() => {
    if (!tooltipRef.current) return;

    const tooltipWidth = tooltipRef.current.offsetWidth;
    const tooltipHeight = tooltipRef.current.offsetHeight;
    const viewportWidth = window.innerWidth;
    const viewportHeight = window.innerHeight;
    const margin = viewportWidth < 640 ? 0 : TOOLTIP_MARGIN;

    // Default position: right of cursor, above cursor
    let x = cursorPos.x + 12;
    let y = cursorPos.y - 12 - tooltipHeight;

    // Check right edge overflow - flip to left of cursor
    if (x + tooltipWidth > viewportWidth - margin) {
      x = cursorPos.x - 12 - tooltipWidth;
    }

    // Check left edge overflow - clamp to left margin
    if (x < margin) {
      x = margin;
    }

    // Check top edge overflow - flip below cursor
    if (y < margin) {
      y = cursorPos.y + 12;
    }

    // Check bottom edge overflow - clamp to bottom
    if (y + tooltipHeight > viewportHeight - margin) {
      y = viewportHeight - margin - tooltipHeight;
    }

    setPosition({ x, y });
  }, [cursorPos]);

  return createPortal(
    <div
      ref={tooltipRef}
      className="pointer-events-none fixed z-[9999]"
      style={{
        left: position.x,
        top: position.y,
      }}
    >
      <ItemTooltipContent item={item} />
    </div>,
    document.body,
  );
});

/**
 * Single console entry row
 */
const ConsoleRow = memo(function ConsoleRow({
  entry,
}: {
  entry: MessageEntryWithLabel;
}) {
  const colorClass =
    messageColorClasses[entry.color] ||
    messageColorClasses[MessageColor.COLOR_DEFAULT];
  const { date, time } = formatDateTimeParts(entry.timestamp);
  const [cursorPos, setCursorPos] = useState<{ x: number; y: number } | null>(
    null,
  );
  const rowRef = useRef<HTMLSpanElement>(null);

  const hasItem = entry.item != null;

  const handleMouseMove = (e: React.MouseEvent) => {
    setCursorPos({ x: e.clientX, y: e.clientY });
  };

  const handleMouseLeave = () => {
    setCursorPos(null);
  };

  const handleTouchStart = (e: React.TouchEvent) => {
    if (!hasItem) return;
    const touch = e.touches[0];
    setCursorPos((prev) =>
      prev ? null : { x: touch.clientX, y: touch.clientY },
    );
  };

  // Close tooltip when touching outside
  useEffect(() => {
    if (!cursorPos) return;

    const handleTouchOutside = (e: TouchEvent) => {
      if (rowRef.current && !rowRef.current.contains(e.target as Node)) {
        setCursorPos(null);
      }
    };

    document.addEventListener("touchstart", handleTouchOutside);
    return () => document.removeEventListener("touchstart", handleTouchOutside);
  }, [cursorPos]);

  return (
    <div className="grid grid-cols-[auto_1fr] gap-2 border-b border-zinc-800/50 px-2 py-0.5 font-mono text-xs hover:bg-zinc-800/50 sm:grid-cols-[auto_auto_1fr]">
      {/* Timestamp - hidden on mobile, time only on sm, full on lg */}
      <span className="hidden whitespace-nowrap text-zinc-500 sm:inline">
        <span className="hidden lg:inline">{date} </span>
        {time}
      </span>

      {/* Source label */}
      <span className="truncate text-d2-gold">{entry.sourceLabel ?? ""}</span>

      {/* Message content */}
      <span
        ref={rowRef}
        className={clsx(
          "min-w-0 break-all",
          colorClass,
          hasItem && "cursor-pointer underline decoration-dotted",
        )}
        onMouseMove={hasItem ? handleMouseMove : undefined}
        onMouseLeave={hasItem ? handleMouseLeave : undefined}
        onTouchStart={hasItem ? handleTouchStart : undefined}
      >
        {renderContentWithLinks(entry.content)}
        {cursorPos && entry.item && (
          <CursorTooltip item={entry.item} cursorPos={cursorPos} />
        )}
      </span>
    </div>
  );
});

export function ConsoleOutput({
  messages,
  autoScroll = true,
  className,
}: ConsoleOutputProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const isAtBottomRef = useRef(true);

  const virtualizer = useVirtualizer({
    count: messages.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => 24,
    overscan: 50,
  });

  // Track whether user is at the bottom of the scroll container
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const handleScroll = () => {
      isAtBottomRef.current =
        el.scrollHeight - el.scrollTop - el.clientHeight < 50;
    };

    el.addEventListener("scroll", handleScroll, { passive: true });
    return () => el.removeEventListener("scroll", handleScroll);
  }, []);

  // Auto-scroll to bottom only when user is already at the bottom
  useEffect(() => {
    if (autoScroll && isAtBottomRef.current && messages.length > 0) {
      virtualizer.scrollToIndex(messages.length - 1, { align: "end" });
    }
  }, [messages.length, autoScroll, virtualizer]);

  return (
    <div
      ref={containerRef}
      className={clsx(
        "overflow-auto rounded-lg bg-zinc-950 ring-1 ring-zinc-800",
        className,
      )}
    >
      {messages.length === 0 ? (
        <div className="flex h-full items-center justify-center text-zinc-500">
          No console output yet...
        </div>
      ) : (
        <div
          style={{
            height: virtualizer.getTotalSize(),
            width: "100%",
            position: "relative",
          }}
        >
          {virtualizer.getVirtualItems().map((virtualItem) => (
            <div
              key={virtualItem.key}
              data-index={virtualItem.index}
              ref={virtualizer.measureElement}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${virtualItem.start}px)`,
              }}
            >
              <ConsoleRow entry={messages[virtualItem.index]} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
