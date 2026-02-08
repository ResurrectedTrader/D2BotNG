/**
 * ScheduleRow component
 *
 * A row in the schedule list that can be expanded to show time periods.
 */

import { Button } from "@/components/ui";
import type { Schedule, TimePeriod } from "@/generated/schedules_pb";
import {
  ChevronRightIcon,
  PencilIcon,
  TrashIcon,
} from "@heroicons/react/24/outline";

export interface ScheduleRowProps {
  schedule: Schedule;
  isExpanded: boolean;
  onToggle: () => void;
  onEdit: (schedule: Schedule) => void;
  onDelete: (schedule: Schedule) => void;
}

/**
 * Format a time as "HH:MM"
 */
function formatTime(hour: number, minute: number): string {
  return `${hour.toString().padStart(2, "0")}:${minute.toString().padStart(2, "0")}`;
}

/**
 * Format a time period as "HH:MM - HH:MM"
 */
function formatTimePeriod(period: TimePeriod): string {
  return `${formatTime(period.startHour, period.startMinute)} - ${formatTime(period.endHour, period.endMinute)}`;
}

export function ScheduleRow({
  schedule,
  isExpanded,
  onToggle,
  onEdit,
  onDelete,
}: ScheduleRowProps) {
  const periodCount = schedule.periods.length;

  return (
    <>
      {/* Row header */}
      <div
        className="flex items-center justify-between px-4 py-3 cursor-pointer hover:bg-zinc-800/50 transition-colors"
        onClick={onToggle}
      >
        <div className="flex items-center gap-3">
          <ChevronRightIcon
            className={`h-4 w-4 text-zinc-400 transition-transform ${isExpanded ? "rotate-90" : ""}`}
          />
          <div>
            <span className="font-medium text-zinc-100">{schedule.name}</span>
            <span className="ml-3 text-sm text-zinc-500">
              {periodCount} {periodCount === 1 ? "period" : "periods"}
            </span>
          </div>
        </div>

        <div
          className="flex items-center gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onEdit(schedule)}
            aria-label="Edit schedule"
          >
            <PencilIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onDelete(schedule)}
            aria-label="Delete schedule"
          >
            <TrashIcon className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Expanded content */}
      {isExpanded && (
        <div className="bg-zinc-900/50">
          {schedule.periods.length > 0 ? (
            <div className="divide-y divide-zinc-800/50">
              {schedule.periods.map((period, index) => (
                <div
                  key={index}
                  className="flex items-center px-4 py-2 pl-11 text-sm"
                >
                  <span className="text-zinc-300 font-mono">
                    {formatTimePeriod(period)}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div className="px-4 py-4 pl-11 text-sm text-zinc-500">
              No time periods defined. Edit to add periods.
            </div>
          )}
        </div>
      )}
    </>
  );
}
