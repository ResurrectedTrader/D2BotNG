/**
 * KeysPage component
 *
 * Main page for managing CD key lists.
 * Includes CRUD operations for key lists.
 */

import { useState, useCallback } from "react";
import {
  Button,
  Card,
  EmptyState,
  LoadingSpinner,
  DeleteConfirmationDialog,
} from "@/components/ui";
import { useDeleteKeyList, useDeleteDialog } from "@/hooks";
import { useKeyLists, useIsLoading } from "@/stores/event-store";
import type { KeyList } from "@/generated/keys_pb";
import { KeyListRow } from "./KeyListRow";
import { KeyListDialog } from "./KeyListDialog";
import { PlusIcon, KeyIcon } from "@heroicons/react/24/outline";

export function KeysPage() {
  // Get key lists from event store
  const isLoading = useIsLoading();
  const keyListsData = useKeyLists();
  const keyLists = keyListsData.map((k) => k.keyList);
  const usageByKeyList = new Map(
    keyListsData.map((k) => [k.keyList.name, k.usage]),
  );
  const deleteKeyList = useDeleteKeyList();

  // Dialog states
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [editingKeyList, setEditingKeyList] = useState<KeyList | null>(null);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  // Delete dialog hook
  const {
    deleteTarget,
    isOpen: isDeleteDialogOpen,
    requestDelete,
    confirmDelete,
    cancelDelete,
  } = useDeleteDialog<KeyList>(deleteKeyList);

  const handleToggleExpand = useCallback((id: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const handleCreate = useCallback(() => {
    setEditingKeyList(null);
    setIsDialogOpen(true);
  }, []);

  const handleEdit = useCallback((keyList: KeyList) => {
    setEditingKeyList(keyList);
    setIsDialogOpen(true);
  }, []);

  const handleDialogClose = useCallback(() => {
    setIsDialogOpen(false);
    setEditingKeyList(null);
  }, []);

  const hasKeyLists = keyLists && keyLists.length > 0;

  // Loading state - waiting for initial data from event stream
  if (isLoading) {
    return <LoadingSpinner fullPage />;
  }

  return (
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-3 border-b border-zinc-800/50">
        <div className="flex items-center justify-between gap-3">
          <h1 className="text-lg font-bold text-zinc-100">Keys</h1>
          <Button onClick={handleCreate} size="sm">
            <PlusIcon className="h-4 w-4" />
            New Key List
          </Button>
        </div>
      </div>

      {/* Content */}
      {hasKeyLists ? (
        <Card className="divide-y divide-zinc-800">
          {keyLists.map((keyList) => (
            <KeyListRow
              key={keyList.name}
              keyList={keyList}
              usage={usageByKeyList.get(keyList.name) ?? []}
              isExpanded={expandedIds.has(keyList.name)}
              onToggle={() => handleToggleExpand(keyList.name)}
              onEdit={handleEdit}
              onDelete={requestDelete}
            />
          ))}
        </Card>
      ) : (
        <EmptyState
          icon={KeyIcon}
          title="No key lists yet"
          description="Create your first key list to manage CD keys for your profiles."
          action={
            <Button onClick={handleCreate}>
              <PlusIcon className="h-4 w-4" />
              Create Key List
            </Button>
          }
        />
      )}

      {/* Create/Edit dialog */}
      <KeyListDialog
        open={isDialogOpen}
        onClose={handleDialogClose}
        keyList={editingKeyList}
      />

      {/* Delete confirmation dialog */}
      <DeleteConfirmationDialog
        open={isDeleteDialogOpen}
        entityType="Key List"
        entityName={deleteTarget?.name ?? ""}
        warningMessage="All keys in this list will be permanently deleted."
        isPending={deleteKeyList.isPending}
        onConfirm={confirmDelete}
        onCancel={cancelDelete}
      />
    </div>
  );
}
