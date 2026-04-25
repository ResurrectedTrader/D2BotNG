/**
 * React hook for rendering item sprites
 */

import { useEffect, useMemo, useState } from "react";
import {
  renderItemToBitmap,
  renderItemWithSocketsToBitmap,
  type RenderOptions,
} from "./itemRenderer";
import { getCachedSprite, makeSpriteKey } from "./spriteCache";

export interface UseItemSpriteOptions extends RenderOptions {
  /** Whether to skip loading (for conditional rendering) */
  skip?: boolean;
}

export interface UseItemSpriteResult {
  /** Cached bitmap for the rendered sprite, or null if loading/error */
  bitmap: ImageBitmap | null;
  /** Whether the sprite is currently loading */
  loading: boolean;
  /** Error message if loading failed */
  error: string | null;
}

/**
 * Hook to render an item sprite and return its cached ImageBitmap.
 * Bitmaps are owned by the global LRU sprite cache; do not call .close() on them.
 */
export function useItemSprite(
  code: string | null | undefined,
  options: UseItemSpriteOptions = {},
): UseItemSpriteResult {
  const {
    skip = false,
    colorShift = -1,
    ethereal = false,
    backgroundColor = null,
    sockets,
  } = options;

  const [bitmap, setBitmap] = useState<ImageBitmap | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const socketsKey = useMemo(
    () => sockets?.map((s) => `${s.code}:${s.itemColor}`).join(",") ?? "",
    [sockets],
  );

  useEffect(() => {
    if (!code || skip) {
      setBitmap(null);
      setLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);
    // Don't clear bitmap - keep showing previous image while loading

    const hasBackground = backgroundColor !== null;
    const key = makeSpriteKey(
      code,
      colorShift,
      ethereal,
      hasBackground,
      socketsKey,
    );

    const factory =
      sockets && sockets.length > 0
        ? () =>
            renderItemWithSocketsToBitmap(code, {
              colorShift,
              ethereal,
              sockets,
            })
        : () =>
            renderItemToBitmap(code, { colorShift, ethereal, backgroundColor });

    getCachedSprite(key, factory)
      .then((bmp) => {
        if (!cancelled) {
          setBitmap(bmp);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(
            err instanceof Error ? err.message : "Failed to render sprite",
          );
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
    // socketsKey is the content hash for `sockets`; including the array itself
    // would re-fire the effect on every parent render (fresh array reference).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [code, skip, colorShift, ethereal, backgroundColor, socketsKey]);

  return { bitmap, loading, error };
}
