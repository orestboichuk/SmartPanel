import { useEffect, useState } from "react";
import { Navigate, NavLink, Route, Routes } from "react-router-dom";
import { api } from "./api";
import DashboardPage from "./pages/DashboardPage";
import RoomsPage from "./pages/RoomsPage";
import CamerasPage from "./pages/CamerasPage";
import ScenesPage from "./pages/ScenesPage";
import TvPage from "./pages/TvPage";
import AdminLoginPage from "./pages/admin/AdminLoginPage";
import AdminDevicesPage from "./pages/admin/AdminDevicesPage";
import AdminLayoutsPage from "./pages/admin/AdminLayoutsPage";
import AdminUsersPage from "./pages/admin/AdminUsersPage";

export default function App() {
  const [theme, setTheme] = useState(() => localStorage.getItem("smartpanel-react-theme") || "light");

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem("smartpanel-react-theme", theme);
  }, [theme]);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-top">
          <h1 className="brand">SmartPanel React</h1>
          <button
            type="button"
            className="btn theme-btn"
            onClick={() => setTheme((t) => (t === "light" ? "dark" : "light"))}>
            {theme === "light" ? "Dark Theme" : "Light Theme"}
          </button>
        </div>
        <nav className="nav">
          <NavItem to="/panel" label="Panel Dashboard" end />
          <NavItem to="/panel/rooms" label="Panel Rooms" />
          <NavItem to="/panel/cameras" label="Panel Cameras" />
          <NavItem to="/panel/tv" label="Panel TVs" />
          <NavItem to="/panel/scenes" label="Panel Scenes" />
          <NavItem to="/admin/login" label="Admin Login" />
          <NavItem to="/admin/devices" label="Admin Devices" />
          <NavItem to="/admin/layouts" label="Admin Layouts" />
          <NavItem to="/admin/users" label="Admin Users" />
        </nav>
      </aside>

      <main className="content">
        <Routes>
          <Route path="/" element={<Navigate to="/panel" replace />} />
          <Route path="/panel" element={<DashboardPage />} />
          <Route path="/panel/rooms" element={<RoomsPage />} />
          <Route path="/panel/cameras" element={<CamerasPage />} />
          <Route path="/panel/tv" element={<TvPage />} />
          <Route path="/panel/scenes" element={<ScenesPage />} />

          <Route path="/admin/login" element={<AdminLoginPage />} />
          <Route path="/admin/devices" element={<RequireAdmin><AdminDevicesPage /></RequireAdmin>} />
          <Route path="/admin/layouts" element={<RequireAdmin><AdminLayoutsPage /></RequireAdmin>} />
          <Route path="/admin/users" element={<RequireAdmin><AdminUsersPage /></RequireAdmin>} />

          <Route path="*" element={<Navigate to="/panel" replace />} />
        </Routes>
      </main>
    </div>
  );
}

function NavItem({ to, label, end = false }) {
  return (
    <NavLink to={to} end={end} className={({ isActive }) => `nav-link ${isActive ? "nav-link-active" : ""}`}>
      {label}
    </NavLink>
  );
}

function RequireAdmin({ children }) {
  const token = api.getAuthToken();
  if (!token) {
    return <Navigate to="/admin/login" replace />;
  }

  return children;
}
