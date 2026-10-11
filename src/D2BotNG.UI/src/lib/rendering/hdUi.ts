/**
 * D2R's own panel art — the inventory, stash and mercenary panels, slot silhouettes, tabs, arrows
 * and a grid cell — and the layout that places things on it.
 *
 * Generated with the item art by `scripts/generate-hd-assets.mjs` (`hdui.json` + `hdui{n}.pngx`),
 * already scaled to the items' 30 pixels per cell, so an item drawn at a slot's rect lands in the
 * panel's own frame. Kept in archives of its own so drawing an item never fetches a panel.
 *
 * Unlike item art this is never recoloured, so a sprite is handed to the browser as the PNG it
 * is: a slice of an archive behind a blob URL, decoded by an `<img>`.
 */

import { useEffect, useState } from "react";

/** `[x, y, width, height]`, in panel pixels. */
export type UiRect = [number, number, number, number];

export interface UiGrid {
  /** The top-left of the grid's first cell. */
  at: [number, number];
  cols: number;
  rows: number;
}

export interface UiSlot {
  rect: UiRect;
  /** The silhouette drawn while the slot is empty. */
  empty?: string;
}

/** A page-arrow button: its rect, and which frame of its sprite each state draws. */
export interface UiArrow {
  rect: UiRect;
  frames: {
    normal: number;
    hovered: number;
    pressed: number;
    disabled: number;
  };
}

export interface UiPanels {
  inventory: {
    background: string;
    size: [number, number];
    grid: UiGrid;
    gold: UiRect;
    /** Where the gold button sits, left of the amount. */
    goldButton: [number, number];
    /** Keyed by body location (1 head .. 10 gloves). */
    slots: Record<string, UiSlot>;
    weaponTabs: {
      /** Frame per selected set: `[set I, set II]`. */
      sprites: [string, string];
      size: [number, number];
      /** Over the right hand, then the left. */
      at: [[number, number], [number, number]];
      /** The "I" and "II" labels, relative to the panel for the first tab. */
      labels: [UiRect, UiRect];
    };
  };
  merc: {
    background: string;
    size: [number, number];
    slots: Record<string, UiSlot>;
  };
  stash: {
    size: [number, number];
    backgrounds: { personal: string; shared: string };
    grid: UiGrid;
    gold: UiRect;
    goldButton: [number, number];
    tabs: {
      at: [number, number];
      size: [number, number];
      /** `[inactive, active]`. */
      sprites: [string, string];
      /** Personal, Shared, Gems, Materials, Runes — the game's own wording. */
      labels: string[];
    };
    pages: { left: UiArrow; right: UiArrow; text: UiRect; format: string };
    advanced: {
      gems: { background: string; slots: Record<string, UiRect> };
      materials: { background: string; slots: Record<string, UiRect> };
      runes: { background: string; slots: Record<string, UiRect> };
      /** Where RotW draws the Horadric Cube on every advanced page. */
      cube: UiGrid;
    };
    /** The pre-RotW bar: a tab per page (personal, then shared ones). */
    pageTabs: {
      at: [number, number];
      size: [number, number];
      /** `[inactive, active]`. */
      sprites: [string, string];
      count: number;
    };
  };
  /** The Horadric Cube's own panel, for a game that does not keep it in the stash. */
  cube: {
    background: string;
    size: [number, number];
    grid: UiGrid;
  };
}

interface UiIndex {
  cell: number;
  archives: number;
  panels: UiPanels;
  sprites: Record<string, [number, number, number]>;
}

const BASE = "/assets/rendering/hd";

let index: Promise<UiIndex> | null = null;
const archives = new Map<number, Promise<Uint8Array>>();
const urls = new Map<string, Promise<string | null>>();

function loadIndex(): Promise<UiIndex> {
  index ??= fetch(`${BASE}/hdui.json`).then((r) => {
    if (!r.ok) throw new Error(`hdui.json: ${r.status}`);
    return r.json();
  });
  return index;
}

function loadArchive(n: number): Promise<Uint8Array> {
  let pending = archives.get(n);
  if (!pending) {
    pending = fetch(`${BASE}/hdui${n}.pngx`)
      .then((r) => r.arrayBuffer())
      .then((b) => new Uint8Array(b));
    archives.set(n, pending);
  }
  return pending;
}

/** A blob URL for one UI sprite, or null when the art has none by that name. Cached for good. */
export function uiSpriteUrl(name: string): Promise<string | null> {
  let pending = urls.get(name);
  if (!pending) {
    pending = loadIndex().then(async (ui) => {
      const entry = ui.sprites[name];
      if (!entry) return null;
      const [archive, offset, length] = entry;
      const bytes = await loadArchive(archive);
      // `slice()`: the Blob must own its bytes, not alias the whole archive.
      return URL.createObjectURL(
        new Blob([bytes.slice(offset, offset + length)], { type: "image/png" }),
      );
    });
    urls.set(name, pending);
  }
  return pending;
}

/**
 * A panel `scripts/compose-lod-panels.mjs` composes from the game's art: a loose PNG beside the
 * archives rather than a sprite in them, since no layout file describes it.
 */
export function composedPanelUrl(file: string): string {
  return `${BASE}/${file}`;
}

/** The panel layouts, once loaded; null while loading or when the art is unavailable. */
export function useUiPanels(): UiPanels | null {
  const [panels, setPanels] = useState<UiPanels | null>(null);
  useEffect(() => {
    let live = true;
    loadIndex()
      .then((ui) => live && setPanels(ui.panels))
      .catch(() => live && setPanels(null));
    return () => {
      live = false;
    };
  }, []);
  return panels;
}

/** One UI sprite's URL, once it is ready. */
export function useUiSprite(name: string | undefined): string | null {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    if (!name) {
      setUrl(null);
      return;
    }
    uiSpriteUrl(name).then((u) => live && setUrl(u));
    return () => {
      live = false;
    };
  }, [name]);
  return url;
}
