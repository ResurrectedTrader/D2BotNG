/**
 * useDeleteDialog Hook
 *
 * Encapsulates delete confirmation dialog state management pattern.
 * Provides consistent delete flow across CRUD pages.
 */

import { useState, useCallback } from "react";
import type { UseMutationResult } from "@tanstack/react-query";

interface Entity {
  name: string;
}

interface UseDeleteDialogResult<T extends Entity> {
  /** The entity currently targeted for deletion */
  deleteTarget: T | null;
  /** Whether the delete dialog is open */
  isOpen: boolean;
  /** Call when user requests to delete an entity */
  requestDelete: (entity: T) => void;
  /** Call when user confirms deletion */
  confirmDelete: () => void;
  /** Call when user cancels deletion */
  cancelDelete: () => void;
}

/**
 * Hook for managing delete confirmation dialog state.
 *
 * @param deleteMutation - TanStack Query mutation for deleting the entity
 * @returns Object with delete dialog state and handlers
 *
 * @example
 * const deleteProfile = useDeleteProfile()
 * const { deleteTarget, isOpen, requestDelete, confirmDelete, cancelDelete } = useDeleteDialog(deleteProfile)
 *
 * // In JSX:
 * <Button onClick={() => requestDelete(profile)}>Delete</Button>
 * <DeleteConfirmationDialog
 *   open={isOpen}
 *   entityName={deleteTarget?.name ?? ''}
 *   onConfirm={confirmDelete}
 *   onCancel={cancelDelete}
 *   isPending={deleteProfile.isPending}
 * />
 */
export function useDeleteDialog<T extends Entity>(
  deleteMutation: UseMutationResult<void, Error, string>,
): UseDeleteDialogResult<T> {
  const [deleteTarget, setDeleteTarget] = useState<T | null>(null);

  const requestDelete = useCallback((entity: T) => {
    setDeleteTarget(entity);
  }, []);

  const confirmDelete = useCallback(() => {
    if (deleteTarget) {
      deleteMutation.mutate(deleteTarget.name);
      setDeleteTarget(null);
    }
  }, [deleteTarget, deleteMutation]);

  const cancelDelete = useCallback(() => {
    setDeleteTarget(null);
  }, []);

  return {
    deleteTarget,
    isOpen: deleteTarget !== null,
    requestDelete,
    confirmDelete,
    cancelDelete,
  };
}
