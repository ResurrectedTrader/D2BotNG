/**
 * ItemSprite component - renders a D2 item sprite from DC6 data
 */

import { memo, useEffect, useRef } from "react";
import { useItemSprite, type UseItemSpriteOptions } from "./useItemSprite";

export interface ItemSpriteProps extends UseItemSpriteOptions {
  /** Item code (e.g., 'amu', 'rin', 'vip') */
  code: string | null | undefined;
  /** Additional CSS classes */
  className?: string;
  /** Alt text for accessibility */
  alt?: string;
}

/**
 * Renders a Diablo 2 item sprite with optional sockets.
 * Draws into a canvas from a cached ImageBitmap (no PNG data URLs).
 */
export const ItemSprite = memo(function ItemSprite({
  code,
  className,
  alt,
  colorShift,
  ethereal,
  backgroundColor,
  sockets,
  skip,
}: ItemSpriteProps) {
  const { bitmap, loading, error } = useItemSprite(code, {
    colorShift,
    ethereal,
    backgroundColor,
    sockets,
    skip,
  });

  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !bitmap) return;

    if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
    }

    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(bitmap, 0, 0);
  }, [bitmap]);

  if (!code || error) {
    return null;
  }

  if (loading && !bitmap) {
    return (
      <div className="flex h-12 w-12 items-center justify-center">
        <div className="h-6 w-6 animate-pulse rounded bg-zinc-700" />
      </div>
    );
  }

  if (!bitmap) {
    return null;
  }

  return (
    <canvas
      ref={canvasRef}
      width={bitmap.width}
      height={bitmap.height}
      className={className}
      style={{ imageRendering: "pixelated" }}
      role="img"
      aria-label={alt ?? code}
    />
  );
});
