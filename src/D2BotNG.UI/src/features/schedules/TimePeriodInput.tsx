/**
 * TimePeriodInput component
 *
 * Input component for entering a time period with start and end times.
 * Displays hour/minute inputs for both start and end times.
 */

import { useCallback } from "react";
import { Button } from "@/components/ui";
import { TrashIcon } from "@heroicons/react/24/outline";

export interface TimePeriodValue {
  startHour: number;
  startMinute: number;
  endHour: number;
  endMinute: number;
}

export interface TimePeriodInputProps {
  value: TimePeriodValue;
  onChange: (value: TimePeriodValue) => void;
  onRemove: () => void;
  index: number;
}

/**
 * Pad a number to 2 digits
 */
function pad(num: number): string {
  return num.toString().padStart(2, "0");
}

/**
 * Clamp a value between min and max
 */
function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function TimePeriodInput({
  value,
  onChange,
  onRemove,
  index,
}: TimePeriodInputProps) {
  const handleStartHourChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = clamp(parseInt(e.target.value, 10) || 0, 0, 23);
      onChange({ ...value, startHour: newValue });
    },
    [value, onChange],
  );

  const handleStartMinuteChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = clamp(parseInt(e.target.value, 10) || 0, 0, 59);
      onChange({ ...value, startMinute: newValue });
    },
    [value, onChange],
  );

  const handleEndHourChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = clamp(parseInt(e.target.value, 10) || 0, 0, 23);
      onChange({ ...value, endHour: newValue });
    },
    [value, onChange],
  );

  const handleEndMinuteChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = clamp(parseInt(e.target.value, 10) || 0, 0, 59);
      onChange({ ...value, endMinute: newValue });
    },
    [value, onChange],
  );

  return (
    <div className="flex items-center gap-3">
      <span className="text-sm text-zinc-500 w-6">{index + 1}.</span>

      {/* Start time */}
      <div className="flex items-center gap-1">
        <input
          type="number"
          min={0}
          max={23}
          value={pad(value.startHour)}
          onChange={handleStartHourChange}
          className="w-14 rounded-lg border-0 bg-zinc-800 px-2 py-1.5 text-center text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm transition-colors"
          aria-label="Start hour"
        />
        <span className="text-zinc-500">:</span>
        <input
          type="number"
          min={0}
          max={59}
          value={pad(value.startMinute)}
          onChange={handleStartMinuteChange}
          className="w-14 rounded-lg border-0 bg-zinc-800 px-2 py-1.5 text-center text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm transition-colors"
          aria-label="Start minute"
        />
      </div>

      <span className="text-zinc-500">-</span>

      {/* End time */}
      <div className="flex items-center gap-1">
        <input
          type="number"
          min={0}
          max={23}
          value={pad(value.endHour)}
          onChange={handleEndHourChange}
          className="w-14 rounded-lg border-0 bg-zinc-800 px-2 py-1.5 text-center text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm transition-colors"
          aria-label="End hour"
        />
        <span className="text-zinc-500">:</span>
        <input
          type="number"
          min={0}
          max={59}
          value={pad(value.endMinute)}
          onChange={handleEndMinuteChange}
          className="w-14 rounded-lg border-0 bg-zinc-800 px-2 py-1.5 text-center text-zinc-100 ring-1 ring-inset ring-zinc-700 placeholder:text-zinc-500 focus:ring-2 focus:ring-inset focus:ring-d2-gold sm:text-sm transition-colors"
          aria-label="End minute"
        />
      </div>

      {/* Remove button */}
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={onRemove}
        aria-label="Remove time period"
      >
        <TrashIcon className="h-4 w-4 text-zinc-400 hover:text-red-400" />
      </Button>
    </div>
  );
}
