import { useState } from "react";
import { Outlet, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { MobileSidebar } from "./MobileSidebar";
import { Header } from "./Header";
import { ConsolePanel } from "./ConsolePanel";

export function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const location = useLocation();

  // Only show console panel on the profiles list page (exact match)
  const showConsolePanel = location.pathname === "/profiles";

  return (
    <>
      {/* Mobile sidebar */}
      <MobileSidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      {/* Desktop sidebar */}
      <Sidebar />

      {/* Main content area */}
      <div className="lg:pl-64 flex flex-col h-screen pt-8">
        {/* Mobile header */}
        <Header onMenuClick={() => setSidebarOpen(true)} />

        {/* Page content - scrollable container for sticky headers */}
        <main className={`flex-1 overflow-y-auto ${showConsolePanel ? "pb-64" : "pb-6"}`}>
          <div className="px-4 sm:px-6 lg:px-8">
            <Outlet />
          </div>
        </main>
      </div>

      {/* Global console panel - only on profiles list */}
      {showConsolePanel && <ConsolePanel />}
    </>
  );
}
