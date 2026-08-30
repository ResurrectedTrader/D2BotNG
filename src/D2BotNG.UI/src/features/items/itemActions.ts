/**
 * Clipboard and download actions for items.
 *
 * Mirrors the legacy D2Bot context-menu actions: Copy Image, Save Image,
 * Copy Description. All rendering happens client-side; the same DOM tooltip
 * the user hovers becomes the captured PNG.
 */

import { stripD2ColorCodes, type RenderableItem } from "./item-utils";
import { captureItemTooltipBlob } from "./captureItemImage";

/** Sanitize an item name for use as a download filename. */
function sanitizeFilename(name: string): string {
  // Strip path separators, control chars, reserved Windows chars; collapse
  // whitespace; strip leading dots and trailing dots/whitespace (Windows
  // refuses filenames that end with `.` or ` `).
  const cleaned = name
    .replace(/[\\/:*?"<>|\x00-\x1f]/g, "")
    .replace(/\s+/g, " ")
    .replace(/^\.+/, "")
    .replace(/[.\s]+$/, "")
    .trim();
  return cleaned.length > 0 ? cleaned : "item";
}

/**
 * Strip color codes and the trailing "$..." metadata from the item description.
 *
 * `breakdown` copies the held-Ctrl view instead — the roll spans, the item level, the socket
 * fillers as their own blocks. It is a separate source rather than a switch inside one, because the
 * two are produced differently: the description is one string to be split back into lines, while
 * the breakdown is already rows, each carrying its own colour runs. Joining those runs is all that
 * is needed here; there is no "$" metadata and no escaped newline in them to undo.
 */
export function getCleanItemDescription(
  item: RenderableItem,
  breakdown = false,
): string {
  if (breakdown && item.detail) {
    return item.detail
      .lines()
      .map((line) => line.segments.map((s) => s.text).join(""))
      .join("\n")
      .trim();
  }

  // Rendered on demand for a source that carries no text of its own, exactly as the tooltip does —
  // otherwise copying a captured item would put an empty string on the clipboard.
  const raw = item.describe?.() ?? item.description ?? "";
  const beforeMeta = raw.split("$")[0];
  // Lines may use literal "\n" (escape sequence in JSON) or real newlines.
  const normalized = beforeMeta.includes(String.raw`\n`)
    ? beforeMeta.replace(/\\n/g, "\n")
    : beforeMeta;
  return normalized
    .split("\n")
    .map((line) => stripD2ColorCodes(line))
    .join("\n")
    .trim();
}

export async function copyItemDescription(
  item: RenderableItem,
  breakdown = false,
): Promise<void> {
  const text = getCleanItemDescription(item, breakdown);
  // The breakdown opens with the item's own name line, so prefixing it would print the name twice
  // — the same reason the tooltip drops its title when either full view is showing.
  const lines = item.name && !breakdown ? `${item.name}\n${text}` : text;
  await navigator.clipboard.writeText(lines);
}

export async function copyItemImage(
  item: RenderableItem,
  breakdown = false,
): Promise<void> {
  const blob = await captureItemTooltipBlob(item, breakdown);
  if (typeof ClipboardItem === "undefined") {
    throw new Error("Clipboard image API not supported in this browser");
  }
  await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]);
}

/** Minimal shape of the File System Access API we use. */
interface SaveFilePickerWindow {
  showSaveFilePicker?: (options?: {
    suggestedName?: string;
    types?: { description?: string; accept: Record<string, string[]> }[];
  }) => Promise<{
    createWritable: () => Promise<{
      write: (data: Blob) => Promise<void>;
      close: () => Promise<void>;
    }>;
  }>;
}

export async function saveItemImage(
  item: RenderableItem,
  breakdown = false,
): Promise<void> {
  const blob = await captureItemTooltipBlob(item, breakdown);
  const filename = `${sanitizeFilename(item.name)}.png`;

  // Prefer the native Save As dialog where supported (Chromium / WebView2).
  // Falls back to a programmatic anchor download on Safari/Firefox.
  const picker = (window as unknown as SaveFilePickerWindow).showSaveFilePicker;
  if (picker) {
    try {
      const handle = await picker({
        suggestedName: filename,
        types: [
          { description: "PNG image", accept: { "image/png": [".png"] } },
        ],
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return;
    } catch (e) {
      // User cancelled the picker — nothing to report or fall back to.
      if (e instanceof DOMException && e.name === "AbortError") return;
      // Any other error: fall through to the anchor fallback so the user
      // still ends up with the file.
    }
  }

  const url = URL.createObjectURL(blob);
  try {
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    // Revoke after the click handler has had a chance to start the download.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
