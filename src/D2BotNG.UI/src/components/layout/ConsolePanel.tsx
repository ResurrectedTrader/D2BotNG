/**
 * ConsolePanel component
 *
 * Collapsible footer panel showing console messages.
 * Supports filtering by source: All, System (global), or individual profiles.
 */

import { useState, useEffect, useCallback, useMemo, useRef } from "react";
import clsx from "clsx";
import {
  ChevronUpIcon,
  ChevronDownIcon,
  TrashIcon,
  ChevronDownIcon as DropdownIcon,
  CheckIcon,
} from "@heroicons/react/24/outline";
import {
  ConsoleOutput,
  type MessageEntryWithLabel,
} from "@/features/console/ConsoleOutput";
import {
  useAllMessages,
  useProfiles,
  type MessageEntry,
} from "@/stores/event-store";
import { useClearConsole } from "@/hooks/useClearConsole";

const STORAGE_KEY = "d2bot-console-height";
const STORAGE_COLLAPSED_KEY = "d2bot-console-collapsed";
const STORAGE_SOURCES_KEY = "d2bot-console-sources";
const STORAGE_FILTER_KEY = "d2bot-console-filter";
const DEFAULT_HEIGHT = 200;
const MIN_HEIGHT = 100;
const MAX_HEIGHT = 500;

