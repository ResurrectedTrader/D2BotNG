import { useEffect, useRef, useState } from "react";

/**
 * A value that settles at most once per interval, rather than tracking every change.
 *
 * A plain trailing debounce was wrong for the thing this exists for. It re-arms its timer on every
 * change, so it only ever settles after a QUIET period — and a running bot re-reports several
 * times a second, which is faster than the delay. The value never settled at all, so the character
 * on screen stayed frozen at whatever it was when it was opened, for exactly as long as the profile
 * kept running. The absorbing is still wanted (each settle refetches a whole inventory); the "wait
 * for silence" part is not.
 *
 * So: the first change through settles immediately, and later ones are held until the interval has
 * passed since the last settle. Under sustained churn that is one update per interval; after a
 * burst the trailing timer still delivers the final value, so nothing is dropped.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);
  const lastSettledAt = useRef(0);

  useEffect(() => {
    if (Object.is(settled, value)) return;

    const elapsed = Date.now() - lastSettledAt.current;
    if (elapsed >= delayMs) {
      lastSettledAt.current = Date.now();
      setSettled(value);
      return;
    }

    const timer = setTimeout(() => {
      lastSettledAt.current = Date.now();
      setSettled(value);
    }, delayMs - elapsed);
    return () => clearTimeout(timer);
  }, [value, delayMs, settled]);

  return settled;
}
