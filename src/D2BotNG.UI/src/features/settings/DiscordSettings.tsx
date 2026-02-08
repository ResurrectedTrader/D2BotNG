/**
 * DiscordSettings component
 *
 * Card for configuring Discord integration.
 */

import { Card, CardHeader, CardContent, Input, Button } from "@/components/ui";
import { useTestDiscord } from "@/hooks";
import { ArrowPathIcon } from "@heroicons/react/24/outline";
import type { DiscordSettings as DiscordSettingsType } from "@/generated/settings_pb";

export interface DiscordValidationErrors {
  token?: string;
  serverId?: string;
}

interface DiscordSettingsProps {
  /** Current discord settings */
  discord?: Partial<DiscordSettingsType>;
  /** Callback when a field changes */
  onChange: (discord: Partial<DiscordSettingsType>) => void;
  /** Validation errors to display */
  errors?: DiscordValidationErrors;
}

export function DiscordSettings({
  discord,
  onChange,
  errors,
}: DiscordSettingsProps) {
  const testDiscord = useTestDiscord();

  const canTest =
    discord?.enabled && discord?.token?.trim() && discord?.serverId?.trim();

  const handleTest = () => {
    if (canTest) {
      testDiscord.mutate({
        token: discord.token!,
        serverId: discord.serverId!,
      });
    }
  };

  return (
    <Card>
      <CardHeader
        title="Discord Integration"
        description="Configure Discord bot notifications and commands."
      />
      <CardContent>
        <div className="space-y-3">
          {/* Enable toggle */}
          <label className="flex cursor-pointer items-center gap-3">
            <input
              type="checkbox"
              checked={discord?.enabled || false}
              onChange={(e) => onChange({ enabled: e.target.checked })}
              className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
            />
            <span className="text-sm text-zinc-300">
              Enable Discord Integration
            </span>
          </label>

          {/* Discord settings grid */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Input
              id="discord-token"
              label="Bot Token"
              tooltip="Bot token from the Discord Developer Portal."
              type="password"
              placeholder="Enter Discord bot token"
              value={discord?.token || ""}
              onChange={(e) => onChange({ token: e.target.value })}
              disabled={!discord?.enabled}
              error={errors?.token}
            />

            <Input
              id="discord-server"
              label="Server ID"
              tooltip="Right-click your server name in Discord and Copy Server ID (requires Developer Mode)."
              placeholder="Enter Discord server ID"
              value={discord?.serverId || ""}
              onChange={(e) => onChange({ serverId: e.target.value })}
              disabled={!discord?.enabled}
              error={errors?.serverId}
            />

            {/* Test button */}
            <div className="flex items-end">
              <Button
                variant="secondary"
                onClick={handleTest}
                disabled={!canTest || testDiscord.isPending}
              >
                {testDiscord.isPending ? (
                  <ArrowPathIcon className="h-4 w-4 animate-spin" />
                ) : null}
                Test Connection
              </Button>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
