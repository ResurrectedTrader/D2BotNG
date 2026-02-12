/**
 * Update Notification Component
 *
 * Fixed bottom-right notification showing update status:
 * - Update available banner
 * - Version info (current vs latest)
 * - Download progress bar
 * - Install button when ready
 */

import { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { Transition } from "@headlessui/react";
import {
  ArrowDownTrayIcon,
  ArrowPathIcon,
  ExclamationTriangleIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import clsx from "clsx";
import { Button } from "./ui/Button";
import {
  useStartUpdate,
  useUpdateVisibility,
  getUpdateStateLabel,
  UpdateState,
} from "@/hooks/useUpdates";
import { useUpdateStatus } from "@/stores/event-store";

export function UpdateNotification() {
  const status = useUpdateStatus();
  const startUpdate = useStartUpdate();
  const isVisible = useUpdateVisibility(status);
  const [dismissed, setDismissed] = useState(false);
  const prevStateRef = useRef(status?.state);

  // Reset dismissed when update state changes (e.g. new update available)
  useEffect(() => {
    if (status?.state !== prevStateRef.current) {
      prevStateRef.current = status?.state;
      setDismissed(false);
    }
  }, [status?.state]);

  const handleDismiss = useCallback(() => {
    setDismissed(true);
  }, []);

  if (!status) return null;

  const isDownloading = status.state === UpdateState.DOWNLOADING;
  const isReadyToInstall = status.state === UpdateState.READY_TO_INSTALL;
  const isInstalling = status.state === UpdateState.INSTALLING;
  const isError = status.state === UpdateState.ERROR;
  const isUpdateAvailable = status.state === UpdateState.UPDATE_AVAILABLE;

  // Don't allow dismissing while actively downloading or installing
  const canDismiss = !isDownloading && !isInstalling;

  const handleStartUpdate = () => {
    startUpdate.mutate();
  };

  return (
    <div className="pointer-events-none fixed bottom-0 right-0 z-50 p-4">
      <Transition
        show={isVisible && !dismissed}
        as={Fragment}
        enter="transform ease-out duration-300 transition"
        enterFrom="translate-y-2 opacity-0 translate-x-2"
        enterTo="translate-y-0 opacity-100 translate-x-0"
        leave="transition ease-in duration-200"
        leaveFrom="opacity-100"
        leaveTo="opacity-0"
      >
        <div
          className={clsx(
            "pointer-events-auto w-80 overflow-hidden rounded-lg shadow-lg",
            "bg-zinc-900 ring-1",
            isError ? "ring-red-500/30" : "ring-d2-gold/30",
          )}
        >
          {/* Header */}
          <div
            className={clsx(
              "flex items-center gap-3 px-4 py-3",
              isError ? "bg-red-500/10" : "bg-d2-gold/10",
            )}
          >
            {isError ? (
              <ExclamationTriangleIcon className="h-5 w-5 text-red-400" />
            ) : isDownloading || isInstalling ? (
              <ArrowPathIcon className="h-5 w-5 animate-spin text-d2-gold" />
            ) : (
              <ArrowDownTrayIcon className="h-5 w-5 text-d2-gold" />
            )}
            <span
              className={clsx(
                "flex-1 text-sm font-semibold",
                isError ? "text-red-400" : "text-d2-gold",
              )}
            >
              {getUpdateStateLabel(status.state)}
            </span>
            {canDismiss && (
              <button
                type="button"
                onClick={handleDismiss}
                className="rounded-lg p-1 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100 transition-colors"
              >
                <span className="sr-only">Dismiss</span>
                <XMarkIcon className="h-4 w-4" aria-hidden="true" />
              </button>
            )}
          </div>

          {/* Content */}
          <div className="px-4 py-3">
            {/* Version info */}
            <div className="mb-3 space-y-1 text-sm">
              <div className="flex justify-between">
                <span className="text-zinc-400">Current:</span>
                <span className="text-zinc-200">{status.currentVersion}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-zinc-400">Latest:</span>
                <span className="text-d2-gold">{status.latestVersion}</span>
              </div>
            </div>

            {/* Progress bar during download */}
            {isDownloading && (
              <div className="mb-3">
                <div className="mb-1 flex justify-between text-xs">
                  <span className="text-zinc-400">Downloading...</span>
                  <span className="text-zinc-200">
                    {status.downloadProgress}%
                  </span>
                </div>
                <div className="h-2 w-full overflow-hidden rounded-full bg-zinc-800">
                  <div
                    className="h-full rounded-full bg-d2-gold transition-all duration-300"
                    style={{ width: `${status.downloadProgress}%` }}
                  />
                </div>
              </div>
            )}

            {/* Error message */}
            {isError && status.errorMessage && (
              <p className="mb-3 text-sm text-red-400">{status.errorMessage}</p>
            )}

            {/* Action buttons */}
            <div className="flex gap-2">
              {isUpdateAvailable && (
                <Button
                  variant="primary"
                  size="sm"
                  className="flex-1"
                  onClick={handleStartUpdate}
                  disabled={startUpdate.isPending}
                >
                  Download Update
                </Button>
              )}

              {isReadyToInstall && (
                <Button
                  variant="primary"
                  size="sm"
                  className="flex-1"
                  onClick={handleStartUpdate}
                  disabled={startUpdate.isPending}
                >
                  Install & Restart
                </Button>
              )}

              {isError && (
                <Button
                  variant="secondary"
                  size="sm"
                  className="flex-1"
                  onClick={handleStartUpdate}
                  disabled={startUpdate.isPending}
                >
                  Retry
                </Button>
              )}

              {(isDownloading || isInstalling) && (
                <p className="flex-1 text-center text-xs text-zinc-400">
                  Please wait...
                </p>
              )}
            </div>
          </div>
        </div>
      </Transition>
    </div>
  );
}
