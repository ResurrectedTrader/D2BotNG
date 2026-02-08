import { type ReactNode } from "react";
import clsx from "clsx";

const variants = {
  gold: "bg-d2-gold/10 text-d2-gold ring-d2-gold/20",
  green: "bg-green-500/10 text-green-400 ring-green-500/20",
  red: "bg-red-500/10 text-red-400 ring-red-500/20",
  yellow: "bg-yellow-500/10 text-yellow-400 ring-yellow-500/20",
  orange: "bg-orange-500/10 text-orange-400 ring-orange-500/20",
  gray: "bg-zinc-500/10 text-zinc-400 ring-zinc-500/20",
  blue: "bg-blue-500/10 text-blue-400 ring-blue-500/20",
};

export interface BadgeProps {
  variant?: keyof typeof variants;
  className?: string;
  title?: string;
  children: ReactNode;
}

export function Badge({
  variant = "gray",
  className,
  title,
  children,
}: BadgeProps) {
  return (
    <span
      className={clsx(
        "inline-flex items-center rounded-full px-2 py-1 text-xs font-medium",
        "ring-1 ring-inset",
        variants[variant],
        className,
      )}
      title={title}
    >
      {children}
    </span>
  );
}
