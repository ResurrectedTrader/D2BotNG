import {
  useState,
  useRef,
  useEffect,
  useCallback,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import clsx from "clsx";

export interface TooltipProps {
  /** Content to show in the tooltip */
  content: ReactNode;
  /** The element that triggers the tooltip */
  children: ReactNode;
  /** Additional CSS classes for the wrapper */
  className?: string;
}

/**
 * A tooltip that appears on hover (desktop) or tap (mobile).
 * Uses a portal to escape overflow containers.
 * Tap anywhere else to dismiss on mobile.
 */
export function Tooltip({ content, children, className }: TooltipProps) {
  const containerRef = useRef<HTMLSpanElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const [isHovered, setIsHovered] = useState(false);
  const [isTouchVisible, setIsTouchVisible] = useState(false);
  const [position, setPosition] = useState({ top: 0, left: 0 });

  const isVisible = isHovered || isTouchVisible;

  const updatePosition = useCallback(() => {
    if (!containerRef.current) return;

    const rect = containerRef.current.getBoundingClientRect();
    const tooltipHeight = tooltipRef.current?.offsetHeight ?? 40;
    const tooltipWidth = tooltipRef.current?.offsetWidth ?? 200;

    // Position below by default, flip above if not enough space
    let top = rect.bottom + 8;
    if (top + tooltipHeight > window.innerHeight - 16) {
      top = rect.top - tooltipHeight - 8;
    }

    // Align left edge with trigger, but keep within viewport
    let left = rect.left;
    if (left + tooltipWidth > window.innerWidth - 16) {
      left = window.innerWidth - tooltipWidth - 16;
    }
    if (left < 16) {
      left = 16;
    }

    setPosition({ top, left });
  }, []);

  const handleMouseEnter = useCallback(() => {
    updatePosition();
    setIsHovered(true);
  }, [updatePosition]);

  const handleMouseLeave = useCallback(() => {
    setIsHovered(false);
  }, []);

  const handleTouchStart = useCallback(
    (e: React.TouchEvent) => {
      e.stopPropagation();
      updatePosition();
      setIsTouchVisible((prev) => !prev);
    },
    [updatePosition],
  );

  // Close tooltip when touching outside
  useEffect(() => {
    if (!isTouchVisible) return;

    const handleTouchOutside = (e: TouchEvent) => {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setIsTouchVisible(false);
      }
    };

    document.addEventListener("touchstart", handleTouchOutside);
    return () => document.removeEventListener("touchstart", handleTouchOutside);
  }, [isTouchVisible]);

  if (!content) return <>{children}</>;

  return (
    <span
      ref={containerRef}
      className={clsx("inline-flex", className)}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      onTouchStart={handleTouchStart}
    >
      {children}
      {isVisible &&
        createPortal(
          <div
            ref={tooltipRef}
            className="fixed z-50 pointer-events-none"
            style={{ top: position.top, left: position.left }}
            role="tooltip"
          >
            <div className="bg-zinc-800 text-zinc-200 text-xs rounded-lg px-3 py-2 shadow-lg ring-1 ring-zinc-700 max-w-xs whitespace-normal break-words">
              {content}
            </div>
          </div>,
          document.body,
        )}
    </span>
  );
}
