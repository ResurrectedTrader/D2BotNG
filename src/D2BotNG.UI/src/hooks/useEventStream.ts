/**
 * Event Stream Hook - Connects to the gRPC StreamEvents endpoint
 *
 * Manages the single event stream connection for all real-time updates.
 * Handles connection, reconnection, and cleanup automatically.
 */

import { useEffect, useRef } from "react";
import { useEventStore } from "@/stores/event-store";
import { eventClient } from "@/lib/grpc-client";

const RETRY_DELAY_MS = 5000;

/**
 * Hook that starts streaming events from gRPC and updates the event store.
 * Call this once at the app root to establish the event stream.
 *
 * Automatically handles:
 * - Initial connection
 * - Processing all event types
 * - Reconnection on error with fixed delay
 * - Cleanup on unmount
 */
export function useEventStream() {
  const handleEvent = useEventStore((state) => state.handleEvent);
  const setConnected = useEventStore((state) => state.setConnected);
  const reset = useEventStore((state) => state.reset);
  const retryTimeoutRef = useRef<number | null>(null);

  useEffect(() => {
    const abortController = new AbortController();
    let isActive = true;
    let hasReceivedFirstEvent = false;

    async function startStream() {
      hasReceivedFirstEvent = false;
      try {
        const stream = eventClient.streamEvents(
          {},
          { signal: abortController.signal },
        );

        for await (const event of stream) {
          if (!isActive) break;
          // Mark as connected only after receiving the first event
          if (!hasReceivedFirstEvent) {
            hasReceivedFirstEvent = true;
            setConnected(true);
          }
          handleEvent(event);
        }

        // Stream ended normally (server closed)
        if (isActive) {
          setConnected(false);
          scheduleRetry();
        }
      } catch (error) {
        // Ignore abort errors (expected on cleanup)
        if (error instanceof Error && error.name === "AbortError") {
          return;
        }

        console.error("Event stream error:", error);
        setConnected(false);

        // Retry after delay if still active
        if (isActive) {
          scheduleRetry();
        }
      }
    }

    function scheduleRetry() {
      retryTimeoutRef.current = window.setTimeout(() => {
        if (isActive) {
          startStream();
        }
      }, RETRY_DELAY_MS);
    }

    startStream();

    // Cleanup function
    return () => {
      isActive = false;
      abortController.abort();
      reset();

      if (retryTimeoutRef.current !== null) {
        clearTimeout(retryTimeoutRef.current);
        retryTimeoutRef.current = null;
      }
    };
  }, [handleEvent, setConnected, reset]);
}
