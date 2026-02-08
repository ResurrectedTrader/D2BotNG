/**
 * Close Prompt Dialog Component
 *
 * Dialog shown when user closes the window and closeAction is set to ASK.
 * Allows user to choose between minimizing to tray or exiting, with option to remember.
 */

import { useState } from "react";
import { Dialog, DialogHeader, DialogFooter } from "./Dialog";
import { Button } from "./Button";

export type CloseChoice = "minimize" | "exit";

export interface ClosePromptDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** Whether the save operation is pending */
  isPending?: boolean;
  /** Called when user makes a choice */
  onChoice: (choice: CloseChoice, remember: boolean) => void;
  /** Called when user cancels or closes the dialog */
  onCancel: () => void;
}

export function ClosePromptDialog({
  open,
  isPending,
  onChoice,
  onCancel,
}: ClosePromptDialogProps) {
  const [remember, setRemember] = useState(false);

  const handleMinimize = () => {
    onChoice("minimize", remember);
  };

  const handleExit = () => {
    onChoice("exit", remember);
  };

  return (
    <Dialog open={open} onClose={onCancel}>
      <DialogHeader
        title="Close D2BotNG"
        description="What would you like to do?"
        onClose={onCancel}
      />
      <DialogFooter>
        <label className="mr-auto flex cursor-pointer items-center gap-2">
          <input
            type="checkbox"
            checked={remember}
            onChange={(e) => setRemember(e.target.checked)}
            className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
          />
          <span className="text-sm text-zinc-400">Remember</span>
        </label>
        <Button variant="ghost" onClick={onCancel} disabled={isPending}>
          Cancel
        </Button>
        <Button
          variant="secondary"
          onClick={handleMinimize}
          disabled={isPending}
        >
          Minimize to Tray
        </Button>
        <Button variant="danger" onClick={handleExit} disabled={isPending}>
          Exit
        </Button>
      </DialogFooter>
    </Dialog>
  );
}
