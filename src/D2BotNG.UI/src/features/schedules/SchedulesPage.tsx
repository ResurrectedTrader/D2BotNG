/**
 * SchedulesPage component
 *
 * Main page for managing schedules.
 * Includes CRUD operations for schedules with time periods.
 */

import { useState, useCallback } from "react";
import {
  Button,
  Card,
  EmptyState,
  LoadingSpinner,
  DeleteConfirmationDialog,
} from "@/components/ui";
import { useDeleteSchedule, useDeleteDialog } from "@/hooks";
import { useSchedules, useIsLoading } from "@/stores/event-store";
import type { Schedule } from "@/generated/schedules_pb";
import { ScheduleRow } from "./ScheduleRow";
import { ScheduleDialog } from "./ScheduleDialog";
import { PlusIcon, ClockIcon } from "@heroicons/react/24/outline";

export function SchedulesPage() {
  // Get schedules from event store
  const isLoading = useIsLoading();
  const schedulesData = useSchedules();
  const schedules = schedulesData.map((s) => s);
  const deleteSchedule = useDeleteSchedule();

  // Dialog states
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [editingSchedule, setEditingSchedule] = useState<Schedule | null>(null);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  // Delete dialog hook
  const {
    deleteTarget,
    isOpen: isDeleteDialogOpen,
    requestDelete,
    confirmDelete,
    cancelDelete,
  } = useDeleteDialog<Schedule>(deleteSchedule);

  const handleToggleExpand = useCallback((id: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const handleCreate = useCallback(() => {
    setEditingSchedule(null);
    setIsDialogOpen(true);
  }, []);

  const handleEdit = useCallback((schedule: Schedule) => {
    setEditingSchedule(schedule);
    setIsDialogOpen(true);
  }, []);

  const handleDialogClose = useCallback(() => {
    setIsDialogOpen(false);
    setEditingSchedule(null);
  }, []);

  const hasSchedules = schedules && schedules.length > 0;

  // Loading state - waiting for initial data from event stream
  if (isLoading) {
    return <LoadingSpinner fullPage />;
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-100">Schedules</h1>
          <p className="mt-1 text-sm text-zinc-400">
            Manage time windows when profiles should run.
          </p>
        </div>
      </div>

      {/* Action bar */}
      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={handleCreate}>
          <PlusIcon className="h-4 w-4" />
          New Schedule
        </Button>
      </div>

      {/* Content */}
      {hasSchedules ? (
        <Card className="divide-y divide-zinc-800">
          {schedules.map((schedule) => (
            <ScheduleRow
              key={schedule.name}
              schedule={schedule}
              isExpanded={expandedIds.has(schedule.name)}
              onToggle={() => handleToggleExpand(schedule.name)}
              onEdit={handleEdit}
              onDelete={requestDelete}
            />
          ))}
        </Card>
      ) : (
        <EmptyState
          icon={ClockIcon}
          title="No schedules yet"
          description="Create your first schedule to define time windows for your profiles."
          action={
            <Button onClick={handleCreate}>
              <PlusIcon className="h-4 w-4" />
              Create Schedule
            </Button>
          }
        />
      )}

      {/* Create/Edit dialog */}
      <ScheduleDialog
        open={isDialogOpen}
        onClose={handleDialogClose}
        schedule={editingSchedule}
      />

      {/* Delete confirmation dialog */}
      <DeleteConfirmationDialog
        open={isDeleteDialogOpen}
        entityType="Schedule"
        entityName={deleteTarget?.name ?? ""}
        warningMessage="Profiles using this schedule will no longer have a schedule assigned."
        isPending={deleteSchedule.isPending}
        onConfirm={confirmDelete}
        onCancel={cancelDelete}
      />
    </div>
  );
}
