/**
 * FrameworksPage component
 *
 * Manage the list of frameworks (launch bundles): add/edit/remove, and see how
 * many profiles reference each one (configured vs currently running).
 */

import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Button,
  Card,
  EmptyState,
  LoadingSpinner,
  DeleteConfirmationDialog,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableHeader,
  TableCell,
  Tooltip,
} from "@/components/ui";
import { useDeleteFramework } from "@/hooks";
import {
  useFrameworks,
  useIsLoading,
  type FrameworkWithUsageData,
} from "@/stores/event-store";
import {
  CommandLineIcon,
  PlusIcon,
  PencilIcon,
  DocumentDuplicateIcon,
  TrashIcon,
} from "@heroicons/react/24/outline";

/** Last path component (filename) of a path, or "-" when empty. */
function fileName(path: string): string {
  if (!path) return "-";
  const parts = path.replace(/\\/g, "/").split("/");
  return parts[parts.length - 1] || "-";
}

export function FrameworksPage() {
  const navigate = useNavigate();
  const isLoading = useIsLoading();
  const frameworks = useFrameworks();
  const deleteFramework = useDeleteFramework();

  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);

  // Tailor the delete warning to actual usage: no scare text for an unused
  // framework, and an explicit heads-up when profiles are currently running on it.
  const deleteTargetData = frameworks.find(
    (f) => f.framework.name === deleteTarget,
  );
  const configuredCount = deleteTargetData?.configuredProfiles.length ?? 0;
  const activeCount = deleteTargetData?.activeProfiles.length ?? 0;
  const profilesUse = configuredCount === 1 ? "profile uses" : "profiles use";
  const deleteWarning =
    configuredCount === 0
      ? "No profiles use this framework."
      : activeCount > 0
        ? `${configuredCount} ${profilesUse} this framework and ${activeCount} ${activeCount === 1 ? "is" : "are"} currently running. Running games may be stopped by health monitoring, and these profiles will need another framework selected before they can launch again.`
        : `${configuredCount} ${profilesUse} this framework and will need another one selected before launching again.`;

  const handleAdd = () => navigate("/frameworks/new");

  if (isLoading) {
    return <LoadingSpinner fullPage />;
  }

  const hasFrameworks = frameworks.length > 0;

  return (
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-3 border-b border-zinc-800/50">
        <div className="flex items-center justify-between gap-3">
          <h1 className="text-lg font-bold text-zinc-100">Frameworks</h1>
          <div className="flex items-center gap-2">
            <Button size="sm" onClick={handleAdd}>
              <PlusIcon className="h-4 w-4" />
              Add Framework
            </Button>
          </div>
        </div>
      </div>

      {/* Content */}
      {hasFrameworks ? (
        <Card>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeader>Name</TableHeader>
                <TableHeader>Game Directory</TableHeader>
                <TableHeader>D2BS Path</TableHeader>
                <TableHeader>DLL</TableHeader>
                <TableHeader>Version</TableHeader>
                <TableHeader>Profiles</TableHeader>
                <TableHeader className="text-right">Actions</TableHeader>
              </TableRow>
            </TableHead>
            <TableBody>
              {frameworks.map((data) => (
                <FrameworkRow
                  key={data.framework.name}
                  data={data}
                  onEdit={() =>
                    navigate(
                      `/frameworks/${encodeURIComponent(data.framework.name)}`,
                    )
                  }
                  onClone={() =>
                    navigate(
                      `/frameworks/new?clone=${encodeURIComponent(data.framework.name)}`,
                    )
                  }
                  onDelete={() => setDeleteTarget(data.framework.name)}
                />
              ))}
            </TableBody>
          </Table>
        </Card>
      ) : (
        <EmptyState
          icon={CommandLineIcon}
          title="No frameworks yet"
          description="Add a framework, then select one per profile."
          action={
            <Button onClick={handleAdd}>
              <PlusIcon className="h-4 w-4" />
              Add Framework
            </Button>
          }
        />
      )}

      {/* Delete confirmation */}
      <DeleteConfirmationDialog
        open={deleteTarget !== null}
        entityType="Framework"
        entityName={deleteTarget ?? ""}
        warningMessage={deleteWarning}
        isPending={deleteFramework.isPending}
        onConfirm={async () => {
          if (deleteTarget) {
            await deleteFramework.mutateAsync(deleteTarget);
            setDeleteTarget(null);
          }
        }}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

interface FrameworkRowProps {
  data: FrameworkWithUsageData;
  onEdit: () => void;
  onClone: () => void;
  onDelete: () => void;
}

function FrameworkRow({ data, onEdit, onClone, onDelete }: FrameworkRowProps) {
  const { framework, configuredProfiles, activeProfiles } = data;
  const activeSet = new Set(activeProfiles);

  const usageTooltip =
    configuredProfiles.length > 0 ? (
      <ul className="space-y-0.5">
        {configuredProfiles.map((name) => (
          <li key={name}>
            {name}
            {activeSet.has(name) ? " (active)" : ""}
          </li>
        ))}
      </ul>
    ) : (
      "No profiles use this framework"
    );

  return (
    <TableRow onDoubleClick={onEdit} className="cursor-pointer select-none">
      <TableCell className="font-medium text-zinc-100">
        {framework.name}
      </TableCell>
      <TableCell className="font-mono text-zinc-300">
        <Tooltip content={framework.gameDirectory || "Not set"}>
          <span className="cursor-default">
            {fileName(framework.gameDirectory)}
          </span>
        </Tooltip>
      </TableCell>
      <TableCell className="font-mono text-zinc-300">
        <Tooltip content={framework.d2bsPath || "Not set"}>
          <span className="cursor-default">{fileName(framework.d2bsPath)}</span>
        </Tooltip>
      </TableCell>
      <TableCell className="font-mono text-zinc-300">
        {framework.dllPaths.length > 0
          ? framework.dllPaths.join(", ")
          : "D2BS.dll"}
      </TableCell>
      <TableCell className="text-zinc-300">
        {framework.gameVersion || "1.14d"}
      </TableCell>
      <TableCell>
        <Tooltip content={usageTooltip}>
          <span className="inline-flex cursor-default items-center gap-2">
            <span className="text-zinc-300">
              {configuredProfiles.length} configured
            </span>
            {activeProfiles.length > 0 && (
              <span className="text-green-400">
                {activeProfiles.length} active
              </span>
            )}
          </span>
        </Tooltip>
      </TableCell>
      <TableCell className="text-right">
        {/* Stop double-clicks on the action buttons from also triggering the row's
            double-click-to-edit (e.g. a double-click on Delete). */}
        <div
          className="inline-flex items-center gap-2"
          onDoubleClick={(e) => e.stopPropagation()}
        >
          <Button
            variant="ghost"
            size="sm"
            onClick={onEdit}
            aria-label="Edit framework"
          >
            <PencilIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={onClone}
            aria-label="Clone framework"
          >
            <DocumentDuplicateIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={onDelete}
            aria-label="Delete framework"
          >
            <TrashIcon className="h-4 w-4" />
          </Button>
        </div>
      </TableCell>
    </TableRow>
  );
}
