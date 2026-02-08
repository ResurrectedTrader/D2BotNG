/**
 * Renders Diablo 2 item images from DC6 sprites
 */

import { decodeFirstFrame } from "./dc6Decoder";
import { getPaletteManager, loadPaletteData } from "./paletteManager";

// DC6 cache
const dc6Cache = new Map<string, ArrayBuffer>();
let fallbackDc6: ArrayBuffer | null = null;

/**
 * Fetches and caches a DC6 file
 */
async function getDc6Data(code: string): Promise<ArrayBuffer> {
  const key = code.toLowerCase();

  if (dc6Cache.has(key)) {
    return dc6Cache.get(key)!;
  }

  const response = await fetch(`/assets/rendering/dc6/${key}.dc6`);
  if (!response.ok) {
    // Return fallback
    if (!fallbackDc6) {
      const response = await fetch("/assets/rendering/dc6/box.dc6");
      fallbackDc6 = await response.arrayBuffer();
    }
    return fallbackDc6;
  }
  const buffer = await response.arrayBuffer();
  dc6Cache.set(key, buffer);
  return buffer;
}

/**
 * Calculates grid size from frame dimensions
 */
function calculateGridSize(
  width: number,
  height: number,
): { x: number; y: number } {
  const x = width < 37 ? 1 : 2;
  const y = height < 30 ? 1 : height < 65 ? 2 : height < 95 ? 3 : 4;
  return { x, y };
}

export interface RenderOptions {
  /** Item color shift index (-1 for no shift) */
  colorShift?: number;
  /** Whether the item is ethereal (semi-transparent) */
  ethereal?: boolean;
  /** Background color (null for transparent) */
  backgroundColor?: { r: number; g: number; b: number } | null;
  /** Socketed items to render on top (each needs code and itemColor) */
  sockets?: Array<{ code: string; itemColor: number }>;
}

/**
 * Renders an item sprite to ImageData
 */
export async function renderItemSprite(
  code: string,
  options: RenderOptions = {},
): Promise<ImageData> {
  const { colorShift = -1, ethereal = false, backgroundColor = null } = options;

  // Ensure palette is loaded
  await loadPaletteData();
  const paletteManager = getPaletteManager();

  // Get and decode DC6
  const dc6Data = await getDc6Data(code);
  const frame = decodeFirstFrame(dc6Data);

  // Create shifted palette
  const palette = paletteManager.createShiftedPalette(colorShift);

  // Create ImageData
  const imageData = new ImageData(frame.width, frame.height);
  const pixels = imageData.data;

  const alpha = ethereal ? 127 : 255;

  for (let y = 0; y < frame.height; y++) {
    for (let x = 0; x < frame.width; x++) {
      const paletteIndex = frame.pixels[y * frame.width + x];
      const pixelIndex = (y * frame.width + x) * 4;

      if (paletteIndex === 0) {
        // Transparent or background
        if (backgroundColor) {
          pixels[pixelIndex] = backgroundColor.r;
          pixels[pixelIndex + 1] = backgroundColor.g;
          pixels[pixelIndex + 2] = backgroundColor.b;
          pixels[pixelIndex + 3] = 20; // Semi-transparent background
        } else {
          pixels[pixelIndex + 3] = 0; // Fully transparent
        }
      } else {
        const color = palette[paletteIndex];
        pixels[pixelIndex] = color.r;
        pixels[pixelIndex + 1] = color.g;
        pixels[pixelIndex + 2] = color.b;
        pixels[pixelIndex + 3] = alpha;
      }
    }
  }

  return imageData;
}

/**
 * Renders an item sprite to a canvas
 */
export async function renderItemToCanvas(
  canvas: HTMLCanvasElement,
  code: string,
  options: RenderOptions = {},
): Promise<void> {
  const imageData = await renderItemSprite(code, options);

  canvas.width = imageData.width;
  canvas.height = imageData.height;

  const ctx = canvas.getContext("2d");
  if (!ctx) {
    throw new Error("Could not get canvas context");
  }

  ctx.putImageData(imageData, 0, 0);
}

/**
 * Renders an item sprite to a data URL
 * Uses grid-based sizing for consistent dimensions with socketed rendering
 */
