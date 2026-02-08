/**
 * React hook for rendering item sprites
 */

import { useEffect, useMemo, useState } from "react";
import {
  renderItemToDataUrl,
  renderItemWithSocketsToDataUrl,
  type RenderOptions,
} from "./itemRenderer";

export interface UseItemSpriteOptions extends RenderOptions {
  /** Whether to skip loading (for conditional rendering) */
  skip?: boolean;
}

export interface UseItemSpriteResult {
  /** Data URL of the rendered sprite, or null if loading/error */
  dataUrl: string | null;
  /** Whether the sprite is currently loading */
  loading: boolean;
  /** Error message if loading failed */
  error: string | null;
}

/**
 * Hook to render an item sprite and return it as a data URL
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

  const [dataUrl, setDataUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Memoize sockets to avoid unnecessary re-renders
  const socketsKey = useMemo(
    () => sockets?.map((s) => `${s.code}:${s.itemColor}`).join(",") ?? "",
    [sockets],
  );

  useEffect(() => {
    if (!code || skip) {
      setDataUrl(null);
      setLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);
    // Don't clear dataUrl - keep showing previous image while loading

    const renderFn =
      sockets && sockets.length > 0
        ? renderItemWithSocketsToDataUrl(code, {
            colorShift,
            ethereal,
            sockets,
          })
        : renderItemToDataUrl(code, { colorShift, ethereal, backgroundColor });

    renderFn
      .then((url) => {
        if (!cancelled) {
          setDataUrl(url);
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
  }, [code, skip, colorShift, ethereal, backgroundColor, sockets, socketsKey]);

  return { dataUrl, loading, error };
}
