import { Fragment, type ReactNode } from "react";
import {
  Dialog as HeadlessDialog,
  DialogPanel,
  DialogTitle,
  Transition,
  TransitionChild,
} from "@headlessui/react";
import { XMarkIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  className?: string;
}

export function Dialog({ open, onClose, children, className }: DialogProps) {
  return (
    <Transition show={open} as={Fragment}>
      <HeadlessDialog onClose={onClose} className="relative z-50">
        {/* Backdrop */}
        <TransitionChild
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-zinc-950/80" aria-hidden="true" />
        </TransitionChild>

        {/* Full-screen container to center the panel */}
        <div className="fixed inset-0 flex w-screen items-center justify-center p-4">
          <TransitionChild
            as={Fragment}
            enter="ease-out duration-300"
            enterFrom="opacity-0 scale-95"
            enterTo="opacity-100 scale-100"
            leave="ease-in duration-200"
            leaveFrom="opacity-100 scale-100"
            leaveTo="opacity-0 scale-95"
          >
            <DialogPanel
              className={clsx(
                "w-full max-w-md rounded-xl bg-zinc-900 ring-1 ring-zinc-800",
                className,
              )}
            >
              {children}
            </DialogPanel>
          </TransitionChild>
        </div>
      </HeadlessDialog>
    </Transition>
  );
}

export interface DialogHeaderProps {
  title: string;
  description?: string;
  onClose?: () => void;
  className?: string;
}

export function DialogHeader({
  title,
  description,
  onClose,
  className,
}: DialogHeaderProps) {
  return (
    <div
      className={clsx(
        "flex items-start justify-between border-b border-zinc-800 px-6 py-4",
        className,
      )}
    >
      <div>
        <DialogTitle className="text-lg font-semibold text-zinc-100">
          {title}
        </DialogTitle>
        {description && (
          <p className="mt-1 text-sm text-zinc-400">{description}</p>
        )}
      </div>
      {onClose && (
        <button
          type="button"
          onClick={onClose}
          className="ml-4 rounded-lg p-1.5 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100 transition-colors"
        >
          <span className="sr-only">Close</span>
          <XMarkIcon className="h-5 w-5" aria-hidden="true" />
        </button>
      )}
    </div>
  );
}

export interface DialogContentProps {
  className?: string;
  children: ReactNode;
}

export function DialogContent({ className, children }: DialogContentProps) {
  return <div className={clsx("p-6", className)}>{children}</div>;
}

export interface DialogFooterProps {
  className?: string;
  children: ReactNode;
}

export function DialogFooter({ className, children }: DialogFooterProps) {
  return (
    <div
      className={clsx(
        "flex items-center justify-end gap-3 border-t border-zinc-800 px-6 py-4",
        className,
      )}
    >
      {children}
    </div>
  );
}
