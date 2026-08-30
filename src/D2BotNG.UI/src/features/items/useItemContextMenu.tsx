/**
 * Right-click context menu for items.
 *
 * Wraps the generic useContextMenu hook with item-specific actions:
 * Save Image, Copy Image, Copy Description, and (optionally) Remove.
 */

import { useCallback, useMemo, useRef } from "react";
import {
  ArrowDownTrayIcon,
  PhotoIcon,
  DocumentTextIcon,
  TrashIcon,
} from "@heroicons/react/24/outline";
import type { RenderableItem } from "./item-utils";
import { useContextMenu } from "@/components/ui/ContextMenu";
import type { DropdownItem } from "@/components/ui/Dropdown";
import { toast } from "@/stores/toast-store";
import {
  copyItemDescription,
  copyItemImage,
  saveItemImage,
} from "./itemActions";

export interface UseItemContextMenuOptions {
  item: RenderableItem | null | undefined;
  /**
   * If provided, the Remove menu entry is added. The caller is responsible
   * for any confirmation dialog and the actual removal call.
   */
  onRemove?: () => void;
}

export function useItemContextMenu({
  item,
  onRemove,
}: UseItemContextMenuOptions) {
  /**
   * Whether the reader was holding Ctrl when they opened the menu — so all three actions copy or
   * save the view they were actually looking at, spans and item level included.
   *
   * Taken from the opening event rather than read live when the action runs, because by then the
   * key means nothing: the reader has moved to a menu entry and clicked it, and holding a modifier
   * through that is not part of the gesture. A ref rather than state, so recording it neither
   * re-renders the cell nor rebuilds the entries below.
   */
  const breakdown = useRef(false);

  const items: DropdownItem[] = useMemo(() => {
    if (!item) return [];

    const entries: DropdownItem[] = [
      {
        label: "Save Image",
        icon: ArrowDownTrayIcon,
        onClick: () => {
          void (async () => {
            try {
              await saveItemImage(item, breakdown.current);
            } catch (e) {
              toast.error(
                "Failed to save image",
                e instanceof Error ? e.message : String(e),
              );
            }
          })();
        },
      },
      {
        label: "Copy Image",
        icon: PhotoIcon,
        onClick: () => {
          void (async () => {
            try {
              await copyItemImage(item, breakdown.current);
              toast.success("Image copied to clipboard");
            } catch (e) {
              toast.error(
                "Failed to copy image",
                e instanceof Error ? e.message : String(e),
              );
            }
          })();
        },
      },
      {
        label: "Copy Description",
        icon: DocumentTextIcon,
        onClick: () => {
          void (async () => {
            try {
              await copyItemDescription(item, breakdown.current);
              toast.success("Description copied to clipboard");
            } catch (e) {
              toast.error(
                "Failed to copy description",
                e instanceof Error ? e.message : String(e),
              );
            }
          })();
        },
      },
    ];

    if (onRemove) {
      entries.push({
        label: "Remove",
        icon: TrashIcon,
        danger: true,
        onClick: onRemove,
      });
    }

    return entries;
  }, [item, onRemove]);

  const { contextMenu, onContextMenu: openMenu } = useContextMenu(items);

  const onContextMenu = useCallback(
    (e: React.MouseEvent) => {
      breakdown.current = e.ctrlKey;
      openMenu(e);
    },
    [openMenu],
  );

  return { contextMenu, onContextMenu };
}
