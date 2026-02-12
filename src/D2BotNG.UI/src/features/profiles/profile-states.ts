/**
 * UI-side mirror of the backend state machine (ProfileInstance.IsValidTransition).
 *
 * Valid transitions:
 *   Stopped  → Starting
 *   Starting → Running | Error
 *   Running  → Stopping | Error
 *   Stopping → Stopped
 *   Error    → Starting | Stopping | Stopped
 */

import { RunState } from "@/generated/common_pb";

/** Profile can be started (state allows transition to Starting). */
export function canStart(state: RunState | undefined): boolean {
  if (state === undefined) return true; // No status = treat as stopped
  return state === RunState.STOPPED || state === RunState.ERROR;
}

/** Profile can be stopped (state allows transition to Stopping). */
export function canStop(state: RunState | undefined): boolean {
  if (state === undefined) return false;
  return state === RunState.RUNNING || state === RunState.ERROR;
}

/** Profile is actively running with a game process (has a window). */
export function isActive(state: RunState | undefined): boolean {
  if (state === undefined) return false;
  return state === RunState.STARTING || state === RunState.RUNNING;
}
