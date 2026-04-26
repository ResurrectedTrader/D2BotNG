/**
 * LRU cache for rendered item sprites (ImageBitmap).
 *
 * Bounds the number of cached entries; evicted bitmaps are released to GC
 * (not explicitly closed) because consumer components may still hold refs in
 * React state. Browser GC reclaims them once no reference remains.
 */

const MAX_ENTRIES = 1000;

const cache = new Map<string, Promise<ImageBitmap>>();

export function makeSpriteKey(
  code: string,
  colorShift: number,
  ethereal: boolean,
  hasBackground: boolean,
  socketsKey: string,
): string {
  return `${code}|${colorShift}|${ethereal ? 1 : 0}|${hasBackground ? 1 : 0}|${socketsKey}`;
}

export function getCachedSprite(
  key: string,
  factory: () => Promise<ImageBitmap>,
): Promise<ImageBitmap> {
  const existing = cache.get(key);
  if (existing) {
    // Refresh LRU position on hit
    cache.delete(key);
    cache.set(key, existing);
    return existing;
  }

  const promise = factory().catch((err) => {
    cache.delete(key);
    throw err;
  });
  cache.set(key, promise);

  if (cache.size > MAX_ENTRIES) {
    const oldestKey = cache.keys().next().value;
    if (oldestKey !== undefined) {
      cache.delete(oldestKey);
    }
  }

  return promise;
}

export function clearSpriteCache(): void {
  cache.clear();
}
