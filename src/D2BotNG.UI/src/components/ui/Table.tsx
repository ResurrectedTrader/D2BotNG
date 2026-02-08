import { type ReactNode, type HTMLAttributes } from "react";
import clsx from "clsx";

export interface TableProps extends HTMLAttributes<HTMLTableElement> {
  children: ReactNode;
}

export function Table({ className, children, ...props }: TableProps) {
  return (
    <div className="overflow-x-auto">
      <table
        className={clsx("min-w-full divide-y divide-zinc-800", className)}
        {...props}
      >
        {children}
      </table>
    </div>
  );
}

export interface TableHeadProps extends HTMLAttributes<HTMLTableSectionElement> {
  children: ReactNode;
}

export function TableHead({ className, children, ...props }: TableHeadProps) {
  return (
    <thead
      className={clsx("bg-zinc-900 sticky top-0 z-10", className)}
      {...props}
    >
      {children}
    </thead>
  );
}

export interface TableBodyProps extends HTMLAttributes<HTMLTableSectionElement> {
  children: ReactNode;
}

export function TableBody({ className, children, ...props }: TableBodyProps) {
  return (
    <tbody
      className={clsx("divide-y divide-zinc-800 bg-zinc-950", className)}
      {...props}
    >
      {children}
    </tbody>
  );
}

export interface TableRowProps extends HTMLAttributes<HTMLTableRowElement> {
  children: ReactNode;
}

export function TableRow({
  className,
  onClick,
  children,
  ...props
}: TableRowProps) {
  return (
    <tr
      className={clsx(
        onClick && "cursor-pointer hover:bg-zinc-900 transition-colors",
        className,
      )}
      onClick={onClick}
      {...props}
    >
      {children}
    </tr>
  );
}

export interface TableHeaderProps extends HTMLAttributes<HTMLTableCellElement> {
  children?: ReactNode;
}

export function TableHeader({
  className,
  children,
  ...props
}: TableHeaderProps) {
  return (
    <th
      scope="col"
      className={clsx(
        "px-3 py-1 text-left text-sm font-semibold text-zinc-300",
        className,
      )}
      {...props}
    >
      {children}
    </th>
  );
}

export interface TableCellProps extends HTMLAttributes<HTMLTableCellElement> {
  children?: ReactNode;
}

export function TableCell({ className, children, ...props }: TableCellProps) {
  return (
    <td
      className={clsx(
        "whitespace-nowrap px-3 py-1 text-sm text-zinc-400",
        className,
      )}
      {...props}
    >
      {children}
    </td>
  );
}
