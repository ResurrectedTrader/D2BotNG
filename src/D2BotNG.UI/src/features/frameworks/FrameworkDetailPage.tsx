/**
 * FrameworkDetailPage component
 *
 * Detail view for a single framework. Supports both creating new frameworks and
 * editing existing ones.
 */

import { useCallback, useMemo } from "react";
import { useParams, useNavigate, useSearchParams } from "react-router-dom";
import { Button, EmptyState } from "@/components/ui";
import { useCreateFramework, useUpdateFramework } from "@/hooks";
import type { FrameworkInput } from "@/hooks/useFrameworks";
import { useFramework } from "@/stores/event-store";
import { FrameworkForm } from "./FrameworkForm";
import { ArrowLeftIcon } from "@heroicons/react/24/outline";

export function FrameworkDetailPage() {
  const { id: name } = useParams<{ id: string }>();
  const decodedName = name ? decodeURIComponent(name) : "";
  const navigate = useNavigate();
  const isNew = !name;

  const frameworkData = useFramework(decodedName);
  const framework = frameworkData?.framework;

  // Cloning: /frameworks/new?clone=<name> prefills the form from an existing framework.
  // Memoize on the framework message, not the usage wrapper: the store keeps the
  // message identity-stable across usage-only snapshot re-broadcasts (profile
  // start/stop), so the form's seeding effect won't reset mid-edit.
  const [searchParams] = useSearchParams();
  const cloneSource = searchParams.get("clone");
  const cloneFramework = useFramework(cloneSource ?? "")?.framework;
  const cloneInitialValues = useMemo(() => {
    if (!cloneFramework) return undefined;
    return {
      ...cloneFramework,
      name: `${cloneFramework.name} - Copy`,
    };
  }, [cloneFramework]);

  const createFramework = useCreateFramework();
  const updateFramework = useUpdateFramework();

  const handleBack = useCallback(() => {
    navigate("/frameworks");
  }, [navigate]);

  const handleSubmit = useCallback(
    async (data: FrameworkInput) => {
      try {
        if (isNew) {
          await createFramework.mutateAsync(data);
        } else {
          const isRename = decodedName !== data.name;
          await updateFramework.mutateAsync({
            framework: data,
            originalName: isRename ? decodedName : undefined,
          });
        }
        navigate("/frameworks", { replace: true });
      } catch {
        // Error handling is done in the hooks
      }
    },
    [isNew, createFramework, updateFramework, navigate, decodedName],
  );

  if (!isNew && !framework) {
    return (
      <EmptyState
        title="Framework not found"
        description="The framework you're looking for doesn't exist or hasn't loaded yet."
        action={<Button onClick={handleBack}>Back to Frameworks</Button>}
      />
    );
  }

  const isSubmitting = createFramework.isPending || updateFramework.isPending;

  return (
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 -mx-4 border-b border-zinc-800/50 bg-zinc-950 px-4 pb-3 pt-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="sm" onClick={handleBack}>
            <ArrowLeftIcon className="h-4 w-4" />
          </Button>
          <h1 className="text-lg font-bold text-zinc-100">
            {isNew ? "New Framework" : framework?.name}
          </h1>
        </div>
      </div>

      <FrameworkForm
        framework={framework}
        initialValues={cloneInitialValues}
        onSubmit={handleSubmit}
        onCancel={handleBack}
        isLoading={isSubmitting}
      />
    </div>
  );
}
