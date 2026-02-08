/**
 * Delete Confirmation Dialog Component
 *
 * Reusable confirmation dialog for delete operations.
 * Provides consistent UX for all delete actions across the application.
 */

import { Dialog, DialogHeader, DialogContent, DialogFooter } from "./Dialog";
import { Button } from "./Button";

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
    <Dialog open={open} onClose={onCancel}>
      <DialogHeader
        title={`Delete ${entityType}`}
        description={`Are you sure you want to delete "${entityName}"? This action cannot be undone.`}
        onClose={onCancel}
      />
      <DialogContent>
        <p className="text-sm text-zinc-400">{warningMessage}</p>
      </DialogContent>
      <DialogFooter>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button variant="danger" onClick={onConfirm} disabled={isPending}>
          Delete
        </Button>
      </DialogFooter>
    </Dialog>
  );
}
