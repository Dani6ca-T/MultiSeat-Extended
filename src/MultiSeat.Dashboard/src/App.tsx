import { BrowserRouter, Routes, Route, NavLink } from "react-router-dom";
import { SeatsPage } from "./pages/SeatsPage";
import { AccountsPage } from "./pages/AccountsPage";
import { SystemPage } from "./pages/SystemPage";
import { InputPage } from "./pages/InputPage";
import { SettingsPage } from "./pages/SettingsPage";

export default function App() {
  return (
    <BrowserRouter>
      <div className="app-layout">
        <nav className="sidebar">
          <div className="sidebar-brand">
            <span className="brand-icon">T</span>
            <span className="brand-text">MultiSeat</span>
          </div>

          <div className="nav-links">
            <NavLink to="/" end>
              Seats
            </NavLink>
            <NavLink to="/input">
              Input
            </NavLink>
            <NavLink to="/accounts">
              Accounts
            </NavLink>
            <NavLink to="/system">
              System
            </NavLink>
            <NavLink to="/settings">
              Settings
            </NavLink>
          </div>
        </nav>

        <main className="main-content">
          <Routes>
            <Route path="/" element={<SeatsPage />} />
            <Route path="/input" element={<InputPage />} />
            <Route path="/accounts" element={<AccountsPage />} />
            <Route path="/system" element={<SystemPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
