import { useEffect, useRef } from "react";

/** Roughly a line and a page, for the wheels that report in those units rather than in pixels. */
const LINE_HEIGHT = 16;

/** The nearest ancestor of the event's target that can actually scroll the way the wheel turned. */
function scrollTarget(
  node: EventTarget | null,
  deltaY: number,
  deltaX: number,
): HTMLElement | null {
  // A wheel event's target can be a text node, and scrolling is a question about elements.
  const from =
    node instanceof HTMLElement
      ? node
      : node instanceof Node
        ? node.parentElement
        : null;

  for (let el = from; el; el = el.parentElement) {
    const style = getComputedStyle(el);
    const canY =
      (style.overflowY === "auto" || style.overflowY === "scroll") &&
      el.scrollHeight > el.clientHeight;
    const canX =
      (style.overflowX === "auto" || style.overflowX === "scroll") &&
      el.scrollWidth > el.clientWidth;
    // Whichever axis the wheel actually moved — a vertical wheel over a horizontally scrolling
    // strip should keep going up to the page rather than being swallowed by it.
    if ((deltaY !== 0 && canY) || (deltaX !== 0 && canX)) return el;
  }
  return null;
}

/**
 * Makes Ctrl+scroll scroll this view instead of zooming the app.
 *
 * The two gestures collided. Ctrl held is what shows the roll-range breakdown, so on a view where
 * that is the point — item search, where every result switches at once — reading down the results
 * with the key held zoomed the whole page instead. Zoom is not a harmless mistake here either: it
 * reflows the list, invalidating the height every virtualized row was placed by.
 *
 * Suppressing the zoom is only half of it. The default action of a Ctrl+wheel IS the zoom, so
 * cancelling it leaves the gesture doing nothing at all — the list would not scroll while the key
 * was down. So the scroll is performed here instead, on whichever ancestor of the pointer can
 * actually take it, which keeps a dropdown's own list scrolling rather than the page behind it.
 *
 * Scoped to one element, so zoom still works everywhere else in the app, and the keyboard's
 * Ctrl +/- is untouched anywhere. A trackpad pinch arrives as this same event and so scrolls here
 * too, which is the same trade in the same place.
 *
 * `passive: false` is load-bearing: a listener that has not said it may cancel is not allowed to,
 * and the browser assumes wheel listeners are passive unless told otherwise.
 */
export function useCtrlWheelScroll<T extends HTMLElement>() {
  const ref = useRef<T>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;

    const onWheel = (e: WheelEvent) => {
      if (!e.ctrlKey) return;
      e.preventDefault();

      const target = scrollTarget(e.target, e.deltaY, e.deltaX);
      if (!target) return;

      // A wheel reports in pixels, lines or pages depending on the device, and only the first is
      // already the number to scroll by.
      const scale =
        e.deltaMode === 1
          ? LINE_HEIGHT
          : e.deltaMode === 2
            ? target.clientHeight
            : 1;

      target.scrollBy({
        top: e.deltaY * scale,
        left: e.deltaX * scale,
        behavior: "instant",
      });
    };

    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, []);

  return ref;
}
