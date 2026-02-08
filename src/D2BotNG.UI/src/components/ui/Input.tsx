import { forwardRef, type InputHTMLAttributes } from "react";
import clsx from "clsx";
import { HelpTooltip } from "./HelpTooltip";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  tooltip?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, tooltip, id, className, ...props }, ref) => {
    return (
      <div className="w-full">
        {label && (
          <div className="mb-1.5 flex items-center gap-1.5">
            <label
              htmlFor={id}
              className="text-sm font-medium text-zinc-400"
            >
              {label}
            </label>
            {tooltip && <HelpTooltip text={tooltip} />}
          </div>
        )}
        <input
          ref={ref}
          id={id}
          className={clsx(
            "block w-full rounded-lg border-0 bg-zinc-800 px-3 py-2 text-zinc-100",
            "ring-1 ring-inset placeholder:text-zinc-500",
            "focus:ring-2 focus:ring-inset focus:ring-d2-gold",
            "disabled:pointer-events-none disabled:opacity-50",
            "sm:text-sm sm:leading-6",
            "transition-colors",
            error ? "ring-red-500" : "ring-zinc-700",
            className,
          )}
          {...props}
        />
        {error && <p className="mt-1.5 text-sm text-red-500">{error}</p>}
      </div>
    );
  },
);

Input.displayName = "Input";