export function ConsolePanel() {
  const allMessages = useAllMessages();
  const profilesData = useProfiles();
  const { clearMessages, isClearing } = useClearConsole();

  // Multi-select dropdown state
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Selected sources state (empty array = all sources)
  const [selectedSources, setSelectedSources] = useState<string[]>(() => {
    if (typeof window === "undefined") return [];
    try {
      const stored = localStorage.getItem(STORAGE_SOURCES_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });

  // Regex filter state
  const [filter, setFilter] = useState(() => {
    if (typeof window === "undefined") return "";
    return localStorage.getItem(STORAGE_FILTER_KEY) || "";
  });
  const [filterError, setFilterError] = useState(false);

  const [isCollapsed, setIsCollapsed] = useState(() => {
    if (typeof window === "undefined") return false;
    const stored = localStorage.getItem(STORAGE_COLLAPSED_KEY);
    return stored === "true";
  });

  const [height, setHeight] = useState(() => {
    if (typeof window === "undefined") return DEFAULT_HEIGHT;
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? parseInt(stored, 10) : DEFAULT_HEIGHT;
  });

  const [isResizing, setIsResizing] = useState(false);

  // Build available source options from actual sources in messages + profiles
  const availableSources = useMemo(() => {
    const sources: string[] = [];

    // Always include System and Service
    sources.push("System");
    sources.push("Service");

    // Add profiles (name is both value and label now)
    profilesData.forEach((p) => {
      sources.push(p.profile.name);
    });

    return sources;
  }, [profilesData]);

  // Close dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        dropdownRef.current &&
        !dropdownRef.current.contains(event.target as Node)
      ) {
        setIsDropdownOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Format timestamp for filtering (same format as ConsoleOutput)
  const formatTimestamp = (timestamp: Date): string => {
    const year = timestamp.getFullYear();
    const month = String(timestamp.getMonth() + 1).padStart(2, "0");
    const day = String(timestamp.getDate()).padStart(2, "0");
    const hours = String(timestamp.getHours()).padStart(2, "0");
    const minutes = String(timestamp.getMinutes()).padStart(2, "0");
    const seconds = String(timestamp.getSeconds()).padStart(2, "0");
    return `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`;
  };

  // Build messages based on source selection and regex filter
  const messages: MessageEntryWithLabel[] = useMemo(() => {
    let filtered: MessageEntryWithLabel[];

    // Empty selectedSources = show all, otherwise filter to selected
    const showAll =
      selectedSources.length === 0 ||
      availableSources.every((s) => selectedSources.includes(s));

    if (showAll) {
      // All messages with source labels
      filtered = allMessages.map((msg: MessageEntry) => {
        return { ...msg, sourceLabel: msg.source };
      });
    } else {
      // Filter by selected sources - with labels since multiple
      filtered = allMessages
        .filter((msg) => selectedSources.includes(msg.source))
        .map((msg) => ({ ...msg, sourceLabel: msg.source }));
    }

    // Apply regex filter if present
    if (filter.trim()) {
      try {
        const regex = new RegExp(filter, "i");
        filtered = filtered.filter((msg) => {
          const timestamp = formatTimestamp(msg.timestamp);
          return (
            regex.test(msg.content) ||
            regex.test(msg.source) ||
            regex.test(timestamp)
          );
        });
        setFilterError(false);
      } catch {
        // Invalid regex - show error state but don't filter
        setFilterError(true);
      }
    } else {
      setFilterError(false);
    }

    return filtered;
  }, [selectedSources, availableSources, allMessages, filter]);

  // Persist selected sources
  useEffect(() => {
    if (typeof window === "undefined") return;
    localStorage.setItem(STORAGE_SOURCES_KEY, JSON.stringify(selectedSources));
  }, [selectedSources]);

  // Persist filter
  useEffect(() => {
    if (typeof window === "undefined") return;
    localStorage.setItem(STORAGE_FILTER_KEY, filter);
  }, [filter]);

  // Persist collapsed state
  useEffect(() => {
    if (typeof window === "undefined") return;
    localStorage.setItem(STORAGE_COLLAPSED_KEY, String(isCollapsed));
  }, [isCollapsed]);

  // Persist height
  useEffect(() => {
    if (typeof window === "undefined") return;
    localStorage.setItem(STORAGE_KEY, String(height));
  }, [height]);

  // Handle resize drag
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      setIsResizing(true);

      const startY = e.clientY;
      const startHeight = height;

      const handleMouseMove = (e: MouseEvent) => {
        const delta = startY - e.clientY;
        const newHeight = Math.min(
          MAX_HEIGHT,
          Math.max(MIN_HEIGHT, startHeight + delta),
        );
        setHeight(newHeight);
      };

      const handleMouseUp = () => {
        setIsResizing(false);
        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
      };

      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
    },
    [height],
  );

  // Touch resize for mobile
  const handleTouchStart = useCallback(
    (e: React.TouchEvent) => {
      setIsResizing(true);

      const startY = e.touches[0].clientY;
      const startHeight = height;

      const handleTouchMove = (e: TouchEvent) => {
        const delta = startY - e.touches[0].clientY;
        const newHeight = Math.min(
          MAX_HEIGHT,
          Math.max(MIN_HEIGHT, startHeight + delta),
        );
        setHeight(newHeight);
      };

      const handleTouchEnd = () => {
        setIsResizing(false);
        document.removeEventListener("touchmove", handleTouchMove);
        document.removeEventListener("touchend", handleTouchEnd);
      };

      document.addEventListener("touchmove", handleTouchMove);
      document.addEventListener("touchend", handleTouchEnd);
    },
    [height],
  );

  const handleClearMessages = useCallback(() => {
    const showAll =
      selectedSources.length === 0 ||
      availableSources.every((s) => selectedSources.includes(s));

    if (showAll) {
      // Clear all - get unique sources from current messages
      const sources = new Set(allMessages.map((m) => m.source));
      sources.forEach((src) => clearMessages(src));
    } else {
      // Clear selected sources
      selectedSources.forEach((src) => clearMessages(src));
    }
  }, [selectedSources, availableSources, clearMessages, allMessages]);

  // Toggle a single source selection
  const toggleSource = useCallback(
    (source: string) => {
      setSelectedSources((prev) => {
        if (prev.length === 0) {
          // Currently showing all - deselect this one means select all except this
          return availableSources.filter((s) => s !== source);
        }
        if (prev.includes(source)) {
          // Deselect this source
          const newSelection = prev.filter((s) => s !== source);
          // If nothing left, use __none__ marker instead of empty array
          return newSelection.length === 0 ? ["__none__"] : newSelection;
        } else {
          // Select this source - remove __none__ marker if present
          const filtered = prev.filter((s) => s !== "__none__");
          return [...filtered, source];
        }
      });
    },
    [availableSources],
  );

  // Select all sources
  const selectAll = useCallback(() => {
    setSelectedSources([]);
  }, []);

  // Deselect all sources
  const deselectAll = useCallback(() => {
    setSelectedSources(["__none__"]); // Special marker to show nothing
  }, []);

  // Get display text for the dropdown button
  const getDropdownLabel = useCallback(() => {
    if (selectedSources.length === 0) {
      return "All Sources";
    }
    if (selectedSources[0] === "__none__") {
      return "None";
    }
    const validSelected = selectedSources.filter((s) =>
      availableSources.includes(s),
    );
    if (validSelected.length === availableSources.length) {
      return "All Sources";
    }
    if (validSelected.length === 0) {
      return "None";
    }
    if (validSelected.length === 1) {
      return validSelected[0];
    }
    return `${validSelected.length} sources`;
  }, [selectedSources, availableSources]);

  const handleFilterChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFilter(e.target.value);
    },
    [],
  );

  const toggleCollapsed = () => setIsCollapsed(!isCollapsed);

  return (
    <div
      className={clsx(
        "fixed bottom-0 left-0 right-0 z-40 flex flex-col bg-zinc-900 ring-1 ring-zinc-800 transition-all lg:left-64",
        isResizing && "select-none",
      )}
      style={{ height: isCollapsed ? 36 : height + 36 }}
    >
      {/* Resize handle */}
      {!isCollapsed && (
        <div
          className="absolute -top-1 left-0 right-0 h-2 cursor-ns-resize hover:bg-d2-gold/20"
          onMouseDown={handleMouseDown}
          onTouchStart={handleTouchStart}
        />
      )}

      {/* Header bar */}
      <div className="flex h-9 flex-shrink-0 items-center justify-between border-b border-zinc-800 px-2">
        <div className="flex items-center gap-2">
          {/* Multi-select dropdown */}
          <div ref={dropdownRef} className="relative">
            <button
              onClick={() => setIsDropdownOpen(!isDropdownOpen)}
              className="flex w-28 items-center justify-between rounded bg-zinc-800 px-2 py-1 text-xs font-medium text-zinc-300 ring-1 ring-zinc-700 hover:ring-zinc-600 focus:ring-d2-gold"
            >
              <span className="truncate">{getDropdownLabel()}</span>
              <DropdownIcon
                className={clsx(
                  "h-3 w-3 transition-transform",
                  isDropdownOpen && "rotate-180",
                )}
              />
            </button>

            {isDropdownOpen && (
              <div className="absolute left-0 top-full z-50 mt-1 min-w-40 max-w-64 rounded bg-zinc-800 py-1 shadow-lg ring-1 ring-zinc-700">
                {/* Select All / Deselect All buttons */}
                <div className="flex gap-1 border-b border-zinc-700 px-2 py-1.5">
                  <button
                    onClick={selectAll}
                    className="flex-1 rounded px-2 py-0.5 text-xs text-zinc-400 hover:bg-zinc-700 hover:text-zinc-200"
                  >
                    All
                  </button>
                  <button
                    onClick={deselectAll}
                    className="flex-1 rounded px-2 py-0.5 text-xs text-zinc-400 hover:bg-zinc-700 hover:text-zinc-200"
                  >
                    None
                  </button>
                </div>

                {/* Source checkboxes */}
                <div className="max-h-48 overflow-y-auto py-1">
                  {availableSources.map((sourceName) => {
                    const isSelected =
                      selectedSources.length === 0 ||
                      selectedSources.includes(sourceName);
                    return (
                      <button
                        key={sourceName}
                        onClick={() => toggleSource(sourceName)}
                        className="flex w-full items-center gap-2 px-2 py-1 text-left text-xs text-zinc-300 hover:bg-zinc-700"
                      >
                        <span
                          className={clsx(
                            "flex h-3.5 w-3.5 items-center justify-center rounded border",
                            isSelected
                              ? "border-d2-gold bg-d2-gold text-zinc-900"
                              : "border-zinc-600 bg-zinc-800",
                          )}
                        >
                          {isSelected && <CheckIcon className="h-2.5 w-2.5" />}
                        </span>
                        <span className="truncate">{sourceName}</span>
                      </button>
                    );
                  })}
                  {availableSources.length === 0 && (
                    <div className="px-2 py-1 text-xs text-zinc-500">
                      No sources available
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>

          <input
            type="text"
            value={filter}
            onChange={handleFilterChange}
            placeholder="Filter (regex)..."
            className={clsx(
              "w-48 rounded border-0 bg-zinc-800 px-2 py-1 text-xs text-zinc-300 ring-1 placeholder:text-zinc-500 focus:ring-d2-gold",
              filterError ? "ring-red-500" : "ring-zinc-700",
            )}
          />
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={handleClearMessages}
            disabled={isClearing}
            className="flex items-center gap-1 rounded px-1.5 py-0.5 text-xs text-zinc-500 hover:bg-zinc-800 hover:text-zinc-300 disabled:opacity-50"
            title="Clear console"
          >
            <TrashIcon className="h-3.5 w-3.5" />
            <span>Clear</span>
          </button>
          <button
            onClick={toggleCollapsed}
            className="rounded p-1 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100"
            title={isCollapsed ? "Expand console" : "Collapse console"}
          >
            {isCollapsed ? (
              <ChevronUpIcon className="h-4 w-4" />
            ) : (
              <ChevronDownIcon className="h-4 w-4" />
            )}
          </button>
        </div>
      </div>

      {/* Console content */}
      {!isCollapsed && (
        <ConsoleOutput
          messages={messages}
          className="min-h-0 flex-1 rounded-none"
        />
      )}
    </div>
  );
}
