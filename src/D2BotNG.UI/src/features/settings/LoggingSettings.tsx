/**
 * LoggingSettings component
 *
 * Displays all registered loggers with per-logger level controls.
 * Changes apply immediately to the MessageServiceSink (UI console).
 * Session-only — levels reset on restart.
 */

import { useCallback } from "react";
import { Card, CardHeader, CardContent, Select } from "@/components/ui";
import { useLogLevels } from "@/stores/event-store";
import { useSetLogLevel } from "@/hooks/useLogLevel";
import { SinkLogLevel } from "@/generated/logging_pb";

const LOG_LEVEL_OPTIONS = [
  { value: String(SinkLogLevel.VERBOSE), label: "Verbose" },
  { value: String(SinkLogLevel.DEBUG), label: "Debug" },
  { value: String(SinkLogLevel.INFORMATION), label: "Information" },
  { value: String(SinkLogLevel.WARNING), label: "Warning" },
  { value: String(SinkLogLevel.ERROR), label: "Error" },
  { value: String(SinkLogLevel.FATAL), label: "Fatal" },
];

export function LoggingSettings() {
  const logLevels = useLogLevels();
  const setLogLevel = useSetLogLevel();

  const handleLevelChange = useCallback(
    (category: string, level: SinkLogLevel) => {
      setLogLevel.mutate({ category, level });
    },
    [setLogLevel],
  );

  const sorted = [...logLevels].sort((a, b) =>
    a.category.localeCompare(b.category),
  );

  return (
    <Card>
      <CardHeader
        title="Log Levels"
        description="Control which log messages appear in the UI console per logger. Changes apply immediately and reset on restart."
      />
      <CardContent>
        {sorted.length === 0 ? (
          <p className="text-sm text-zinc-500">No loggers registered yet.</p>
        ) : (
          <div className="space-y-3">
            {sorted.map((entry) => (
              <div key={entry.category} className="flex items-center gap-4">
                <span
                  className="min-w-0 flex-1 truncate text-sm text-zinc-300"
                  title={entry.category}
                >
                  {entry.category}
                </span>
                <div className="w-40 shrink-0">
                  <Select
                    options={LOG_LEVEL_OPTIONS}
                    value={String(entry.level)}
                    onChange={(e) =>
                      handleLevelChange(
                        entry.category,
                        Number(e.target.value) as SinkLogLevel,
                      )
                    }
                  />
                </div>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
