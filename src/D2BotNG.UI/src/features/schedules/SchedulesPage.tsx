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
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-3 border-b border-zinc-800/50">
        <div className="flex items-center justify-between gap-3">
          <h1 className="text-lg font-bold text-zinc-100">Schedules</h1>
          <Button onClick={handleCreate} size="sm">
            <PlusIcon className="h-4 w-4" />
            New Schedule
          </Button>
        </div>
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
