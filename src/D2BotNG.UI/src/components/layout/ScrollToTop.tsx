import { useEffect, useState, type RefObject } from "react";
import { ChevronUpIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";

/**
 * Back to the top of the scrolled region.
 *
 * Bound to the app's one scroll container rather than the window, because the document does not
 * scroll here — `main` does, so `window.scrollTo` would do nothing and the browser's own
 * Home-key behaviour never reaches it either.
 *
 * Hidden until there is a reason for it: a screenful of scrolling is roughly where a reader stops
 * being able to flick back, and showing it before that puts a control over content for no gain.
 */
const REVEAL_AFTER_PX = 600;

export function ScrollToTop({
  container,
  className,
}: {
  container: RefObject<HTMLElement | null>;
  /** Where it sits — the caller owns that, since what else is in the corner varies by route. */
  className?: string;
}) {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const element = container.current;
    if (!element) return;

    const onScroll = () => setVisible(element.scrollTop > REVEAL_AFTER_PX);
    // Once up front: a route change can leave the container already scrolled, and no scroll event
    // is coming to say so.
    onScroll();
    element.addEventListener("scroll", onScroll, { passive: true });
    return () => element.removeEventListener("scroll", onScroll);
  }, [container]);

  if (!visible) return null;

  return (
    <button
      type="button"
      title="Back to top"
      aria-label="Back to top"
      // Instant, not smooth. A smooth scroll is an animation the browser abandons the moment
      // anything else sets the scroll position — and a virtualized list does exactly that on the
      // way past: rows mount, their sprites resolve a frame later, and each row that grows above
      // the current offset is corrected for by the virtualizer so the content under the reader
      // stays put. Every one of those corrections cancelled the animation, so the button stopped
      // partway up a long list. There is also nothing to see in the frames it would spend: at this
      // distance the rows in between go by far too fast to read.
      onClick={() => container.current?.scrollTo({ top: 0, behavior: "auto" })}
      className={clsx(
        "fixed z-40 rounded-full bg-zinc-800/90 p-2 text-zinc-400 shadow-lg ring-1 ring-zinc-700 backdrop-blur transition-colors hover:bg-zinc-700 hover:text-zinc-100",
        className,
      )}
    >
      <ChevronUpIcon className="h-5 w-5" />
    </button>
  );
}
