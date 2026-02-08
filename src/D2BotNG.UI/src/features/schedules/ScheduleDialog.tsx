/**
 * ScheduleDialog component
 *
 * Dialog for creating and editing schedules.
 * Allows entering a name and managing time periods.
 */

import { useState, useCallback, useEffect, useMemo } from "react";
import {
  Dialog,
  DialogHeader,
  DialogContent,
  DialogFooter,
  Button,
  Input,
} from "@/components/ui";
import { useCreateSchedule, useUpdateSchedule } from "@/hooks";
import { useSchedules } from "@/stores/event-store";
import type { Schedule } from "@/generated/schedules_pb";
import { TimePeriodInput, type TimePeriodValue } from "./TimePeriodInput";
import { PlusIcon } from "@heroicons/react/24/outline";

export interface ScheduleDialogProps {
  open: boolean;
  onClose: () => void;
  schedule?: Schedule | null;
}

/**
 * Create an empty time period value
 */
function createEmptyPeriod(): TimePeriodValue {
  return {
    startHour: 0,
    startMinute: 0,
    endHour: 23,
    endMinute: 59,
  };
}

/**
 * Validate time periods
 * Returns an error message or undefined if valid
 */
function validatePeriods(periods: TimePeriodValue[]): string | undefined {
  for (let i = 0; i < periods.length; i++) {
    const period = periods[i];
    const startTime = period.startHour * 60 + period.startMinute;
    const endTime = period.endHour * 60 + period.endMinute;

    // Allow overnight periods (end < start means it wraps to next day)
    // But warn if start and end are exactly the same
    if (startTime === endTime) {
      return "Period " + (i + 1) + " has the same start and end time";
    }
  }
  return undefined;
}

export function ScheduleDialog({
  open,
  onClose,
  schedule,
}: ScheduleDialogProps) {
  const [name, setName] = useState("");
  const [periods, setPeriods] = useState<TimePeriodValue[]>([]);
  const [nameError, setNameError] = useState<string | undefined>();
  const [periodsError, setPeriodsError] = useState<string | undefined>();

  const createSchedule = useCreateSchedule();
  const updateSchedule = useUpdateSchedule();
  const schedulesData = useSchedules();

  const isEditing = schedule !== null && schedule !== undefined;

  // Build set of existing schedule names for uniqueness validation
  const existingNames = useMemo(
    () => new Set(schedulesData.map((s) => s.name.toLowerCase())),
    [schedulesData],
  );

  // Reset form when dialog opens/closes or schedule changes
  useEffect(() => {
    if (open) {
      if (schedule) {
        setName(schedule.name);
        setPeriods(
          schedule.periods.map((p) => ({
            startHour: p.startHour,
            startMinute: p.startMinute,
            endHour: p.endHour,
            endMinute: p.endMinute,
          })),
        );
      } else {
        setName("");
        setPeriods([createEmptyPeriod()]);
      }
      setNameError(undefined);
      setPeriodsError(undefined);
    }
  }, [open, schedule]);

  const handleAddPeriod = useCallback(() => {
    setPeriods((prev) => [...prev, createEmptyPeriod()]);
  }, []);

  const handleRemovePeriod = useCallback((index: number) => {
    setPeriods((prev) => prev.filter((_, idx) => idx !== index));
  }, []);

  const handlePeriodChange = useCallback(
    (index: number, value: TimePeriodValue) => {
      setPeriods((prev) => prev.map((p, idx) => (idx === index ? value : p)));
    },
    [],
  );

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault();

      // Validate name
      if (!name.trim()) {
        setNameError("Name is required");
        return;
      }

      // Check for duplicate name (allow keeping the same name when editing)
      const trimmedName = name.trim().toLowerCase();
      const isSameName = isEditing && schedule?.name.toLowerCase() === trimmedName;
      if (!isSameName && existingNames.has(trimmedName)) {
        setNameError("A schedule with this name already exists");
        return;
      }

      // Validate periods
      const periodError = validatePeriods(periods);
      if (periodError) {
        setPeriodsError(periodError);
        return;
      }

      setNameError(undefined);
      setPeriodsError(undefined);

      const scheduleData = {
        name: name.trim(),
        periods: periods.map((p) => ({
          startHour: p.startHour,
          startMinute: p.startMinute,
          endHour: p.endHour,
          endMinute: p.endMinute,
        })),
      };

      try {
        if (isEditing && schedule) {
          const isRename = schedule.name !== name.trim();
          await updateSchedule.mutateAsync({
            schedule: scheduleData,
            originalName: isRename ? schedule.name : undefined,
          });
        } else {
          await createSchedule.mutateAsync(scheduleData);
        }
        onClose();
      } catch {
        // Error toast is handled by mutation hooks
      }
    },
    [
      name,
      periods,
      isEditing,
      schedule,
      existingNames,
      updateSchedule,
      createSchedule,
      onClose,
    ],
  );

  const isPending = createSchedule.isPending || updateSchedule.isPending;

  return (
    <Dialog open={open} onClose={onClose}>
      <form onSubmit={handleSubmit}>
        <DialogHeader
          title={isEditing ? "Edit Schedule" : "Create Schedule"}
          description={
            isEditing
              ? "Update the schedule name and time periods."
              : "Create a new schedule with time periods."
          }
          onClose={onClose}
        />

        <DialogContent className="space-y-4">
          <Input
            id="name"
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError}
            placeholder="Enter schedule name"
            autoFocus
          />

          <div>
            <label className="mb-1.5 block text-sm font-medium text-zinc-400">
              Time Periods
            </label>
            <div className="space-y-2">
              {periods.map((period, index) => (
                <TimePeriodInput
                  key={index}
                  value={period}
                  onChange={(value) => handlePeriodChange(index, value)}
                  onRemove={() => handleRemovePeriod(index)}
                  index={index}
                />
              ))}
            </div>
            {periodsError && (
              <p className="mt-1.5 text-sm text-red-400">{periodsError}</p>
            )}
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleAddPeriod}
              className="mt-2"
            >
              <PlusIcon className="h-4 w-4" />
              Add Period
            </Button>
          </div>
        </DialogContent>

        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={onClose}
            disabled={isPending}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={isPending}>
            {isEditing ? "Save Changes" : "Create Schedule"}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  );
}
