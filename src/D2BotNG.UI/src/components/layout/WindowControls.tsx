import { useState, useEffect, useCallback, useRef } from "react";
import { MinusIcon, StopIcon, XMarkIcon } from "@heroicons/react/24/outline";
import { Square2StackIcon } from "@heroicons/react/24/outline";
import { ClosePromptDialog, type CloseChoice } from "@/components/ui";
import { useSettings } from "@/stores/event-store";
import { useUpdateSettings } from "@/hooks/useSettings";
import { CloseAction } from "@/generated/settings_pb";

const TITLEBAR_HEIGHT = 32;
const RESIZE_BORDER = 6;
/** Windows' default double-click time and drag rectangle (GetDoubleClickTime / SM_CXDOUBLECLK). */
const DOUBLE_CLICK_MS = 500;
const DOUBLE_CLICK_SLOP = 4;

function isWebView(): boolean {
  return !!(window as any).chrome?.webview;
}

function postMessage(action: string, data?: Record<string, string>) {
  const webview = (window as any).chrome?.webview;
  if (webview) {
    webview.postMessage(JSON.stringify({ action, ...data }));
  }
}

type ResizeEdge =
  | "left"
  | "right"
  | "top"
  | "bottom"
  | "topLeft"
  | "topRight"
  | "bottomLeft"
  | "bottomRight";

const cursorMap: Record<ResizeEdge, string> = {
  left: "ew-resize",
  right: "ew-resize",
  top: "ns-resize",
  bottom: "ns-resize",
  topLeft: "nwse-resize",
  topRight: "nesw-resize",
  bottomLeft: "nesw-resize",
  bottomRight: "nwse-resize",
};

