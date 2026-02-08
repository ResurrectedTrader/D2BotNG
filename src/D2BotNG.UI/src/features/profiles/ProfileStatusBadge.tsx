/**
 * ProfileStatusBadge component
 *
 * Displays the current profile state with appropriate colors.
 */

import { useRef, useState, useEffect } from "react";
import { ProfileState } from "@/generated/common_pb";
import { Tooltip } from "@/components/ui";

export interface ProfileStatusBadgeProps {
  state: ProfileState;
  status?: string;
}

const stateConfig: Record<ProfileState, { label: string; color: string }> = {
  [ProfileState.STOPPED]: { label: "Stopped", color: "text-zinc-400" },
  [ProfileState.STARTING]: { label: "Starting", color: "text-yellow-400" },
  [ProfileState.RUNNING]: { label: "Running", color: "text-green-400" },
  [ProfileState.BUSY]: { label: "Busy", color: "text-orange-400" },
  [ProfileState.ERROR]: { label: "Error", color: "text-red-400" },
  [ProfileState.STOPPING]: { label: "Stopping", color: "text-yellow-400" },
};

export function ProfileStatusBadge({ state, status }: ProfileStatusBadgeProps) {
  const textRef = useRef<HTMLSpanElement>(null);
  const [isTruncated, setIsTruncated] = useState(false);
  const config = stateConfig[state] ?? stateConfig[ProfileState.STOPPED];
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
