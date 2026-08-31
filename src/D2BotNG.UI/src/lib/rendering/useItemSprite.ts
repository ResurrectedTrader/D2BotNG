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
import type { HdAppearance } from "./hdRenderer";

export interface UseItemSpriteOptions extends RenderOptions {
  /** Whether to skip loading (for conditional rendering) */
  skip?: boolean;
  /**
   * Draw from D2R's artwork instead of the classic DC6s, when this item can be.
   *
   * Per item rather than per app: D2R art needs a `colorName` the classic path does not carry, and
   * an item code the shipped archives actually hold. An item that cannot be drawn that way falls
   * back to classic silently, which is the only sensible answer — the alternative is a gap in a
   * grid where an item plainly is.
   */
  hd?: HdAppearance;
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
    invTrans = 0,
    ethereal = false,
    backgroundColor = null,
    sockets,
    hd,
  } = options;

  const [bitmap, setBitmap] = useState<ImageBitmap | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const socketsKey = useMemo(
    () =>
      sockets
        ?.map((s) => `${s.code}:${s.itemColor}:${s.invTrans ?? 0}`)
        .join(",") ?? "",
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
    // The style is part of the key, so the two artworks for one item are separate cache entries
    // and toggling the setting does not hand back whichever was rendered first.
    const key = makeSpriteKey(
      code,
      colorShift,
      invTrans,
      ethereal,
      hasBackground,
      socketsKey,
      hd ? `hd:${hd.gfxIndex}:${hd.colorName ?? ""}` : "",
    );

    // One renderer either way — `hd` only changes where its pixels come from, and it falls back
    // per sprite when D2R has no art for a code.
    const factory = () =>
      sockets && sockets.length > 0
        ? renderItemWithSocketsToBitmap(code, {
            colorShift,
            invTrans,
            ethereal,
            sockets,
            hd,
          })
        : renderItemToBitmap(code, {
            colorShift,
            invTrans,
            ethereal,
            backgroundColor,
            hd,
          });

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
  }, [
    code,
    skip,
    colorShift,
    invTrans,
    ethereal,
    backgroundColor,
    socketsKey,
    hd?.gfxIndex,
    hd?.colorName,
  ]);

  return { bitmap, loading, error };
}
