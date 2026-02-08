import { useState } from "react";
import { NavLink } from "react-router-dom";
import {
  UserGroupIcon,
  KeyIcon,
  ClockIcon,
  UserIcon,
  Cog6ToothIcon,
  InformationCircleIcon,
} from "@heroicons/react/24/outline";
import clsx from "clsx";
import { AboutDialog } from "../AboutDialog";

const navigation = [
  { name: "Profiles", href: "/profiles", icon: UserGroupIcon },
  { name: "Keys", href: "/keys", icon: KeyIcon },
  { name: "Schedules", href: "/schedules", icon: ClockIcon },
  { name: "Characters", href: "/characters", icon: UserIcon },
  { name: "Settings", href: "/settings", icon: Cog6ToothIcon },
];

export function Sidebar() {
  const [aboutOpen, setAboutOpen] = useState(false);

  return (
    <>
      <div className="hidden lg:fixed lg:inset-y-0 lg:z-50 lg:flex lg:w-64 lg:flex-col">
        <div className="flex grow flex-col gap-y-5 overflow-y-auto bg-zinc-900 px-6 pb-4 ring-1 ring-zinc-800">
          {/* Logo */}
          <div className="flex h-52 shrink-0 items-center justify-center pt-4">
            <img src="/assets/logo.png" alt="D2BotNG" className="h-48 w-auto" />
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
                  onClick={() => setAboutOpen(true)}
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
      </div>

      {/* About Dialog */}
      <AboutDialog open={aboutOpen} onClose={() => setAboutOpen(false)} />
    </>
  );
}

export { navigation };
