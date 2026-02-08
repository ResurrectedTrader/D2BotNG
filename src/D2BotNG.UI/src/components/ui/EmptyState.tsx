import { type ElementType, type ReactNode } from "react";
import clsx from "clsx";

export interface EmptyStateProps {
  icon?: ElementType;
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
}

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: EmptyStateProps) {
  return (
    <div
      className={clsx(
        "flex flex-col items-center justify-center py-12 text-center",
        className,
      )}
    >
      {Icon && (
        <Icon className="mx-auto h-12 w-12 text-zinc-600" aria-hidden="true" />
      )}
      <h3 className="mt-4 text-lg font-semibold text-zinc-100">{title}</h3>
      {description && (
        <p className="mt-2 max-w-sm text-sm text-zinc-400">{description}</p>
      )}
      {action && <div className="mt-6">{action}</div>}
    </div>
  );
}
