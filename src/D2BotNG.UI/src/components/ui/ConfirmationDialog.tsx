/**
 * Confirmation Dialog
 *
 * Generic confirm/cancel dialog with a configurable title, prompt, body, and confirm
 * button. DeleteConfirmationDialog is a thin wrapper over this for the common delete case.
 */

import { type ComponentProps } from "react";
import { Dialog, DialogHeader, DialogContent, DialogFooter } from "./Dialog";
import { Button } from "./Button";

export interface ConfirmationDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** Header title (e.g. "Delete Profile", "Reset kills") */
  title: string;
  /** Header sub-text under the title */
  description?: string;
  /** Optional body paragraph (e.g. a warning) */
  message?: string;
  /** Confirm button label (default "Confirm") */
  confirmLabel?: string;
  /** Confirm button variant (default "danger") */
  confirmVariant?: ComponentProps<typeof Button>["variant"];
  /** Whether the confirm action is in flight */
  isPending?: boolean;
  /** Called when the user confirms */
  onConfirm: () => void;
  /** Called when the user cancels or closes the dialog */
  onCancel: () => void;
}

export function ConfirmationDialog({
  open,
  title,
  description,
  message,
  confirmLabel = "Confirm",
  confirmVariant = "danger",
  isPending = false,
  onConfirm,
  onCancel,
}: ConfirmationDialogProps) {
  return (
    <Dialog open={open} onClose={onCancel}>
      <DialogHeader
        title={title}
        description={description}
        onClose={onCancel}
      />
      {message && (
        <DialogContent>
          <p className="text-sm text-zinc-400">{message}</p>
        </DialogContent>
      )}
      <DialogFooter>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button
          variant={confirmVariant}
          onClick={onConfirm}
          disabled={isPending}
        >
          {confirmLabel}
        </Button>
      </DialogFooter>
    </Dialog>
  );
}
