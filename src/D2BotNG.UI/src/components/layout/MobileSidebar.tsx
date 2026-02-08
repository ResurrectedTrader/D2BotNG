import { useState } from "react";
import {
  Dialog,
  DialogBackdrop,
  DialogPanel,
  TransitionChild,
} from "@headlessui/react";
import { XMarkIcon, InformationCircleIcon } from "@heroicons/react/24/outline";
import { NavLink } from "react-router-dom";
import clsx from "clsx";
import { navigation } from "./Sidebar";
import { AboutDialog } from "../AboutDialog";

interface MobileSidebarProps {
  open: boolean;
  onClose: () => void;
}

export function MobileSidebar({ open, onClose }: MobileSidebarProps) {
  const [aboutOpen, setAboutOpen] = useState(false);

  return (
    <>
      <Dialog open={open} onClose={onClose} className="relative z-50 lg:hidden">
        <DialogBackdrop
          transition
          className="fixed inset-0 bg-zinc-950/80 transition-opacity duration-300 ease-linear data-[closed]:opacity-0"
        />

        <div className="fixed inset-0 flex">
          <DialogPanel
            transition
            className="relative mr-16 flex w-full max-w-xs flex-1 transform transition duration-300 ease-in-out data-[closed]:-translate-x-full"
          >
            {/* Close button */}
            <TransitionChild>
              <div className="absolute left-full top-0 flex w-16 justify-center pt-5 duration-300 ease-in-out data-[closed]:opacity-0">
                <button
                  type="button"
                  onClick={onClose}
                  className="-m-2.5 p-2.5"
                >
                  <span className="sr-only">Close sidebar</span>
                  <XMarkIcon
                    className="h-6 w-6 text-white"
                    aria-hidden="true"
                  />
                </button>
              </div>
            </TransitionChild>

            {/* Sidebar content */}
            <div className="flex grow flex-col gap-y-5 overflow-y-auto bg-zinc-900 px-6 pb-4 ring-1 ring-zinc-800">
              {/* Logo */}
              <div className="flex h-52 shrink-0 items-center justify-center pt-4">
                <img
                  src="/assets/logo.png"
                  alt="D2BotNG"
                  className="h-48 w-auto"
                />
              </div>

              {/* Navigation */}
              <nav className="flex flex-1 flex-col">
                <ul role="list" className="flex flex-1 flex-col gap-y-7">
                  <li>
                    <ul role="list" className="-mx-2 space-y-1">
                      {navigation.map((item) => (
                        <li key={item.name}>
                          <NavLink
                            to={item.href}
                            onClick={onClose}
                            className={({ isActive }) =>
                              clsx(
                                "group flex gap-x-3 rounded-md p-2 text-sm font-semibold leading-6",
                                isActive
                                  ? "bg-zinc-800 text-d2-gold"
                                  : "text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100",
                              )
                            }
                          >
                            {({ isActive }) => (
                              <>
                                <item.icon
                                  className={clsx(
                                    "h-6 w-6 shrink-0",
                                    isActive
                                      ? "text-d2-gold"
                                      : "text-zinc-400 group-hover:text-zinc-100",
                                  )}
                                  aria-hidden="true"
                                />
                                {item.name}
                              </>
                            )}
                          </NavLink>
                        </li>
                      ))}
                    </ul>
                  </li>

                  {/* Footer with About link */}
                  <li className="mt-auto">
                    <button
                      onClick={() => {
                        onClose();
                        setAboutOpen(true);
                      }}
                      className="group -mx-2 flex w-full gap-x-3 rounded-md p-2 text-sm font-semibold leading-6 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100"
                    >
                      <InformationCircleIcon
                        className="h-6 w-6 shrink-0 text-zinc-400 group-hover:text-zinc-100"
                        aria-hidden="true"
                      />
                      About
                    </button>
                  </li>
                </ul>
              </nav>
            </div>
          </DialogPanel>
        </div>
      </Dialog>

      {/* About Dialog */}
      <AboutDialog open={aboutOpen} onClose={() => setAboutOpen(false)} />
    </>
  );
}
