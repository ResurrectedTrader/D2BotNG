/**
 * Captures an ItemTooltipContent render to a PNG Blob.
 *
 * Renders the tooltip off-screen via a fresh React root, waits for the sprite
 * canvas and web fonts to be ready, then rasterizes via html-to-image.
 *
 * The output is the same DOM the user sees on hover — no separate PNG renderer.
 */

import { createElement } from "react";
import { createRoot, type Root } from "react-dom/client";
import { toBlob } from "html-to-image";
import {
  getCachedSprite,
  makeSpriteKey,
  renderItemToBitmap,
  renderItemWithSocketsToBitmap,
} from "@/lib/rendering";
import type { Item } from "@/generated/items_pb";
import { ItemTooltipContent } from "./ItemTooltip";
import { isEthereal } from "./item-utils";

const nextFrame = () =>
  new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

/**
 * Warm the global sprite cache for this item using the same key
 * `useItemSprite` will look up later — this way the offscreen render finds
 * the bitmap on a microtask instead of re-decoding from DC6.
 */
async function preloadSprite(item: Item): Promise<void> {
  try {
    const ethereal = isEthereal(item);
    const socketsKey = item.sockets
      .map((s) => `${s.code}:${s.itemColor}:${s.invTrans ?? 0}`)
      .join(",");
    const key = makeSpriteKey(
      item.code,
      item.itemColor,
      item.invTrans,
      ethereal,
      false,
      socketsKey,
    );
    const factory =
      item.sockets.length > 0
        ? () =>
            renderItemWithSocketsToBitmap(item.code, {
              colorShift: item.itemColor,
              invTrans: item.invTrans,
              ethereal,
              sockets: item.sockets,
            })
        : () =>
            renderItemToBitmap(item.code, {
              colorShift: item.itemColor,
              invTrans: item.invTrans,
              ethereal,
              backgroundColor: null,
            });
    await getCachedSprite(key, factory);
  } catch {
    // Sprite render failures shouldn't block capture — the tooltip still has
    // useful text content.
  }
}

/** Force the Exocet font to load before render so it's available at capture time. */
async function preloadFont(): Promise<void> {
  if (!document.fonts || typeof document.fonts.load !== "function") return;
  try {
    await document.fonts.load('16px "Exocet Blizzard OT Light"');
  } catch {
    // Font load failures fall through to whatever the browser substitutes.
  }
}

/**
 * Wait for the offscreen tooltip's sprite canvas to be drawn (non-zero size
 * and ImageBitmap copied in). Returns once a canvas with non-zero width is
 * present, or after the timeout regardless — the tooltip still has useful
 * text content even if the sprite isn't ready.
 */
async function waitForSpriteCanvas(
  container: HTMLElement,
  maxFrames: number,
): Promise<void> {
  for (let i = 0; i < maxFrames; i++) {
    const canvas = container.querySelector("canvas");
    if (canvas && canvas.width > 0 && canvas.height > 0) return;
    await nextFrame();
  }
}

export async function captureItemTooltipBlob(item: Item): Promise<Blob> {
  await Promise.all([preloadSprite(item), preloadFont()]);

  const container = document.createElement("div");
  container.style.position = "fixed";
  container.style.top = "-10000px";
  container.style.left = "-10000px";
  container.style.zIndex = "-1";
  // The tooltip uses whitespace-nowrap; let it size to its natural width.
  container.style.width = "max-content";
  document.body.appendChild(container);

  let root: Root | null = null;
  try {
    root = createRoot(container);
    root.render(createElement(ItemTooltipContent, { item }));

    // Initial commit window: useItemSprite resolves its cached promise (microtask)
    // and React commits the canvas element on the next frame.
    await nextFrame();
    await nextFrame();
    // Then poll for the canvas to have actual content. Bounded so we still
    // produce something if the sprite never resolves.
    await waitForSpriteCanvas(container, 30);
    if (document.fonts && typeof document.fonts.ready?.then === "function") {
      await document.fonts.ready;
    }
    await nextFrame();

    const target = container.firstElementChild as HTMLElement | null;
    if (!target) {
      throw new Error("Tooltip render produced no DOM");
    }

    const blob = await toBlob(target, {
      pixelRatio: 1,
      cacheBust: true,
      // Preserve the tooltip's own bg-zinc-900/95 background.
      backgroundColor: undefined,
    });
    if (!blob) {
      throw new Error("html-to-image returned no blob");
    }
    return blob;
  } finally {
    // Defer unmount/removal: html-to-image can hold onto cloned references
    // briefly during its own promise chain.
    const finalRoot = root;
    const finalContainer = container;
    setTimeout(() => {
      try {
        finalRoot?.unmount();
      } catch {
        /* ignore */
      }
      finalContainer.remove();
    }, 0);
  }
}
