/**
 * EnvVarsEditor
 *
 * Reusable key/value list editor for environment variables. Presentational only —
 * the parent owns the array and converts to/from a record. Used by the framework
 * and profile editors.
 */

import { Button } from "./Button";
import { Input } from "./Input";
import { PlusIcon, TrashIcon } from "@heroicons/react/24/outline";

export interface EnvVar {
  key: string;
  value: string;
}

export interface EnvVarsEditorProps {
  value: EnvVar[];
  onChange: (next: EnvVar[]) => void;
  /** Prefix for input ids; keep unique per usage on a page. */
  idPrefix?: string;
}

export function EnvVarsEditor({
  value,
  onChange,
  idPrefix = "env",
}: EnvVarsEditorProps) {
  return (
    <div className="space-y-2">
      {value.map((pair, index) => (
        <div key={index} className="flex items-center gap-2">
          <div className="flex-1">
            <Input
              id={`${idPrefix}-key-${index}`}
              value={pair.key}
              placeholder="NAME"
              onChange={(e) =>
                onChange(
                  value.map((p, i) =>
                    i === index ? { ...p, key: e.target.value } : p,
                  ),
                )
              }
            />
          </div>
          <div className="flex-1">
            <Input
              id={`${idPrefix}-value-${index}`}
              value={pair.value}
              placeholder="value"
              onChange={(e) =>
                onChange(
                  value.map((p, i) =>
                    i === index ? { ...p, value: e.target.value } : p,
                  ),
                )
              }
            />
          </div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-label="Remove variable"
            onClick={() => onChange(value.filter((_, i) => i !== index))}
          >
            <TrashIcon className="h-4 w-4" />
          </Button>
        </div>
      ))}
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => onChange([...value, { key: "", value: "" }])}
      >
        <PlusIcon className="h-4 w-4" />
        Add Variable
      </Button>
    </div>
  );
}
