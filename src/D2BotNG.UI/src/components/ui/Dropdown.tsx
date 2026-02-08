import { type ReactNode, type ElementType } from "react";
import { Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import { EllipsisVerticalIcon, CheckIcon } from "@heroicons/react/24/outline";
import clsx from "clsx";

export interface DropdownItem {
  label: string;
  onClick: () => void;
  icon?: ElementType;
  danger?: boolean;
  disabled?: boolean;
  checked?: boolean;
}

export interface DropdownProps {
  items: DropdownItem[];
  trigger?: ReactNode;
  className?: string;
}

export function Dropdown({ items, trigger, className }: DropdownProps) {
  return (
    <Menu
      as="div"
      className={clsx("relative inline-block text-left", className)}
    >
      <MenuButton
        className={clsx(
          "flex items-center rounded-lg p-1.5 text-zinc-400",
          "hover:bg-zinc-800 hover:text-zinc-100",
          "focus:outline-none focus:ring-2 focus:ring-d2-gold focus:ring-offset-2 focus:ring-offset-zinc-950",
          "transition-colors",
        )}
      >
        {trigger || (
          <>
            <span className="sr-only">Open options</span>
            <EllipsisVerticalIcon className="h-5 w-5" aria-hidden="true" />
          </>
        )}
      </MenuButton>

      <MenuItems
        anchor="bottom end"
        transition
        className={clsx(
          "z-50 w-48 rounded-lg",
          "bg-zinc-800 ring-1 ring-zinc-700 shadow-lg",
          "focus:outline-none",
          "[--anchor-gap:4px]",
          "transition duration-100 ease-out data-[closed]:scale-95 data-[closed]:opacity-0",
        )}
      >
        <div className="py-1">
          {items.map((item, index) => {
            const isCheckbox = item.checked !== undefined;
            return (
              <MenuItem key={index} disabled={item.disabled}>
                {({ focus, close }) => (
                  <button
                    type="button"
                    onClick={(e) => {
                      if (isCheckbox) {
                        e.preventDefault();
                        item.onClick();
                      } else {
                        item.onClick();
                        close();
                      }
                    }}
                    disabled={item.disabled}
                    className={clsx(
                      "flex w-full items-center gap-2 px-4 py-2 text-sm",
                      "disabled:pointer-events-none disabled:opacity-50",
                      "transition-colors",
                      item.danger
                        ? focus
                          ? "bg-red-600 text-white"
                          : "text-red-400"
                        : focus
                          ? "bg-zinc-700 text-zinc-100"
                          : "text-zinc-300",
                    )}
                  >
                    {isCheckbox && (
                      <span className="flex h-4 w-4 items-center justify-center">
                        {item.checked && (
                          <CheckIcon
                            className="h-4 w-4 text-d2-gold"
                            aria-hidden="true"
                          />
                        )}
                      </span>
                    )}
                    {item.icon && (
                      <item.icon className="h-4 w-4" aria-hidden="true" />
                    )}
                    {item.label}
                  </button>
                )}
              </MenuItem>
            );
          })}
        </div>
      </MenuItems>
    </Menu>
  );
}
