/**
 * ConsoleOutput component
 *
 * Displays console log entries with timestamps and colored messages.
 * Uses MessageColor from the event stream for coloring.
 * Shows item tooltip on hover for messages with items.
 */

import { useEffect, memo, useMemo, useState, Fragment, useRef } from "react";
import { createPortal } from "react-dom";
import clsx from "clsx";
import { MessageColor } from "@/generated/events_pb";
import type { Item } from "@/generated/items_pb";
import type { MessageEntry } from "@/stores/event-store";
import { ItemTooltipContent } from "@/features/items";

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
 * Format a Date for display - returns date and time parts separately
 */
function formatTimestamp(timestamp: Date): { date: string; time: string } {
  const year = timestamp.getFullYear();
  const month = String(timestamp.getMonth() + 1).padStart(2, "0");
  const day = String(timestamp.getDate()).padStart(2, "0");
  const hours = String(timestamp.getHours()).padStart(2, "0");
  const minutes = String(timestamp.getMinutes()).padStart(2, "0");
  const seconds = String(timestamp.getSeconds()).padStart(2, "0");
  return {
    date: `${year}-${month}-${day}`,
    time: `${hours}:${minutes}:${seconds}`,
  };
}

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
  const { date, time } = formatTimestamp(entry.timestamp);
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
    <div className="col-span-full grid grid-cols-subgrid gap-2 border-b border-zinc-800/50 px-2 py-0.5 font-mono text-xs hover:bg-zinc-800/50">
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

  // Memoize reversed array to avoid recreating on every render
  const reversedMessages = useMemo(() => [...messages].reverse(), [messages]);

  // Auto-scroll to bottom when new messages arrive
  useEffect(() => {
    if (autoScroll && containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight;
    }
  }, [messages.length, autoScroll]);

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
        <div className="grid grid-cols-[auto_1fr] sm:grid-cols-[auto_auto_1fr]">
          {/* Render in reverse order so newest is at bottom */}
          {reversedMessages.map((entry) => (
            <ConsoleRow key={entry.id} entry={entry} />
          ))}
        </div>
      )}
    </div>
  );
}
