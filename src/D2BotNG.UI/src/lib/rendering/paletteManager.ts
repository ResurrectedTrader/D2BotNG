/**
 * Manages Diablo 2 palettes and color shifting for item rendering
 */

export interface Color {
  r: number;
  g: number;
  b: number;
  a: number;
}

export class PaletteManager {
  private basePalette: Color[] = [];
  private colorMap: Uint8Array = new Uint8Array(0);
  private loaded = false;

  /**
   * Loads palette data from the provided buffers
   */
  load(palData: ArrayBuffer, colorMapData: ArrayBuffer): void {
    const pal = new Uint8Array(palData);
    this.colorMap = new Uint8Array(colorMapData);

    // Load base palette (768 bytes = 256 colors * 3 bytes RGB, stored as BGR)
    this.basePalette = [];
    for (let i = 0; i < 256; i++) {
      const b = pal[i * 3];
      const g = pal[i * 3 + 1];
      const r = pal[i * 3 + 2];
      this.basePalette.push({ r, g, b, a: 255 });
    }

    this.loaded = true;
  }

  /**
   * Checks if the palette data has been loaded
   */
  isLoaded(): boolean {
    return this.loaded;
  }

  /**
   * Gets the base palette color for an index
   */
  getColor(index: number): Color {
    if (index < 0 || index >= 256) {
      return { r: 0, g: 0, b: 0, a: 0 };
    }
    return this.basePalette[index];
  }

  /**
   * Gets a color-shifted palette color
   * @param index Palette index (0-255)
   * @param shiftColor Color shift value (-1 for no shift, 0+ for shift index)
   */
  getShiftedColor(index: number, shiftColor: number): Color {
    if (index < 0 || index >= 256) {
      return { r: 0, g: 0, b: 0, a: 0 };
    }

    if (shiftColor < 0) {
      return this.basePalette[index];
    }

    // Apply color map shift
    const mapIndex = shiftColor * 256 + index;
    if (mapIndex < 0 || mapIndex >= this.colorMap.length) {
      return this.basePalette[index];
    }

    const shiftedIndex = this.colorMap[mapIndex];
    return this.basePalette[shiftedIndex];
  }

  /**
   * Creates a shifted palette array for a specific shift value
   */
  createShiftedPalette(shiftColor: number): Color[] {
    const palette: Color[] = [];
    for (let i = 0; i < 256; i++) {
      palette.push(this.getShiftedColor(i, shiftColor));
    }
    return palette;
  }
}

// Singleton instance
let instance: PaletteManager | null = null;

/**
 * Gets the singleton PaletteManager instance
 */
export function getPaletteManager(): PaletteManager {
  if (!instance) {
    instance = new PaletteManager();
  }
  return instance;
}

/**
 * Loads palette data from assets
 */
export async function loadPaletteData(): Promise<PaletteManager> {
  const manager = getPaletteManager();

  if (manager.isLoaded()) {
    return manager;
  }

  const [palResponse, colorMapResponse] = await Promise.all([
    fetch("/assets/rendering/pal.dat"),
    fetch("/assets/rendering/invgreybrown.dat"),
  ]);

  const palData = await palResponse.arrayBuffer();
  const colorMapData = await colorMapResponse.arrayBuffer();

  manager.load(palData, colorMapData);

  return manager;
}
