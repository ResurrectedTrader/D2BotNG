/**
 * Authentication state management for remote server access.
 *
 * When a server has password protection enabled, this module handles:
 * - Storing the password (persisted in sessionStorage)
 * - Detecting auth failures
 * - Triggering the password prompt
 */

import { create } from "zustand";

const AUTH_PASSWORD_KEY = "d2bot-auth-password";

/** Get password from sessionStorage */
function getStoredPassword(): string | null {
  if (typeof window === "undefined") return null;
  return sessionStorage.getItem(AUTH_PASSWORD_KEY);
}

/** Store password in sessionStorage */
function storePassword(password: string | null): void {
  if (typeof window === "undefined") return;
  if (password) {
    sessionStorage.setItem(AUTH_PASSWORD_KEY, password);
  } else {
    sessionStorage.removeItem(AUTH_PASSWORD_KEY);
  }
}

interface AuthState {
  /** The password for authenticated requests */
  password: string | null;
  /** Whether an auth prompt is needed */
  authRequired: boolean;
  /** Set the password */
  setPassword: (password: string | null) => void;
  /** Mark that auth is required (triggers prompt) */
  requireAuth: () => void;
  /** Clear the auth required flag */
  clearAuthRequired: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  password: getStoredPassword(),
  authRequired: false,
  setPassword: (password) => {
    storePassword(password);
    set({ password, authRequired: false });
  },
  requireAuth: () => set({ authRequired: true }),
  clearAuthRequired: () => set({ authRequired: false }),
}));

/** Get current password (for use outside React components) */
export function getAuthPassword(): string | null {
  return useAuthStore.getState().password;
}

/** Trigger auth required state (for use outside React components) */
export function triggerAuthRequired(): void {
  useAuthStore.getState().requireAuth();
}
