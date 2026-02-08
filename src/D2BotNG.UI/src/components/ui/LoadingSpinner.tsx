/**
 * Loading Spinner Component
 *
 * Branded loading spinner used across the application.
 * Centralized to ensure consistent styling and easy updates.
 */

import clsx from "clsx";

export interface LoadingSpinnerProps {
  /** Display as full-page centered spinner */
  fullPage?: boolean;
  /** Size variant */
  size?: "sm" | "md" | "lg";
  /** Additional CSS classes */
  className?: string;
}

const sizeClasses = {
  sm: "h-4 w-4 border",
  md: "h-8 w-8 border-2",
  lg: "h-12 w-12 border-2",
};

export function LoadingSpinner({
  fullPage = false,
  size = "md",
  className,
}: LoadingSpinnerProps) {
  const spinner = (
    <div
      className={clsx(
        "animate-spin rounded-full border-zinc-600 border-t-d2-gold",
        sizeClasses[size],
        className,
      )}
    />
  );

  if (fullPage) {
    return (
      <div className="flex items-center justify-center py-12">{spinner}</div>
    );
  }

  return spinner;
}
