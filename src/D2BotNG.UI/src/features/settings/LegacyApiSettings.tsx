/**
 * LegacyApiSettings component
 *
 * Card for configuring Legacy API access, profiles, and users.
 */

import { useCallback, useMemo } from "react";
import {
  Card,
  CardHeader,
  CardContent,
  Input,
  Select,
  Button,
} from "@/components/ui";
import { create } from "@bufbuild/protobuf";
import {
  LegacyApiPermissions,
  LegacyApiUserSchema,
  type LegacyApiSettings as LegacyApiSettingsType,
  type LegacyApiUser,
} from "@/generated/settings_pb";
import { useProfiles } from "@/stores/event-store";
import { PlusIcon, TrashIcon, KeyIcon } from "@heroicons/react/24/outline";

interface LegacyApiSettingsProps {
  legacyApi?: LegacyApiSettingsType;
  onChange: (updates: Partial<LegacyApiSettingsType>) => void;
}

const permissionOptions = [
  {
    value: LegacyApiPermissions.PUBLIC.toString(),
    label: "Public",
  },
  {
    value: LegacyApiPermissions.ADMIN.toString(),
    label: "Admin",
  },
];

function generateApiKey(): string {
  const chars =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  let result = "";
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  for (const byte of array) {
    result += chars[byte % chars.length];
  }
  return result;
}

export function LegacyApiSettings({
  legacyApi,
  onChange,
}: LegacyApiSettingsProps) {
  const allProfiles = useProfiles();
  const users = useMemo(() => legacyApi?.users ?? [], [legacyApi?.users]);
  const profiles = useMemo(
    () => legacyApi?.profiles ?? [],
    [legacyApi?.profiles],
  );

  const handleAddUser = useCallback(() => {
    onChange({
      users: [
        ...users,
        create(LegacyApiUserSchema, {
          name: "",
          apiKey: generateApiKey(),
          permissions: LegacyApiPermissions.PUBLIC,
        }),
      ],
    });
  }, [users, onChange]);

  const handleRemoveUser = useCallback(
    (index: number) => {
      onChange({ users: users.filter((_, i) => i !== index) });
    },
    [users, onChange],
  );

  const handleUserChange = useCallback(
    (index: number, updates: Partial<LegacyApiUser>) => {
      onChange({
        users: users.map((u, i) =>
          i === index ? create(LegacyApiUserSchema, { ...u, ...updates }) : u,
        ),
      });
    },
    [users, onChange],
  );

  const handleProfileToggle = useCallback(
    (profileName: string, checked: boolean) => {
      const next = checked
        ? [...profiles, profileName]
        : profiles.filter((p) => p !== profileName);
      onChange({ profiles: next });
    },
    [profiles, onChange],
  );

  return (
    <div className="space-y-4">
      {/* Enable toggle */}
      <Card>
        <CardHeader
          title="Legacy API"
          description="Enable the legacy HTTP API for external application integration, such as Limedrop."
        />
        <CardContent>
          <label className="flex cursor-pointer items-center gap-3">
            <input
              type="checkbox"
              checked={legacyApi?.enabled || false}
              onChange={(e) => onChange({ enabled: e.target.checked })}
              className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
            />
            <span className="text-sm text-zinc-300">Enable Legacy API</span>
          </label>
        </CardContent>
      </Card>

      {/* Profiles */}
      <Card>
        <CardHeader
          title="Exposed Profiles"
          description="Select which profiles are accessible through the legacy API."
        />
        <CardContent>
          {allProfiles.length === 0 ? (
            <p className="text-sm text-zinc-500">No profiles configured yet.</p>
          ) : (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {allProfiles.map(({ profile }) => (
                <label
                  key={profile.name}
                  className="flex cursor-pointer items-center gap-3"
                >
                  <input
                    type="checkbox"
                    checked={profiles.includes(profile.name)}
                    onChange={(e) =>
                      handleProfileToggle(profile.name, e.target.checked)
                    }
                    disabled={!legacyApi?.enabled}
                    className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
                  />
                  <span className="truncate text-sm text-zinc-300">
                    {profile.name}
                  </span>
                </label>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Users */}
      <Card>
        <CardHeader
          title="API Users"
          description="Manage users and their API keys for legacy API authentication."
          action={
            <Button
              variant="secondary"
              onClick={handleAddUser}
              disabled={!legacyApi?.enabled}
            >
              <PlusIcon className="h-4 w-4" />
              Add User
            </Button>
          }
        />
        <CardContent>
          {users.length === 0 ? (
            <p className="text-sm text-zinc-500">
              No API users configured. Add a user to enable API access.
            </p>
          ) : (
            <div className="space-y-3">
              {users.map((user, index) => (
                <div
                  key={index}
                  className="grid grid-cols-1 items-end gap-3 rounded-lg border border-zinc-800 bg-zinc-900/50 p-3 sm:grid-cols-[1fr_1fr_auto_auto]"
                >
                  <Input
                    id={`user-name-${index}`}
                    label="Name"
                    placeholder="Username"
                    value={user.name}
                    onChange={(e) =>
                      handleUserChange(index, { name: e.target.value })
                    }
                    disabled={!legacyApi?.enabled}
                  />

                  <Input
                    id={`user-key-${index}`}
                    label="API Key"
                    placeholder="API key"
                    value={user.apiKey}
                    onChange={(e) =>
                      handleUserChange(index, { apiKey: e.target.value })
                    }
                    disabled={!legacyApi?.enabled}
                  />

                  <Select
                    id={`user-perms-${index}`}
                    label="Permissions"
                    options={permissionOptions}
                    value={(
                      user.permissions || LegacyApiPermissions.PUBLIC
                    ).toString()}
                    onChange={(e) =>
                      handleUserChange(index, {
                        permissions: parseInt(
                          e.target.value,
                          10,
                        ) as LegacyApiPermissions,
                      })
                    }
                    disabled={!legacyApi?.enabled}
                  />

                  <div className="flex gap-1 pb-0.5">
                    <Button
                      variant="secondary"
                      onClick={() =>
                        handleUserChange(index, {
                          apiKey: generateApiKey(),
                        })
                      }
                      disabled={!legacyApi?.enabled}
                      title="Regenerate API key"
                    >
                      <KeyIcon className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="secondary"
                      onClick={() => handleRemoveUser(index)}
                      disabled={!legacyApi?.enabled}
                      title="Remove user"
                    >
                      <TrashIcon className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
