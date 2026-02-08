import { create } from "zustand";

export type ToastType = "success" | "error" | "info" | "warning";

export interface Toast {
  id: string;
  type: ToastType;
  title: string;
  message?: string;
}

interface ToastState {
  toasts: Toast[];
  addToast: (toast: Omit<Toast, "id">) => void;
  removeToast: (id: string) => void;
}

export const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  addToast: (toast) => {
    const id = Math.random().toString(36).substring(2, 9);
    set((state) => ({
      toasts: [...state.toasts, { ...toast, id }],
    }));
    // Auto-dismiss after 5 seconds
    setTimeout(() => {
      set((state) => ({
        toasts: state.toasts.filter((t) => t.id !== id),
      }));
    }, 5000);
  },
  removeToast: (id) =>
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id),
    })),
}));

// Helper functions for easy toast creation
export const toast = {
  success: (title: string, message?: string) => {
    useToastStore.getState().addToast({ type: "success", title, message });
  },
  error: (title: string, message?: string) => {
    useToastStore.getState().addToast({ type: "error", title, message });
  },
  info: (title: string, message?: string) => {
    useToastStore.getState().addToast({ type: "info", title, message });
  },
  warning: (title: string, message?: string) => {
    useToastStore.getState().addToast({ type: "warning", title, message });
  },
};
