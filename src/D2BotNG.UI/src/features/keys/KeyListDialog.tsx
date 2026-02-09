/**
 * KeyListDialog component
 *
 * Dialog for creating and editing key lists.
 * Allows entering a name and keys in a textarea (one per line, format: "name,classickey,expansionkey").
 */

import { useState, useCallback, useEffect, useMemo } from "react";
import {
  Dialog,
  DialogHeader,
  DialogContent,
  DialogFooter,
  Button,
  Input,
} from "@/components/ui";
import { useCreateKeyList, useUpdateKeyList } from "@/hooks";
import { useKeyLists } from "@/stores/event-store";
import type { KeyList, CDKey } from "@/generated/keys_pb";

export interface KeyListDialogProps {
  open: boolean;
  onClose: () => void;
  keyList?: KeyList | null;
}

/**
 * Parse keys from textarea (one per line, format: "name,classickey,expansionkey")
 */
function parseKeys(
  text: string,
): Array<{ name: string; classic: string; expansion: string }> {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const parts = line.split(",").map((p) => p.trim());
      return {
        name: parts[0] || "",
        classic: parts[1] || "",
        expansion: parts[2] || "",
      };
    })
    .filter(
      (key) =>
        key.name.length > 0 &&
        (key.classic.length > 0 || key.expansion.length > 0),
    );
}

/**
 * Format keys to textarea text
 */
function formatKeys(keys: CDKey[]): string {
  return keys
    .map((key) => key.name + "," + key.classic + "," + key.expansion)
    .join("\n");
}

export function KeyListDialog({ open, onClose, keyList }: KeyListDialogProps) {
  const [name, setName] = useState("");
  const [keysText, setKeysText] = useState("");
  const [nameError, setNameError] = useState<string | undefined>();
  const [keysError, setKeysError] = useState<string | undefined>();

  const createKeyList = useCreateKeyList();
  const updateKeyList = useUpdateKeyList();
  const keyListsData = useKeyLists();

  const isEditing = keyList !== null && keyList !== undefined;

  // Build set of existing key list names for uniqueness validation
  const existingNames = useMemo(
    () => new Set(keyListsData.map((kl) => kl.keyList.name.toLowerCase())),
    [keyListsData],
  );

  // Reset form when dialog opens/closes or keyList changes
  useEffect(() => {
    if (open) {
      if (keyList) {
        setName(keyList.name);
        setKeysText(formatKeys(keyList.keys));
      } else {
        setName("");
        setKeysText("");
      }
      setNameError(undefined);
      setKeysError(undefined);
    }
  }, [open, keyList]);

  const parsedKeys = useMemo(() => parseKeys(keysText), [keysText]);

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault();

      // Validate name
      if (!name.trim()) {
        setNameError("Name is required");
        return;
      }

      // Check for duplicate name (allow keeping the same name when editing)
      const trimmedName = name.trim().toLowerCase();
      const isSameName =
        isEditing && keyList?.name.toLowerCase() === trimmedName;
      if (!isSameName && existingNames.has(trimmedName)) {
        setNameError("A key list with this name already exists");
        return;
      }

      // Check for duplicate key names within the list
      const keyNameCounts = new Map<string, number>();
      for (const key of parsedKeys) {
        const lower = key.name.toLowerCase();
        keyNameCounts.set(lower, (keyNameCounts.get(lower) ?? 0) + 1);
      }
      const duplicateKeyName = parsedKeys.find(
        (key) => (keyNameCounts.get(key.name.toLowerCase()) ?? 0) > 1,
      );
      if (duplicateKeyName) {
        setKeysError(`Duplicate key name: "${duplicateKeyName.name}"`);
        return;
      }

      setNameError(undefined);
      setKeysError(undefined);

      const keyListData = {
        name: name.trim(),
        keys: parsedKeys,
      };

      try {
        if (isEditing && keyList) {
          const isRename = keyList.name !== name.trim();
          await updateKeyList.mutateAsync({
            keyList: keyListData,
            originalName: isRename ? keyList.name : undefined,
          });
        } else {
          await createKeyList.mutateAsync(keyListData);
        }
        onClose();
      } catch {
        // Error toast is handled by mutation hooks
      }
    },
    [
      name,
      parsedKeys,
      isEditing,
      keyList,
      existingNames,
      updateKeyList,
      createKeyList,
      onClose,
    ],
  );

  const isPending = createKeyList.isPending || updateKeyList.isPending;

  return (
    <Dialog open={open} onClose={onClose}>
      <form onSubmit={handleSubmit}>
        <DialogHeader
          title={isEditing ? "Edit Key List" : "Create Key List"}
          description={
            isEditing
              ? "Update the key list name and keys."
              : "Create a new key list with CD keys."
          }
          onClose={onClose}
        />

        <DialogContent className="space-y-4">
          <Input
            id="name"
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError}
            placeholder="Enter key list name"
            autoFocus
          />

          <div>
            <label
              htmlFor="keys"
              className="mb-1.5 block text-sm font-medium text-zinc-400"
            >
              Keys
            </label>
            <textarea
              id="keys"
              value={keysText}
              onChange={(e) => setKeysText(e.target.value)}
              className="block w-full rounded-lg border-0 bg-zinc-800 px-3 py-2 text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm sm:leading-6 transition-colors font-mono"
              rows={8}
              placeholder="Enter keys (one per line)&#10;Format: name,classickey,expansionkey&#10;&#10;Example:&#10;Key1,ABCD1234EFGH5678,IJKL9012MNOP3456&#10;Key2,QRST4567UVWX8901,YZAB2345CDEF6789"
            />
            <p className="mt-1.5 text-sm text-zinc-500">
              {parsedKeys.length} {parsedKeys.length === 1 ? "key" : "keys"}{" "}
              parsed
            </p>
            {keysError && (
              <p className="mt-1 text-sm text-red-400">{keysError}</p>
            )}
          </div>
        </DialogContent>

        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={onClose}
            disabled={isPending}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={isPending}>
            {isEditing ? "Save Changes" : "Create Key List"}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  );
}
