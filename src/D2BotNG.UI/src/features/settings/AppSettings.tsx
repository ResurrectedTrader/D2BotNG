/**
 * DevSettings component
 *
 * Development-only settings for backend URL configuration.
 */

import { useState } from "react";
import { Card, CardHeader, CardContent, Input } from "@/components/ui";
import { getDevBackendUrl, setDevBackendUrl } from "@/lib/grpc-client";

/**
 * DevSettings component - standalone dev settings that render independently.
 * Only visible in dev mode. Renders even when backend is unreachable.
 */
export function DevSettings() {
  const [devBackendUrl, setDevBackendUrlLocal] = useState(getDevBackendUrl);
  const [urlSaved, setUrlSaved] = useState(false);

  if (!import.meta.env.DEV) return null;

  const handleBackendUrlChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setDevBackendUrlLocal(e.target.value);
    setUrlSaved(false);
  };

  const handleBackendUrlBlur = () => {
    setDevBackendUrl(devBackendUrl);
    setUrlSaved(true);
  };

  return (
    <Card>
      <CardHeader
        title="Developer Settings"
        description="Development-only settings. Not visible in production."
      />
      <CardContent>
        <Input
          id="dev-backend-url"
          label="Backend URL"
          value={devBackendUrl}
          onChange={handleBackendUrlChange}
          onBlur={handleBackendUrlBlur}
          placeholder="http://localhost:5000"
        />
        {urlSaved && (
          <p className="mt-1.5 text-xs text-zinc-400">
            Saved. Reload the page for changes to take effect.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
