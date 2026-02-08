import { useState, useCallback, useMemo, useEffect } from "react";
import clsx from "clsx";
import {
  FolderIcon,
  DocumentIcon,
  ArrowUturnLeftIcon,
  PencilIcon,
} from "@heroicons/react/24/outline";
import { Dialog, DialogHeader, DialogContent, DialogFooter } from "./Dialog";
import { Button } from "./Button";
import { Input } from "./Input";
import { LoadingSpinner } from "./LoadingSpinner";
import { useDirectoryListing } from "@/hooks";
import type { DirectoryEntry } from "@/generated/settings_pb";

export interface PathSelectorDialogProps {
  open: boolean;
  onClose: () => void;
  onSelect: (path: string) => void;
  mode: "file" | "directory";
  title?: string;
  description?: string;
  initialPath?: string;
  filter?: (entry: DirectoryEntry, currentPath: string) => boolean;
}

export function PathSelectorDialog({
  open,
  onClose,
  onSelect,
  mode,
  title = mode === "file" ? "Select File" : "Select Directory",
  description,
  initialPath = "",
  filter,
}: PathSelectorDialogProps) {
  const [currentPath, setCurrentPath] = useState(initialPath);
  const [selectedEntry, setSelectedEntry] = useState<string | null>(null);
  const [isEditingPath, setIsEditingPath] = useState(false);
  const [editPath, setEditPath] = useState("");

  // Reset state when dialog opens
  useEffect(() => {
    if (open) {
      setCurrentPath(initialPath);
      setSelectedEntry(null);
      setIsEditingPath(false);
      setEditPath("");
    }
  }, [open, initialPath]);

  const { data, isLoading, error } = useDirectoryListing(currentPath);

  const isAtRoot = !currentPath;

  // Build full path from current path and entry name
  const buildFullPath = useCallback(
    (entryName: string) => {
      if (!currentPath) return entryName;
      return currentPath + "\\" + entryName;
    },
    [currentPath],
  );

  // Filter entries based on mode and filter function
  const filteredEntries = data?.entries.filter((entry) => {
    if (entry.isDirectory) return true;
    if (mode === "directory") return false;
    if (filter) return filter(entry, currentPath);
    return true;
  });

  // Parse path into breadcrumb segments
  const breadcrumbs = useMemo(
    () => (currentPath ? currentPath.split(/[\\\/]/).filter(Boolean) : []),
    [currentPath],
  );

  // Navigate to a directory (normalizes path with trailing backslash)
  const navigateTo = useCallback((path: string) => {
    // Empty path = drive list, don't add slash
    // Otherwise ensure trailing backslash for Windows paths
    const normalized = path && !path.endsWith("\\") ? path + "\\" : path;
    setCurrentPath(normalized);
    setSelectedEntry(null);
  }, []);

  // Handle entry click (single click = select, navigate if directory)
  const handleEntryClick = useCallback(
    (entry: DirectoryEntry) => {
      if (entry.isDirectory) {
        navigateTo(buildFullPath(entry.name));
      } else {
        setSelectedEntry(entry.name);
      }
    },
    [navigateTo, buildFullPath],
  );

  // Handle entry double-click (immediate select)
  const handleEntryDoubleClick = useCallback(
    (entry: DirectoryEntry) => {
      if (entry.isDirectory) {
        if (mode === "directory") {
          onSelect(buildFullPath(entry.name));
        }
      } else {
        onSelect(buildFullPath(entry.name));
      }
    },
    [mode, onSelect, buildFullPath],
  );

  // Handle parent directory navigation
  const handleGoUp = useCallback(() => {
    const segments = currentPath.split(/[\\\/]/).filter(Boolean);
    if (segments.length <= 1) {
      navigateTo("");
    } else {
      segments.pop();
      navigateTo(segments.join("\\"));
    }
  }, [currentPath, navigateTo]);

  // Handle breadcrumb click
  const handleBreadcrumbClick = useCallback(
    (index: number) => {
      if (index < 0) {
        navigateTo("");
      } else {
        navigateTo(breadcrumbs.slice(0, index + 1).join("\\"));
      }
    },
    [breadcrumbs, navigateTo],
  );

  // Handle path edit submission
  const handlePathSubmit = useCallback(() => {
    navigateTo(editPath);
    setIsEditingPath(false);
  }, [editPath, navigateTo]);

  // Handle confirm button
  const handleConfirm = useCallback(() => {
    if (mode === "directory") {
      onSelect(currentPath);
    } else if (selectedEntry) {
      onSelect(buildFullPath(selectedEntry));
    }
  }, [mode, currentPath, selectedEntry, onSelect, buildFullPath]);

  // Check if confirm button should be enabled
  const canConfirm = mode === "directory" ? !!currentPath : !!selectedEntry;

  return (
    <Dialog open={open} onClose={onClose} className="max-w-2xl">
      <DialogHeader title={title} onClose={onClose} />

      <DialogContent className="p-0">
        {/* Path Bar */}
        <div className="border-b border-zinc-800 px-4 py-2">
          {isEditingPath ? (
            <div className="flex gap-2">
              <Input
                value={editPath}
                onChange={(e) => setEditPath(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") handlePathSubmit();
                  if (e.key === "Escape") setIsEditingPath(false);
                }}
                className="flex-1"
                autoFocus
              />
              <Button size="sm" onClick={handlePathSubmit}>
                Go
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => setIsEditingPath(false)}
              >
                Cancel
              </Button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <div className="flex-1 flex items-center gap-1 overflow-x-auto text-sm min-h-[32px]">
                {breadcrumbs.map((segment, index) => (
                  <span key={index} className="flex items-center gap-1">
                    {index > 0 && <span className="text-zinc-600">&gt;</span>}
                    <button
                      onClick={() => handleBreadcrumbClick(index)}
                      className={clsx(
                        "px-2 py-1 rounded hover:bg-zinc-800 transition-colors whitespace-nowrap",
                        index === breadcrumbs.length - 1
                          ? "text-zinc-100"
                          : "text-zinc-400",
                      )}
                    >
                      {segment}
                    </button>
                  </span>
                ))}
              </div>
              <button
                onClick={() => {
                  setEditPath(currentPath);
                  setIsEditingPath(true);
                }}
                className="p-1.5 rounded hover:bg-zinc-800 text-zinc-400 hover:text-zinc-100 transition-colors"
                title="Edit path"
              >
                <PencilIcon className="h-4 w-4" />
              </button>
            </div>
          )}
        </div>

        {/* Entry List */}
        <div className="h-80 overflow-y-auto">
          {isLoading ? (
            <div className="flex items-center justify-center h-full">
              <LoadingSpinner />
            </div>
          ) : error ? (
            <div className="flex items-center justify-center h-full text-red-500 px-4 text-center">
              {error.message}
            </div>
          ) : (
            <div className="py-1">
              {/* Parent directory entry */}
              {!isAtRoot && (
                <button
                  onClick={handleGoUp}
                  className="w-full flex items-center gap-3 px-4 py-2.5 hover:bg-zinc-800 transition-colors text-left"
                >
                  <ArrowUturnLeftIcon className="h-5 w-5 text-zinc-400" />
                  <span className="text-zinc-300">..</span>
                </button>
              )}

              {/* Directory/file entries */}
              {filteredEntries?.map((entry) => (
                <button
                  key={entry.name}
                  onClick={() => handleEntryClick(entry)}
                  onDoubleClick={() => handleEntryDoubleClick(entry)}
                  className={clsx(
                    "w-full flex items-center gap-3 px-4 py-2.5 transition-colors text-left",
                    selectedEntry === entry.name
                      ? "bg-d2-gold/20 ring-1 ring-d2-gold"
                      : "hover:bg-zinc-800",
                  )}
                >
                  {entry.isDirectory ? (
                    <FolderIcon className="h-5 w-5 text-d2-gold" />
                  ) : (
                    <DocumentIcon className="h-5 w-5 text-zinc-400" />
                  )}
                  <span
                    className={clsx(
                      entry.isDirectory ? "text-zinc-100" : "text-zinc-300",
                    )}
                  >
                    {entry.name}
                  </span>
                </button>
              ))}

              {/* Empty state */}
              {filteredEntries?.length === 0 && isAtRoot && (
                <div className="flex items-center justify-center h-40 text-zinc-500">
                  No drives available
                </div>
              )}
              {filteredEntries?.length === 0 && !isAtRoot && (
                <div className="flex items-center justify-center h-40 text-zinc-500">
                  {mode === "directory"
                    ? "No subdirectories"
                    : "No matching files"}
                </div>
              )}
            </div>
          )}
        </div>
      </DialogContent>

      <DialogFooter className="justify-between">
        {description ? (
          <span className="text-sm text-zinc-500">{description}</span>
        ) : (
          <span />
        )}
        <div className="flex gap-3">
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={handleConfirm} disabled={!canConfirm}>
            Select
          </Button>
        </div>
      </DialogFooter>
    </Dialog>
  );
}
