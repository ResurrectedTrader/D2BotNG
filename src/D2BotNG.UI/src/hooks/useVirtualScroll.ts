import { useEffect, useRef, useState } from "react";

/**
 * What a virtualizer needs to know about a scroll container it does not own.
 *
 * `@tanstack/react-virtual` assumes the scrolled element is either the window or one the caller
 * holds. Neither is true here: the app scrolls its layout's `main`, and the virtualized lists are
 * several routes below it, sitting some way down a page that also holds filters and headers. So
 * two things have to be supplied — the element that actually scrolls, and `scrollMargin`, the
 * distance from the top of the scrolled content to the top of the list, which is what makes a
 * virtual item's offset mean a position on the page rather than within the list.
 *
 * The margin is measured rather than assumed because everything above the list can move: filter
 * controls wrap onto another line at narrow widths, and headers collapse. A stale margin puts every
 * row at the wrong offset — the list appears to slide out from under its own scrollbar.
 *
 * Attach `parentRef` to the element that wraps the virtual rows.
 */
export function useVirtualScroll<T extends HTMLElement>() {
  const parentRef = useRef<T>(null);
  const [scrollElement, setScrollElement] = useState<HTMLElement | null>(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    setScrollElement(document.querySelector("main"));
  }, []);

  useEffect(() => {
    const parent = parentRef.current;
    if (!parent || !scrollElement) return;

    const measure = () => {
      const parentRect = parent.getBoundingClientRect();
      const scrollRect = scrollElement.getBoundingClientRect();
      setScrollMargin(
        parentRect.top - scrollRect.top + scrollElement.scrollTop,
      );
      setWidth(parentRect.width);
    };
    measure();

    const observer = new ResizeObserver(measure);
    observer.observe(scrollElement);
    observer.observe(parent);
    // And whatever sits directly above, because that is the one thing that moves this list without
    // resizing it or the scroller: a filter panel that wraps onto another line changes its own
    // height and nothing else's, so neither observation above would fire.
    const sibling = parent.previousElementSibling;
    if (sibling instanceof HTMLElement) observer.observe(sibling);

    return () => observer.disconnect();
  }, [scrollElement]);

  // `width` is returned for a list whose rows are measured rather than fixed: their height is a
  // function of it, since text wraps, so a change invalidates every cached measurement — including
  // the rows not currently rendered, which is exactly what a virtualizer cannot notice on its own.
  // Browser zoom is the case that makes this unavoidable rather than a nicety: it changes how many
  // CSS pixels the viewport holds, so it reflows everything without any element resizing itself.
  return { parentRef, scrollElement, scrollMargin, width };
}
