import { forwardRef, useState, type InputHTMLAttributes } from "react";
import { EyeIcon, EyeSlashIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";
import { HelpTooltip } from "./HelpTooltip";

export interface PasswordInputProps extends Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "type"
> {
  label?: string;
  error?: string;
  tooltip?: string;
}

export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(
  ({ label, error, tooltip, id, className, ...props }, ref) => {
    const [showPassword, setShowPassword] = useState(false);

    return (
      <div className="w-full">
        {label && (
          <div className="mb-1.5 flex items-center gap-1.5">
            <label htmlFor={id} className="text-sm font-medium text-zinc-400">
              {label}
            </label>
            {tooltip && <HelpTooltip text={tooltip} />}
          </div>
        )}
        <div className="relative">
          <input
            ref={ref}
            id={id}
            type={showPassword ? "text" : "password"}
            className={clsx(
              "block w-full rounded-lg border-0 bg-zinc-800 px-3 py-2 pr-10 text-zinc-100",
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
          <button
            type="button"
            className="absolute right-2 top-1/2 -translate-y-1/2 text-zinc-400 hover:text-zinc-200 transition-colors"
            onClick={() => setShowPassword(!showPassword)}
            title={showPassword ? "Hide password" : "Show password"}
          >
            {showPassword ? (
              <EyeSlashIcon className="h-5 w-5" />
            ) : (
              <EyeIcon className="h-5 w-5" />
            )}
          </button>
        </div>
        {error && <p className="mt-1.5 text-sm text-red-500">{error}</p>}
      </div>
    );
  },
);

PasswordInput.displayName = "PasswordInput";
