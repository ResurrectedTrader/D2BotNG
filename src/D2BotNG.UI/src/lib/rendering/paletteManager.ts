/**
 * Manages Diablo 2 palettes and color shifting for item rendering.
 *
 * Item recoloring picks the shift table PER ITEM by the base item's InvTrans group
 * (the game's PALETTE_GetItemPalette): group -> palette file, and a tint is only
 * applied for groups {1,2,5,6,7,8} with a color index in [0,20]. Groups 0/3/4/>=9
 * (and a missing/absent group) produce no shift.
 */

export interface Color {
  r: number;
  g: number;
  b: number;
  a: number;
}

export class PaletteManager {
  private basePalette: Color[] = [];
  private groupMaps = new Map<number, Uint8Array>();
  private loaded = false;

  /**
   * Loads the base palette plus the per-group shift tables.
   * Each shift table is 5376 bytes = 21 shifts x 256-byte remap.
   */
  load(palData: ArrayBuffer, groupData: Map<number, ArrayBuffer>): void {
    const pal = new Uint8Array(palData);

    // Base palette (768 bytes = 256 colors * 3 bytes, stored BGR).
    this.basePalette = [];
    for (let i = 0; i < 256; i++) {
      const b = pal[i * 3];
      const g = pal[i * 3 + 1];
      const r = pal[i * 3 + 2];
      this.basePalette.push({ r, g, b, a: 255 });
    }

    this.groupMaps.clear();
    for (const [group, buffer] of groupData) {
      this.groupMaps.set(group, new Uint8Array(buffer));
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
   * Gets a color-shifted palette color.
   * @param index Palette index (0-255)
   * @param shiftColor Color shift value (itemColor; -1 = no shift)
   * @param invTrans Base item's InvTrans group (selects the shift table)
   */
  getShiftedColor(index: number, shiftColor: number, invTrans: number): Color {
    if (index < 0 || index >= 256) {
      return { r: 0, g: 0, b: 0, a: 0 };
    }

    // Only inventory-tintable groups have a loaded table; an absent key (groups
    // 0/3/4/>=9, or no invTrans) means no shift, as does a negative color. The
    // bounds check below also enforces shiftColor in [0,20].
    const map = this.groupMaps.get(invTrans);
    if (shiftColor < 0 || !map) {
      return this.basePalette[index];
    }

    const mapIndex = shiftColor * 256 + index;
    if (mapIndex < 0 || mapIndex >= map.length) {
      return this.basePalette[index];
    }

    return this.basePalette[map[mapIndex]];
  }

  /**
   * Creates a shifted palette array for a specific shift value + InvTrans group
   */
  createShiftedPalette(shiftColor: number, invTrans: number): Color[] {
    const palette: Color[] = [];
    for (let i = 0; i < 256; i++) {
      palette.push(this.getShiftedColor(i, shiftColor, invTrans));
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

// InvTrans group -> palette file (under /assets/rendering/). Only these groups are
// inventory-tintable, and the presence of a group's loaded table is what gates the
// shift — so no separate "valid groups" set is needed. Groups 3 (gold) and 4 (brown)
// aren't inventory-tintable and aren't loaded.
const GROUP_FILES = new Map<number, string>([
  [1, "grey"],
  [2, "grey2"],
  [5, "greybrown"],
  [6, "invgrey"],
  [7, "invgrey2"],
  [8, "invgreybrown"],
]);

/**
 * Loads palette data from assets (base palette + all inventory shift tables)
 */
export async function loadPaletteData(): Promise<PaletteManager> {
  const manager = getPaletteManager();

  if (manager.isLoaded()) {
    return manager;
  }

  const [palData, groupEntries] = await Promise.all([
    fetch("/assets/rendering/pal.dat").then((r) => r.arrayBuffer()),
    Promise.all(
      [...GROUP_FILES].map(async ([group, name]) => {
        const buffer = await fetch(`/assets/rendering/${name}.dat`).then((r) =>
          r.arrayBuffer(),
        );
        return [group, buffer] as [number, ArrayBuffer];
      }),
    ),
  ]);

  manager.load(palData, new Map(groupEntries));

  return manager;
}