export async function renderItemToDataUrl(
  code: string,
  options: RenderOptions = {},
): Promise<string> {
  const imageData = await renderItemSprite(code, options);

  // Use grid-based sizing for consistent dimensions
  const gridSize = calculateGridSize(imageData.width, imageData.height);
  const width = gridSize.x * 30 - 1;
  const height = gridSize.y * 30 - 1;

  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext("2d");
  if (!ctx) {
    throw new Error("Could not get canvas context");
  }

  // Center the sprite in the grid-based canvas
  const itemX = Math.floor((width - imageData.width) / 2);
  const itemY = Math.floor((height - imageData.height) / 2);
  ctx.putImageData(imageData, itemX, itemY);
  return canvas.toDataURL("image/png");
}

/**
 * Gets frame info without rendering (for layout calculations)
 */
export async function getItemFrameInfo(
  code: string,
): Promise<{
  width: number;
  height: number;
  gridSize: { x: number; y: number };
}> {
  const dc6Data = await getDc6Data(code);
  const frame = decodeFirstFrame(dc6Data);
  const gridSize = calculateGridSize(frame.width, frame.height);

  return {
    width: frame.width,
    height: frame.height,
    gridSize,
  };
}

/**
 * Preloads DC6 files for a list of item codes
 */
export async function preloadItems(codes: string[]): Promise<void> {
  await Promise.all(codes.map((code) => getDc6Data(code)));
}

/**
 * Clears the DC6 cache
 */
export function clearCache(): void {
  dc6Cache.clear();
}

interface Point {
  x: number;
  y: number;
}

const SOCKET_SPACING = 14;

/**
 * Calculates socket positions based on item grid size and socket count
 * Matches the C# implementation positioning
 */
function getSocketPositions(
  count: number,
  gridSize: { x: number; y: number },
  baseX: number,
): Point[] {
  const positions: Point[] = [];
  const offsetY = -1;

  const x1 = baseX;
  const x2 = x1 + SOCKET_SPACING;
  const x3 = x2 + SOCKET_SPACING;
  const y1 = 2;
  const y2 = y1 + SOCKET_SPACING * 2 + 1;
  const y3 = y2 + SOCKET_SPACING * 2 + 1;
  const y4 = y3 + SOCKET_SPACING * 2 + 1;

  switch (count) {
    case 1:
      if (gridSize.y === 2) {
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y1 + SOCKET_SPACING + offsetY,
        });
      } else if (gridSize.y === 3) {
        positions.push({ x: gridSize.x === 1 ? x1 : x2, y: y2 + offsetY });
      } else {
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y2 + SOCKET_SPACING + offsetY,
        });
      }
      break;

    case 2:
      if (gridSize.y === 2) {
        positions.push({ x: gridSize.x === 1 ? x1 : x2, y: y1 + offsetY });
        positions.push({ x: gridSize.x === 1 ? x1 : x2, y: y2 + offsetY });
      } else if (gridSize.y === 3) {
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y1 + SOCKET_SPACING + offsetY,
        });
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y2 + SOCKET_SPACING + offsetY,
        });
      } else {
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y1 + SOCKET_SPACING + offsetY,
        });
        positions.push({
          x: gridSize.x === 1 ? x1 : x2,
          y: y3 + SOCKET_SPACING + offsetY,
        });
      }
      break;

    case 3:
      if (gridSize.y === 2) {
        positions.push({ x: x1, y: y1 + offsetY });
        positions.push({ x: x3, y: y1 + offsetY });
        positions.push({ x: x2, y: y2 + offsetY });
      } else if (gridSize.y === 3) {
        const x = gridSize.x === 1 ? x1 : x2;
        positions.push({ x, y: y1 + offsetY });
        positions.push({ x, y: y2 + offsetY });
        positions.push({ x, y: y3 + offsetY });
      } else {
        const x = gridSize.x === 1 ? x1 : x2;
        positions.push({ x, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x, y: y2 + SOCKET_SPACING + offsetY });
        positions.push({ x, y: y3 + SOCKET_SPACING + offsetY });
      }
      break;

    case 4:
      if (gridSize.y === 3) {
        positions.push({ x: x1, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x1, y: y2 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y2 + SOCKET_SPACING + offsetY });
      } else if (gridSize.y === 2) {
        positions.push({ x: x1, y: y1 + offsetY });
        positions.push({ x: x3, y: y1 + offsetY });
        positions.push({ x: x1, y: y2 + offsetY });
        positions.push({ x: x3, y: y2 + offsetY });
      } else {
        const x = gridSize.x === 1 ? x1 : x2;
        positions.push({ x, y: y1 + offsetY });
        positions.push({ x, y: y2 + offsetY });
        positions.push({ x, y: y3 + offsetY });
        positions.push({ x, y: y4 + offsetY });
      }
      break;

    case 5:
      if (gridSize.y === 3) {
        positions.push({ x: x1, y: y1 + offsetY });
        positions.push({ x: x3, y: y1 + offsetY });
        positions.push({ x: x2, y: y2 + offsetY });
        positions.push({ x: x1, y: y3 + offsetY });
        positions.push({ x: x3, y: y3 + offsetY });
      } else {
        positions.push({ x: x1, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x2, y: y2 + SOCKET_SPACING + offsetY });
        positions.push({ x: x1, y: y3 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y3 + SOCKET_SPACING + offsetY });
      }
      break;

    case 6:
      if (gridSize.y === 3) {
        positions.push({ x: x1, y: y1 + offsetY });
        positions.push({ x: x3, y: y1 + offsetY });
        positions.push({ x: x1, y: y2 + offsetY });
        positions.push({ x: x3, y: y2 + offsetY });
        positions.push({ x: x1, y: y3 + offsetY });
        positions.push({ x: x3, y: y3 + offsetY });
      } else {
        positions.push({ x: x1, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y1 + SOCKET_SPACING + offsetY });
        positions.push({ x: x1, y: y2 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y2 + SOCKET_SPACING + offsetY });
        positions.push({ x: x1, y: y3 + SOCKET_SPACING + offsetY });
        positions.push({ x: x3, y: y3 + SOCKET_SPACING + offsetY });
      }
      break;
  }

  return positions;
}

