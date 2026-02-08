/**
 * useProfileTableColumns hook
 *
 * Manages which columns are visible in the profiles table.
 * Persists preferences to localStorage.
 */

import { useState, useCallback, useEffect } from "react";

export type ProfileColumnKey =
  | "runs"
  | "chickens"
  | "deaths"
  | "crashes"
  | "restarts"
  | "key"
  | "gamePath";

export interface ProfileColumn {
  key: ProfileColumnKey;
  label: string;
  defaultVisible: boolean;
}

export const PROFILE_COLUMNS: ProfileColumn[] = [
  { key: "runs", label: "Runs", defaultVisible: true },
  { key: "chickens", label: "Chickens", defaultVisible: true },
  { key: "deaths", label: "Deaths", defaultVisible: true },
  { key: "crashes", label: "Crashes", defaultVisible: true },
  { key: "restarts", label: "Restarts", defaultVisible: true },
  { key: "key", label: "Key", defaultVisible: false },
  { key: "gamePath", label: "Target", defaultVisible: false },
];

const STORAGE_KEY = "d2bot:profileTableColumns";

function getDefaultColumns(): Set<ProfileColumnKey> {
  return new Set(
    PROFILE_COLUMNS.filter((c) => c.defaultVisible).map((c) => c.key),
  );
}

function loadColumns(): Set<ProfileColumnKey> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored) as ProfileColumnKey[];
      return new Set(parsed);
    }
  } catch {
    // Ignore parse errors, use defaults
  }
  return getDefaultColumns();
}

function saveColumns(columns: Set<ProfileColumnKey>): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify([...columns]));
}

export function useProfileTableColumns() {
  const [visibleColumns, setVisibleColumns] = useState<Set<ProfileColumnKey>>(
    () => loadColumns(),
  );

  // Persist to localStorage when columns change
  useEffect(() => {
    saveColumns(visibleColumns);
  }, [visibleColumns]);

  const isColumnVisible = useCallback(
    (key: ProfileColumnKey): boolean => {
      return visibleColumns.has(key);
    },
    [visibleColumns],
  );

  const toggleColumn = useCallback((key: ProfileColumnKey): void => {
    setVisibleColumns((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }, []);

  const resetToDefaults = useCallback((): void => {
    setVisibleColumns(getDefaultColumns());
  }, []);

  return {
    visibleColumns,
    isColumnVisible,
    toggleColumn,
    resetToDefaults,
    columns: PROFILE_COLUMNS,
  };
}
