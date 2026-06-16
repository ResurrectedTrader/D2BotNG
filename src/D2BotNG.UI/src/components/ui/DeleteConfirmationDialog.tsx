/**
 * Delete Confirmation Dialog
 *
 * Thin wrapper over ConfirmationDialog for the common delete case: a standard
 * "Delete {entityType}" title + prompt and a danger "Delete" button.
 */

import { ConfirmationDialog } from "./ConfirmationDialog";

export interface DeleteConfirmationDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** Entity type name (e.g., "Profile", "Key List", "Schedule") */
  entityType: string;
  /** Name of the entity being deleted */
  entityName: string;
  /** Warning message to display */
  warningMessage: string;
  /** Whether the delete operation is pending */
  isPending: boolean;
  /** Called when user confirms deletion */
  onConfirm: () => void;
  /** Called when user cancels or closes the dialog */
  onCancel: () => void;
}

export function DeleteConfirmationDialog({
  open,
  entityType,
  entityName,
  warningMessage,
  isPending,
  onConfirm,
  onCancel,
}: DeleteConfirmationDialogProps) {
  return (
    <ConfirmationDialog
      open={open}
      title={`Delete ${entityType}`}
      description={`Are you sure you want to delete "${entityName}"? This action cannot be undone.`}
      message={warningMessage}
      confirmLabel="Delete"
      confirmVariant="danger"
      isPending={isPending}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}
