import { type ReactNode } from "react";
import clsx from "clsx";

export interface CardProps {
  className?: string;
  children: ReactNode;
}

export function Card({ className, children }: CardProps) {
  return (
    <div
      className={clsx("rounded-xl bg-zinc-900 ring-1 ring-zinc-800", className)}
    >
      {children}
    </div>
  );
}

export interface CardHeaderProps {
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
}

export function CardHeader({
  title,
  description,
  action,
  className,
}: CardHeaderProps) {
  return (
    <div
      className={clsx(
        "flex items-start justify-between border-b border-zinc-800 px-4 py-3",
        className,
      )}
    >
      <div>
        <h3 className="text-lg font-semibold text-zinc-100">{title}</h3>
        {description && (
          <p className="mt-1 text-sm text-zinc-400">{description}</p>
        )}
      </div>
      {action && <div className="ml-4 flex-shrink-0">{action}</div>}
    </div>
  );
}

export interface CardContentProps {
  className?: string;
  children: ReactNode;
}

export function CardContent({ className, children }: CardContentProps) {
  return <div className={clsx("p-4", className)}>{children}</div>;
}

export interface CardFooterProps {
  className?: string;
  children: ReactNode;
}

export function CardFooter({ className, children }: CardFooterProps) {
  return (
    <div
      className={clsx(
        "flex items-center justify-end gap-3 border-t border-zinc-800 px-4 py-3",
        className,
      )}
    >
      {children}
    </div>
  );
}
