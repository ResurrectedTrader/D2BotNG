import { useRef, useState } from "react";
import { Outlet, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { MobileSidebar } from "./MobileSidebar";
import { Header } from "./Header";
import { ConsolePanel } from "./ConsolePanel";
import { ScrollToTop } from "./ScrollToTop";

export function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const scrollRef = useRef<HTMLElement>(null);
  const location = useLocation();

  // Only show console panel on the profiles list page (exact match)
  const showConsolePanel = location.pathname === "/profiles";

  return (
    <>
      {/* Mobile sidebar */}
      <MobileSidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      {/* Desktop sidebar */}
      <Sidebar />

      {/* Main content area.
          `h-full` — 100% of #root — rather than `h-screen`: 100vh is the VIEWPORT, and in the
          WebView2 host that is taller than the client area the page actually gets (the window
          keeps a strip for its own controls, which is what `pt-8` leaves room for). Sizing the
          shell to the viewport made it overflow its container by that strip, so the document
          scrolled as well as `main` — two scrollbars, the outer one revealing bare background. */}
      <div className="lg:pl-64 flex h-full flex-col pt-8">
        {/* Mobile header */}
        <Header onMenuClick={() => setSidebarOpen(true)} />

        {/* Page content - scrollable container for sticky headers */}
        <main
          ref={scrollRef}
          className={`flex-1 overflow-y-auto ${showConsolePanel ? "pb-64" : "pb-6"}`}
        >
          <div className="px-4 sm:px-6 lg:px-8">
            <Outlet />
          </div>
        </main>
      </div>

      {/* Clear of the console panel where there is one, and one step up from the corner otherwise,
          so it stacks above the hint the character views put there rather than covering it. */}
      <ScrollToTop
        container={scrollRef}
        className={
          showConsolePanel ? "bottom-[17rem] right-4" : "bottom-9 right-4"
        }
      />

      {/* Global console panel - only on profiles list */}
      {showConsolePanel && <ConsolePanel />}
    </>
  );
}
