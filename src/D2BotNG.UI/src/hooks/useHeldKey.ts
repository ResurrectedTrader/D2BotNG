import { useCallback, useSyncExternalStore } from "react";

/**
 * One window listener set per key, shared by every subscriber.
 *
 * Not an optimisation detail: a storage grid mounts a tooltip per item, so a stash of a few
 * hundred items with a listener set each meant a thousand handlers on the window and a thousand
 * state updates for one keypress. The key is either down or it is not — that is one fact, so it
 * is held once and broadcast.
 */
interface KeyState {
  held: boolean;
  subscribers: Set<() => void>;
  attached: boolean;
}

const states = new Map<string, KeyState>();

function stateFor(key: string): KeyState {
  let state = states.get(key);
  if (!state) {
    state = { held: false, subscribers: new Set(), attached: false };
    states.set(key, state);
  }
  return state;
}

/**
 * Attached on first use and never detached, which is what keeps the answer true.
 *
 * Detaching with the last subscriber sounds tidy and is wrong: the subscriber here is a hover
 * tooltip, so it exists only while the pointer is over an item. Press Ctrl first and then hover,
 * or move from one item to the next with Ctrl down, and the listeners are being attached AFTER the
 * keydown they needed to see — the key reads as up and the breakdown never appears. Three window
 * listeners for the life of the page is the cheaper half of that trade.
 *
 * The blur reset is not optional. Hold Ctrl, alt-tab away, release it in another window, and the
 * keyup never arrives: without this the key reads as held forever and every tooltip is stuck in
 * its alternate view until you press and release again. `getModifierState` on the next keydown
 * would eventually correct it, but "eventually" means after the user has already seen it wrong.
 */
function attach(key: string, state: KeyState): void {
  const set = (held: boolean) => {
    if (state.held === held) return;
    state.held = held;
    for (const notify of state.subscribers) notify();
  };
  const down = (e: KeyboardEvent) => {
    if (e.key === key) set(true);
  };
  const up = (e: KeyboardEvent) => {
    if (e.key === key) set(false);
  };
  const clear = () => set(false);

  window.addEventListener("keydown", down);
  window.addEventListener("keyup", up);
  window.addEventListener("blur", clear);
  state.attached = true;
}

function subscribe(key: string, notify: () => void): () => void {
  const state = stateFor(key);
  if (!state.attached) attach(key, state);
  state.subscribers.add(notify);
  return () => state.subscribers.delete(notify);
}

/**
 * Whether a modifier key is currently held.
 *
 * Listens on the window rather than on an element, because the thing that reacts (a hover tooltip)
 * never has focus — the pointer is over it, the keyboard is somewhere else entirely.
 */
export function useHeldKey(key: string): boolean {
  const subscribeToKey = useCallback(
    (notify: () => void) => subscribe(key, notify),
    [key],
  );
  return useSyncExternalStore(
    subscribeToKey,
    () => states.get(key)?.held ?? false,
    () => false,
  );
}
