/**
 * DC6 file format decoder for Diablo 2 sprites
 */

export interface Dc6Frame {
  width: number;
  height: number;
  offsetX: number;
  offsetY: number;
  pixels: Uint8Array; // Palette indices, row-major [y * width + x]
}

interface Dc6Header {
  version: number;
  subVersion: number;
  directions: number;
  framesPerDirection: number;
}

interface Dc6FrameHeader {
  flip: number;
  width: number;
  height: number;
  offsetX: number;
  offsetY: number;
  nextBlock: number;
  length: number;
}

const HEADER_SIZE = 24;
const FRAME_HEADER_SIZE = 32;

function readInt32(data: DataView, offset: number): number {
  return data.getInt32(offset, true); // little-endian
}

function readHeader(data: DataView): Dc6Header {
  return {
    version: readInt32(data, 0),
    subVersion: readInt32(data, 4),
    // zeros at 8
    // termination at 12
    directions: readInt32(data, 16),
    framesPerDirection: readInt32(data, 20),
  };
}

function readFrameHeader(data: DataView, offset: number): Dc6FrameHeader {
  return {
    flip: readInt32(data, offset),
    width: readInt32(data, offset + 4),
    height: readInt32(data, offset + 8),
    offsetX: readInt32(data, offset + 12),
    offsetY: readInt32(data, offset + 16),
    // zeros at offset + 20
    nextBlock: readInt32(data, offset + 24),
    length: readInt32(data, offset + 28),
  };
}

function decodeFramePixels(
  data: Uint8Array,
  dataOffset: number,
  header: Dc6FrameHeader,
): Uint8Array {
  const pixels = new Uint8Array(header.width * header.height);

  if (header.width <= 0 || header.height <= 0) {
    return pixels;
  }

  let x = 0;
  let y = header.height - 1; // DC6 is stored bottom-to-top
  let offset = dataOffset;
  let bytesRead = 0;

  while (bytesRead < header.length && offset < data.length) {
    const b = data[offset++];
    bytesRead++;

    if (b === 0x80) {
      // Row terminator
      x = 0;
      y--;
      if (y < 0) break;
    } else if ((b & 0x80) === 0x80) {
      // Skip pixels (transparent)
      x += b & 0x7f;
    } else {
      // Literal run
      const count = b;
      for (let i = 0; i < count && offset < data.length; i++) {
        if (x < header.width && y >= 0) {
          pixels[y * header.width + x] = data[offset];
        }
        offset++;
        bytesRead++;
        x++;
      }
    }
  }

  return pixels;
}

/**
 * Decodes the first frame from DC6 data
 */
export function decodeFirstFrame(buffer: ArrayBuffer): Dc6Frame {
  const data = new Uint8Array(buffer);
  const view = new DataView(buffer);

  if (data.length < HEADER_SIZE) {
    throw new Error("DC6 data too small for header");
  }

  const header = readHeader(view);

  if (header.directions < 1 || header.framesPerDirection < 1) {
    throw new Error("Invalid DC6 header: no frames");
  }

  // Read first frame pointer (located after main header)
  const framePointer = readInt32(view, HEADER_SIZE);

  // Read frame header
  const frameHeader = readFrameHeader(view, framePointer);

  // Decode the frame pixels
  const pixels = decodeFramePixels(
    data,
    framePointer + FRAME_HEADER_SIZE,
    frameHeader,
  );

  return {
    width: frameHeader.width,
    height: frameHeader.height,
    offsetX: frameHeader.offsetX,
    offsetY: frameHeader.offsetY,
    pixels,
  };
}

/**
 * Decodes all frames from DC6 data
 */
export function decodeAllFrames(buffer: ArrayBuffer): Dc6Frame[] {
  const data = new Uint8Array(buffer);
  const view = new DataView(buffer);

  if (data.length < HEADER_SIZE) {
    throw new Error("DC6 data too small for header");
  }

  const header = readHeader(view);
  const totalFrames = header.directions * header.framesPerDirection;
  const frames: Dc6Frame[] = [];

  for (let i = 0; i < totalFrames; i++) {
    const framePointer = readInt32(view, HEADER_SIZE + i * 4);
    const frameHeader = readFrameHeader(view, framePointer);
    const pixels = decodeFramePixels(
      data,
      framePointer + FRAME_HEADER_SIZE,
      frameHeader,
    );

    frames.push({
      width: frameHeader.width,
      height: frameHeader.height,
      offsetX: frameHeader.offsetX,
      offsetY: frameHeader.offsetY,
      pixels,
    });
  }

  return frames;
}
