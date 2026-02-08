import { Bars3Icon } from "@heroicons/react/24/outline";

interface HeaderProps {
  onMenuClick: () => void;
}

export function Header({ onMenuClick }: HeaderProps) {
  return (
    <div className="sticky top-0 z-40 flex items-center gap-x-6 bg-zinc-900 px-4 py-4 shadow-sm ring-1 ring-zinc-800 sm:px-6 lg:hidden">
      <button
        type="button"
        onClick={onMenuClick}
        className="-m-2.5 p-2.5 text-zinc-400 hover:text-zinc-100"
      >
        <span className="sr-only">Open sidebar</span>
        <Bars3Icon className="h-6 w-6" aria-hidden="true" />
      </button>
      <div className="flex flex-1 items-center">
        <img
          src="/assets/logo-horizontal.png"
          alt="D2BotNG"
          className="h-8 w-auto"
        />
      </div>
    </div>
  );
}