/**
 * Renders an item with sockets to a data URL
 */
export async function renderItemWithSocketsToDataUrl(
  code: string,
  options: RenderOptions = {},
): Promise<string> {
  const { colorShift = -1, ethereal = false, sockets = [] } = options;

  // Ensure palette is loaded
  await loadPaletteData();

  // Get base item frame info
  const dc6Data = await getDc6Data(code);
  const frame = decodeFirstFrame(dc6Data);
  const gridSize = calculateGridSize(frame.width, frame.height);

  // Calculate canvas size based on grid
  const width = gridSize.x * 30 - 1;
  const height = gridSize.y * 30 - 1;

  // Create canvas
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext("2d")!;

  // Clear to transparent
  ctx.clearRect(0, 0, width, height);

  // Render base item centered
  const baseImageData = await renderItemSprite(code, { colorShift, ethereal });
  const itemX = Math.floor((width - baseImageData.width) / 2);
  const itemY = Math.floor((height - baseImageData.height) / 2);
  ctx.putImageData(baseImageData, itemX, itemY);

  // Render sockets if present
  if (sockets.length > 0) {
    const positions = getSocketPositions(sockets.length, gridSize, itemX);

    for (let i = 0; i < sockets.length && i < positions.length; i++) {
      const socket = sockets[i];
      const pos = positions[i];

      // Render socket item (empty sockets use 'gemsocket' code)
      const isEmptySocket = socket.code === "gemsocket";
      const socketImageData = await renderItemSprite(socket.code, {
        colorShift: socket.itemColor,
        ethereal: isEmptySocket, // Empty sockets are semi-transparent
      });

      // Create temp canvas for the socket to preserve alpha compositing
      const socketCanvas = document.createElement("canvas");
      socketCanvas.width = socketImageData.width;
      socketCanvas.height = socketImageData.height;
      const socketCtx = socketCanvas.getContext("2d")!;
      socketCtx.putImageData(socketImageData, 0, 0);

      // Adjust position for empty sockets
      const drawX = isEmptySocket ? pos.x - 1 : pos.x;
      const drawY = isEmptySocket ? pos.y + 1 : pos.y;

      ctx.drawImage(socketCanvas, drawX, drawY);
    }
  }

  return canvas.toDataURL("image/png");
}
