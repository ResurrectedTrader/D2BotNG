/**
 * Console component
 *
 * Reusable console that displays messages from the event store.
 * Use profileId="" for global console, or a specific profile ID for profile console.
 */

import { useCallback } from "react";
import clsx from "clsx";
import {
  SignalIcon,
  SignalSlashIcon,
  TrashIcon,
} from "@heroicons/react/24/outline";
import { ConsoleOutput } from "./ConsoleOutput";
import { useMessages, useIsConnected } from "@/stores/event-store";
import { useClearConsole } from "@/hooks/useClearConsole";

interface ConsoleProps {
  /** Profile ID to stream console for (empty string for global) */
  profileId: string;
  /** Optional label to display */
  label?: string;
  /** Optional CSS class for the container */
  className?: string;
  /** Whether to show the header with connection status */
  showHeader?: boolean;
}

export function Console({
  profileId,
  label,
  className,
  showHeader = true,
}: ConsoleProps) {
  // Use source-based message filtering: "System" for global, profileId for profile
  const source = profileId || "System";
  const messages = useMessages(source);

  const isConnected = useIsConnected();
  const { clearMessages, isClearing } = useClearConsole();

  const handleClearMessages = useCallback(() => {
    clearMessages(source);
  }, [source, clearMessages]);

  return (
    <div className={clsx("flex flex-col", className)}>
      {showHeader && (
        <div className="flex items-center justify-between border-b border-zinc-800 bg-zinc-900 px-3 py-1.5">
          <div className="flex items-center gap-2">
            {isConnected ? (
              <SignalIcon
                className="h-4 w-4 text-green-500"
                title="Connected"
              />
            ) : (
              <SignalSlashIcon
                className="h-4 w-4 text-red-500"
                title="Disconnected"
              />
            )}
            <span className="text-xs font-medium text-zinc-400">
              {label || (profileId ? "Profile Console" : "System Console")}
            </span>
          </div>
          <button
            onClick={handleClearMessages}
            disabled={isClearing}
            className="flex items-center gap-1 rounded px-1.5 py-0.5 text-xs text-zinc-500 hover:bg-zinc-800 hover:text-zinc-300 disabled:opacity-50"
            title="Clear console"
          >
            <TrashIcon className="h-3.5 w-3.5" />
            <span>Clear</span>
          </button>
        </div>
      )}
      <ConsoleOutput messages={messages} className="flex-1 rounded-none" />
    </div>
  );
}
