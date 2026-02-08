/**
 * ItemSprite component - renders a D2 item sprite from DC6 data
 */

import { memo } from "react";
import { useItemSprite, type UseItemSpriteOptions } from "./useItemSprite";

export interface ItemSpriteProps extends UseItemSpriteOptions {
  /** Item code (e.g., 'amu', 'rin', 'vip') */
  code: string | null | undefined;
  /** Additional CSS classes */
  className?: string;
  /** Alt text for the image */
  alt?: string;
}

/**
 * Renders a Diablo 2 item sprite with optional sockets
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
  const { dataUrl, loading, error } = useItemSprite(code, {
    colorShift,
    ethereal,
    backgroundColor,
    sockets,
    skip,
  });

  // Show loading placeholder on initial load (no dataUrl yet)
  if (!code || error) {
    return null;
  }

  if (loading && !dataUrl) {
    return (
      <div className="flex h-12 w-12 items-center justify-center">
        <div className="h-6 w-6 animate-pulse rounded bg-zinc-700" />
      </div>
    );
  }

  if (!dataUrl) {
    return null;
  }

  return (
    <img
      src={dataUrl}
      alt={alt ?? code}
      className={className}
      style={{ imageRendering: "pixelated" }}
    />
  );
});
