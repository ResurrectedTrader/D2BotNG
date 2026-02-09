/**
 * ProfileStatusBadge component
 *
 * Displays the current profile state with appropriate colors.
 */

import { useRef, useState, useEffect } from "react";
import { RunState } from "@/generated/common_pb";
import { Tooltip } from "@/components/ui";

export interface ProfileStatusBadgeProps {
  state: RunState;
  status?: string;
}

const stateConfig: Record<RunState, { label: string; color: string }> = {
  [RunState.STOPPED]: { label: "Stopped", color: "text-zinc-400" },
  [RunState.STARTING]: { label: "Starting", color: "text-yellow-400" },
  [RunState.RUNNING]: { label: "Running", color: "text-green-400" },
  [RunState.ERROR]: { label: "Error", color: "text-red-400" },
  [RunState.STOPPING]: { label: "Stopping", color: "text-yellow-400" },
};

export function ProfileStatusBadge({ state, status }: ProfileStatusBadgeProps) {
  const textRef = useRef<HTMLSpanElement>(null);
  const [isTruncated, setIsTruncated] = useState(false);
  const config = stateConfig[state] ?? stateConfig[RunState.STOPPED];
  const displayText = status || config.label;

  // Check if text is truncated
  useEffect(() => {
    const el = textRef.current;
    if (el) {
      setIsTruncated(el.scrollWidth > el.clientWidth);
    }
  }, [displayText]);

  return (
    <Tooltip
      content={isTruncated ? displayText : null}
      className="block max-w-full"
    >
      <span
        ref={textRef}
        className={`block truncate text-xs font-medium ${config.color}`}
      >
        {displayText}
      </span>
    </Tooltip>
  );
}