export function WindowControls() {
  const [isMaximized, setIsMaximized] = useState(false);
  const [inWebView, setInWebView] = useState(false);
  const [showCloseDialog, setShowCloseDialog] = useState(false);
  const [pendingChoice, setPendingChoice] = useState<CloseChoice | null>(null);

  const settings = useSettings();
  const updateSettings = useUpdateSettings();

  useEffect(() => {
    setInWebView(isWebView());

    // Listen for window state changes from C#
    const d2bot = (window as any).d2bot || {};
    d2bot.onWindowStateChanged = (state: { isMaximized: boolean }) => {
      setIsMaximized(state.isMaximized);
    };
    (window as any).d2bot = d2bot;

    // Listen for close dialog event from C# (taskbar close with Ask setting)
    const handleShowCloseDialog = () => {
      setShowCloseDialog(true);
    };
    window.addEventListener("d2bot-show-close-dialog", handleShowCloseDialog);

    return () => {
      if ((window as any).d2bot) {
        delete (window as any).d2bot.onWindowStateChanged;
      }
      window.removeEventListener(
        "d2bot-show-close-dialog",
        handleShowCloseDialog,
      );
    };
  }, []);

  const handleMinimize = useCallback(() => {
    postMessage("minimize");
  }, []);

  const handleMaximize = useCallback(() => {
    postMessage("maximize");
  }, []);

  const executeClose = useCallback((choice: CloseChoice) => {
    if (choice === "minimize") {
      postMessage("minimizeToTray");
    } else {
      postMessage("forceClose");
    }
  }, []);

  const handleClose = useCallback(() => {
    const closeAction = settings?.closeAction ?? CloseAction.ASK;

    if (closeAction === CloseAction.ASK) {
      setShowCloseDialog(true);
    } else if (closeAction === CloseAction.MINIMIZE_TO_TRAY) {
      executeClose("minimize");
    } else {
      executeClose("exit");
    }
  }, [settings?.closeAction, executeClose]);

  const handleCloseChoice = useCallback(
    async (choice: CloseChoice, remember: boolean) => {
      if (remember && settings) {
        setPendingChoice(choice);
        const newCloseAction =
          choice === "minimize"
            ? CloseAction.MINIMIZE_TO_TRAY
            : CloseAction.EXIT;

        updateSettings.mutate(
          { ...settings, closeAction: newCloseAction },
          {
            onSuccess: () => {
              setShowCloseDialog(false);
              setPendingChoice(null);
              executeClose(choice);
            },
            onError: () => {
              setPendingChoice(null);
            },
          },
        );
      } else {
        setShowCloseDialog(false);
        executeClose(choice);
      }
    },
    [settings, updateSettings, executeClose],
  );

  const handleCloseCancel = useCallback(() => {
    setShowCloseDialog(false);
    setPendingChoice(null);
  }, []);

  // Every press hands the mouse to the native move loop, which swallows the mouseup, so neither
  // Chromium's dblclick nor the OS's caption double-click ever fires. Pair the presses ourselves.
  const isMaximizedRef = useRef(isMaximized);
  isMaximizedRef.current = isMaximized;
  const lastPress = useRef<{ time: number; x: number; y: number } | null>(null);

  const handleTitlebarMouseDown = useCallback((e: React.MouseEvent) => {
    if (e.button !== 0 || !(e.target as HTMLElement).closest(".drag-region")) {
      return;
    }
    // A second press here is a browser double-click too, whose default selects the page's text.
    e.preventDefault();
    const previous = lastPress.current;
    const isDouble =
      previous !== null &&
      e.timeStamp - previous.time < DOUBLE_CLICK_MS &&
      Math.abs(e.screenX - previous.x) <= DOUBLE_CLICK_SLOP &&
      Math.abs(e.screenY - previous.y) <= DOUBLE_CLICK_SLOP;
    if (isDouble) {
      lastPress.current = null;
      postMessage("maximize");
      return;
    }
    lastPress.current = { time: e.timeStamp, x: e.screenX, y: e.screenY };
    if (!isMaximizedRef.current) {
      postMessage("dragStart");
      return;
    }
    // Dragging a maximized window restores it first, so wait until the mouse really moves: a
    // press that is the first half of a double-click must leave it maximized.
    const startX = e.screenX;
    const startY = e.screenY;
    const onMove = (move: MouseEvent) => {
      if (
        Math.abs(move.screenX - startX) > DOUBLE_CLICK_SLOP ||
        Math.abs(move.screenY - startY) > DOUBLE_CLICK_SLOP
      ) {
        stop();
        postMessage("dragStart");
      }
    };
    const stop = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", stop);
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", stop);
  }, []);

  const handleResizeMouseDown = useCallback(
    (edge: ResizeEdge) => (e: React.MouseEvent) => {
      if (e.button === 0) {
        e.preventDefault();
        postMessage("resizeStart", { edge });
      }
    },
    [],
  );

  if (!inWebView) {
    return null;
  }

  return (
    <>
      {/* Resize handles - only show when not maximized */}
      {!isMaximized && (
        <>
          {/* Edges */}
          <div
            className="fixed top-0 left-0 right-0 z-[10000]"
            style={{ height: RESIZE_BORDER, cursor: cursorMap.top }}
            onMouseDown={handleResizeMouseDown("top")}
          />
          <div
            className="fixed bottom-0 left-0 right-0 z-[10000]"
            style={{ height: RESIZE_BORDER, cursor: cursorMap.bottom }}
            onMouseDown={handleResizeMouseDown("bottom")}
          />
          <div
            className="fixed top-0 bottom-0 left-0 z-[10000]"
            style={{ width: RESIZE_BORDER, cursor: cursorMap.left }}
            onMouseDown={handleResizeMouseDown("left")}
          />
          <div
            className="fixed top-0 bottom-0 right-0 z-[10000]"
            style={{ width: RESIZE_BORDER, cursor: cursorMap.right }}
            onMouseDown={handleResizeMouseDown("right")}
          />

          {/* Corners */}
          <div
            className="fixed top-0 left-0 z-[10001]"
            style={{
              width: RESIZE_BORDER * 2,
              height: RESIZE_BORDER * 2,
              cursor: cursorMap.topLeft,
            }}
            onMouseDown={handleResizeMouseDown("topLeft")}
          />
          <div
            className="fixed top-0 right-0 z-[10001]"
            style={{
              width: RESIZE_BORDER * 2,
              height: RESIZE_BORDER * 2,
              cursor: cursorMap.topRight,
            }}
            onMouseDown={handleResizeMouseDown("topRight")}
          />
          <div
            className="fixed bottom-0 left-0 z-[10001]"
            style={{
              width: RESIZE_BORDER * 2,
              height: RESIZE_BORDER * 2,
              cursor: cursorMap.bottomLeft,
            }}
            onMouseDown={handleResizeMouseDown("bottomLeft")}
          />
          <div
            className="fixed bottom-0 right-0 z-[10001]"
            style={{
              width: RESIZE_BORDER * 2,
              height: RESIZE_BORDER * 2,
              cursor: cursorMap.bottomRight,
            }}
            onMouseDown={handleResizeMouseDown("bottomRight")}
          />
        </>
      )}

      {/* Titlebar with drag region and buttons */}
      <div
        className="fixed top-0 left-0 right-0 z-[9999] flex items-center justify-end"
        style={{ height: TITLEBAR_HEIGHT }}
      >
        {/* Draggable region - covers the whole titlebar except buttons */}
        <div
          className="drag-region absolute inset-0 select-none"
          onMouseDown={handleTitlebarMouseDown}
          style={{ cursor: "grab" }}
        />

        {/* Window control buttons - bg only when sidebar visible (lg+) */}
        <div className="relative z-10 flex items-center lg:bg-black">
          <button
            onClick={handleMinimize}
            className="flex h-8 w-12 items-center justify-center text-zinc-400 hover:bg-zinc-700 hover:text-zinc-100 transition-colors"
            title="Minimize"
          >
            <MinusIcon className="h-4 w-4" />
          </button>

          <button
            onClick={handleMaximize}
            className="flex h-8 w-12 items-center justify-center text-zinc-400 hover:bg-zinc-700 hover:text-zinc-100 transition-colors"
            title={isMaximized ? "Restore" : "Maximize"}
          >
            {isMaximized ? (
              <Square2StackIcon className="h-4 w-4" />
            ) : (
              <StopIcon className="h-4 w-4" />
            )}
          </button>

          <button
            onClick={handleClose}
            className="flex h-8 w-12 items-center justify-center text-zinc-400 hover:bg-red-600 hover:text-white transition-colors"
            title="Close"
          >
            <XMarkIcon className="h-4 w-4" />
          </button>
        </div>
      </div>

      {/* Close prompt dialog */}
      <ClosePromptDialog
        open={showCloseDialog}
        isPending={pendingChoice !== null}
        onChoice={handleCloseChoice}
        onCancel={handleCloseCancel}
      />
    </>
  );
}
