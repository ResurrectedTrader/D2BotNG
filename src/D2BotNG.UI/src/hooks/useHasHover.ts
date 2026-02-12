import { useSyncExternalStore } from "react";

const query = window.matchMedia("(hover: hover)");

function subscribe(callback: () => void) {
  query.addEventListener("change", callback);
  return () => query.removeEventListener("change", callback);
}

function getSnapshot() {
  return query.matches;
}

/**
 * Hook to check if the device has hover capability (desktop/mouse).
 * Reactively updates when hover capability changes (e.g. mouse connected/disconnected).
 */
export function useHasHover(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot);
}
