import { useState, useEffect, useRef, useCallback } from "react";
import { createPortal } from "react-dom";
import clsx from "clsx";
import type { DropdownItem } from "./Dropdown";

interface ContextMenuPosition {
  x: number;
  y: number;
}

interface ContextMenuPortalProps {
  items: DropdownItem[];
  position: ContextMenuPosition;
  onClose: () => void;
}

function ContextMenuPortal({
  items,
  position,
  onClose,
}: ContextMenuPortalProps) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleMouseDown = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        onClose();
      }
    };
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    const handleScroll = () => onClose();

    document.addEventListener("mousedown", handleMouseDown);
    document.addEventListener("keydown", handleKeyDown);
    document.addEventListener("scroll", handleScroll, true);

    return () => {
      document.removeEventListener("mousedown", handleMouseDown);
      document.removeEventListener("keydown", handleKeyDown);
      document.removeEventListener("scroll", handleScroll, true);
    };
  }, [onClose]);

  // Adjust position to keep menu within viewport
  useEffect(() => {
    if (!menuRef.current) return;
    const rect = menuRef.current.getBoundingClientRect();
    const el = menuRef.current;

    if (rect.right > window.innerWidth) {
      el.style.left = `${window.innerWidth - rect.width - 8}px`;
    }
    if (rect.bottom > window.innerHeight) {
      el.style.top = `${window.innerHeight - rect.height - 8}px`;
    }
  }, [position]);

  return createPortal(
    <div
      ref={menuRef}
      style={{ position: "fixed", left: position.x, top: position.y }}
      className={clsx(
        "z-50 w-48 rounded-lg",
        "bg-zinc-800 ring-1 ring-zinc-700 shadow-lg",
        "focus:outline-none",
      )}
      onContextMenu={(e) => e.preventDefault()}
    >
      <div className="py-1">
        {items.map((item, index) => (
          <button
            key={index}
            type="button"
            onClick={() => {
              if (!item.disabled) {
                item.onClick();
                onClose();
              }
            }}
            disabled={item.disabled}
            className={clsx(
              "flex w-full items-center gap-2 px-4 py-2 text-sm",
              "disabled:pointer-events-none disabled:opacity-50",
              "transition-colors",
              item.danger
                ? "hover:bg-red-600 hover:text-white text-red-400"
                : "hover:bg-zinc-700 hover:text-zinc-100 text-zinc-300",
            )}
          >
            {item.icon && <item.icon className="h-4 w-4" aria-hidden="true" />}
            {item.label}
          </button>
        ))}
      </div>
    </div>,
    document.body,
  );
}

export function useContextMenu(items: DropdownItem[]) {
  const [position, setPosition] = useState<ContextMenuPosition | null>(null);

  const onContextMenu = useCallback((e: React.MouseEvent) => {
    // Only show on devices with hover capability (desktop)
    if (!window.matchMedia("(hover: hover)").matches) return;
    e.preventDefault();
    setPosition({ x: e.clientX, y: e.clientY });
  }, []);

  const close = useCallback(() => setPosition(null), []);

  const contextMenu = position ? (
    <ContextMenuPortal items={items} position={position} onClose={close} />
  ) : null;

  return { contextMenu, onContextMenu };
}
