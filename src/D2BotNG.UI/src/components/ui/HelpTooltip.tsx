import { useState } from "react";
import { QuestionMarkCircleIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";

export interface HelpTooltipProps {
  /** The help text to display on hover */
  text: string;
  /** Additional CSS classes */
  className?: string;
}

/**
 * A question mark icon that displays help text on hover.
 */
export function HelpTooltip({ text, className }: HelpTooltipProps) {
  const [isVisible, setIsVisible] = useState(false);

  return (
    <span className={clsx("relative inline-flex items-center", className)}>
      <QuestionMarkCircleIcon
        className="h-4 w-4 cursor-help text-zinc-500 hover:text-zinc-400 transition-colors"
        onMouseEnter={() => setIsVisible(true)}
        onMouseLeave={() => setIsVisible(false)}
      />
      {isVisible && (
        <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 z-50">
          <div className="bg-zinc-800 text-zinc-200 text-xs rounded-lg px-3 py-2 shadow-lg ring-1 ring-zinc-700 min-w-64 max-w-sm whitespace-normal">
            {text}
          </div>
          {/* Arrow */}
          <div className="absolute top-full left-1/2 -translate-x-1/2 -mt-px">
            <div className="border-4 border-transparent border-t-zinc-800" />
          </div>
        </div>
      )}
    </span>
  );
}
