import { Fragment } from "react";
import { Transition } from "@headlessui/react";
import {
  CheckCircleIcon,
  ExclamationCircleIcon,
  ExclamationTriangleIcon,
  InformationCircleIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import clsx from "clsx";
import { useToastStore, type ToastType } from "../../stores/toast-store";

const icons: Record<ToastType, typeof CheckCircleIcon> = {
  success: CheckCircleIcon,
  error: ExclamationCircleIcon,
  warning: ExclamationTriangleIcon,
  info: InformationCircleIcon,
};

const iconColors: Record<ToastType, string> = {
  success: "text-green-400",
  error: "text-red-400",
  warning: "text-yellow-400",
  info: "text-blue-400",
};

const ringColors: Record<ToastType, string> = {
  success: "ring-green-500/20",
  error: "ring-red-500/20",
  warning: "ring-yellow-500/20",
  info: "ring-blue-500/20",
};

export function ToastContainer() {
  const { toasts, removeToast } = useToastStore();

  return (
    <div
      aria-live="assertive"
      className="pointer-events-none fixed bottom-0 right-0 z-50 flex flex-col items-end gap-3 p-4"
    >
      {toasts.map((toast) => {
        const Icon = icons[toast.type];
        return (
          <Transition
            key={toast.id}
            show={true}
            as={Fragment}
            enter="transform ease-out duration-300 transition"
            enterFrom="translate-y-2 opacity-0 translate-x-2"
            enterTo="translate-y-0 opacity-100 translate-x-0"
            leave="transition ease-in duration-100"
            leaveFrom="opacity-100"
            leaveTo="opacity-0"
          >
            <div
              className={clsx(
                "pointer-events-auto w-full max-w-sm overflow-hidden rounded-lg",
                "bg-zinc-900 ring-1 shadow-lg",
                ringColors[toast.type],
              )}
            >
              <div className="p-4">
                <div className="flex items-start gap-3">
                  <Icon
                    className={clsx(
                      "h-5 w-5 flex-shrink-0",
                      iconColors[toast.type],
                    )}
                    aria-hidden="true"
                  />
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-semibold text-zinc-100">
                      {toast.title}
                    </p>
                    {toast.message && (
                      <p className="mt-1 text-sm text-zinc-400">
                        {toast.message}
                      </p>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => removeToast(toast.id)}
                    className="flex-shrink-0 rounded-lg p-1 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100 transition-colors"
                  >
                    <span className="sr-only">Close</span>
                    <XMarkIcon className="h-4 w-4" aria-hidden="true" />
                  </button>
                </div>
              </div>
            </div>
          </Transition>
        );
      })}
    </div>
  );
}
