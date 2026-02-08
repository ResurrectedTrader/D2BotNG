/**
 * Password Prompt Dialog Component
 *
 * Shown when the server requires authentication.
 * User must enter the correct password to access the server.
 */

import { useState, useEffect } from "react";
import { Dialog, DialogHeader, DialogContent, DialogFooter } from "./Dialog";
import { Button } from "./Button";
import { Input } from "./Input";

export interface PasswordPromptDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** Error message to display (e.g., "Invalid password") */
  error?: string;
  /** Called when user submits password */
  onSubmit: (password: string) => void;
}

export function PasswordPromptDialog({
  open,
  error,
  onSubmit,
}: PasswordPromptDialogProps) {
  const [password, setPassword] = useState("");

  // Clear password when dialog opens
  useEffect(() => {
    if (open) {
      setPassword("");
    }
  }, [open]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (password.trim()) {
      onSubmit(password);
    }
  };

  return (
    <Dialog open={open} onClose={() => {}}>
      <form onSubmit={handleSubmit}>
        <DialogHeader
          title="Authentication Required"
          description="This server requires a password to access."
        />
        <DialogContent>
          <Input
            id="auth-password"
            label="Password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={error}
            autoFocus
            placeholder="Enter server password"
          />
        </DialogContent>
        <DialogFooter>
          <Button type="submit" disabled={!password.trim()}>
            Connect
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  );
}
