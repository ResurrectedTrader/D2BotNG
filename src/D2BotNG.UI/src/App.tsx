import { useState, useCallback } from "react";
import { Routes, Route, Navigate } from "react-router-dom";
import { Layout } from "./components/layout";
import { WindowControls } from "./components/layout/WindowControls";
import {
  ToastContainer,
  ErrorBoundary,
  PasswordPromptDialog,
} from "./components/ui";
import { UpdateNotification } from "./components/UpdateNotification";
import { ProfilesPage, ProfileDetailPage } from "@/features/profiles";
import { KeysPage } from "@/features/keys";
import { SchedulesPage } from "@/features/schedules";
import { CharactersPage } from "@/features/characters";
import { SettingsPage } from "@/features/settings";
import { useEventStream } from "@/hooks/useEventStream";
import { useAuthStore } from "@/lib/auth";

export default function App() {
  // Start the event stream at app root
  useEventStream();

  // Auth state
  const { authRequired, setPassword } = useAuthStore();
  const [authError, setAuthError] = useState<string>();

  const handlePasswordSubmit = useCallback(
    (password: string) => {
      setAuthError(undefined);
      setPassword(password);
      // The next request will use this password.
      // If it fails, authRequired will be set again and we'll show an error.
      // We need to reload the page to retry all requests with the new password.
      window.location.reload();
    },
    [setPassword],
  );

  return (
    <ErrorBoundary>
      {/* Phantom titlebar for WebView - doesn't shift content */}
      <WindowControls />

      <Routes>
        <Route path="/" element={<Layout />}>
          {/* Redirect root to profiles */}
          <Route index element={<Navigate to="/profiles" replace />} />

          {/* Profile routes */}
          <Route path="profiles">
            <Route index element={<ProfilesPage />} />
            <Route path="new" element={<ProfileDetailPage />} />
            <Route path=":id" element={<ProfileDetailPage />} />
          </Route>

          {/* Other main routes */}
          <Route path="keys" element={<KeysPage />} />
          <Route path="schedules" element={<SchedulesPage />} />
          <Route path="characters" element={<CharactersPage />} />
          <Route path="settings" element={<SettingsPage />} />
        </Route>
      </Routes>

      {/* Global components */}
      <UpdateNotification />
      <ToastContainer />

      {/* Password prompt when auth is required */}
      <PasswordPromptDialog
        open={authRequired}
        error={authError}
        onSubmit={handlePasswordSubmit}
      />
    </ErrorBoundary>
  );
}
