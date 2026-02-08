/**
 * useClearConsole hook
 *
 * Clears console messages on the server and locally.
 * Local clearing happens after the RPC succeeds.
 */

import { useCallback, useState } from "react";
import { eventClient } from "@/lib/grpc-client";
import { useEventStore } from "@/stores/event-store";

export function useClearConsole() {
  const [isClearing, setIsClearing] = useState(false);
  const clearMessagesLocal = useEventStore((state) => state.clearMessages);

  const clearMessages = useCallback(
    async (source: string) => {
      setIsClearing(true);
      try {
        await eventClient.clearMessages({ source });
        clearMessagesLocal(source);
      } catch (error) {
        console.error(`Failed to clear console for ${source}:`, error);
      } finally {
        setIsClearing(false);
      }
    },
    [clearMessagesLocal],
  );

  return { clearMessages, isClearing };
}
