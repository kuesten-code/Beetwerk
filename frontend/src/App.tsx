import { useEffect } from "react";
import { BrowserRouter, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { AppDataProvider, useAppData } from "./AppData";
import { ToastProvider } from "./components/Toast";
import { registerServiceWorker, syncSubscription } from "./lib/push";
import { MapPage } from "./pages/MapPage";
import { SettingsPage } from "./pages/SettingsPage";
import { SpeciesListPage } from "./pages/SpeciesListPage";
import { SpeciesPage } from "./pages/SpeciesPage";
import { TaskPage } from "./pages/TaskPage";
import { TasksPage } from "./pages/TasksPage";
import { toIsoDate } from "./lib/dates";

function Shell() {
  const location = useLocation();
  const navigate = useNavigate();
  const { config, openTasks } = useAppData();
  const isMap = location.pathname === "/";
  const today = toIsoDate(new Date());
  const dueCount = openTasks.filter((t) => t.dueDate <= today).length;

  useEffect(() => {
    registerServiceWorker().then(() => {
      if (config.pushEnabled && config.vapidPublicKey) syncSubscription(config.vapidPublicKey).catch(() => undefined);
    });
    // Klick auf eine Push-Nachricht bei bereits geöffneter App: der Service Worker meldet das Ziel.
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type === "navigate" && typeof event.data.url === "string") navigate(event.data.url);
    };
    navigator.serviceWorker?.addEventListener("message", onMessage);
    return () => navigator.serviceWorker?.removeEventListener("message", onMessage);
  }, [config, navigate]);

  return (
    <div className="shell">
      <main className="content">
        {/* Die Karte bleibt beim Tabwechsel erhalten, damit Kacheln und Ansicht nicht neu geladen werden. */}
        <MapPage active={isMap} />
        {!isMap && (
          <Routes>
            <Route path="/aufgaben" element={<TasksPage />} />
            <Route path="/aufgaben/:id" element={<TaskPage />} />
            <Route path="/arten" element={<SpeciesListPage />} />
            <Route path="/arten/:id" element={<SpeciesPage />} />
            <Route path="/einstellungen" element={<SettingsPage />} />
            <Route path="*" element={<div className="page empty">Seite nicht gefunden.</div>} />
          </Routes>
        )}
      </main>
      <nav className="tabbar" aria-label="Hauptnavigation">
        <NavLink to="/" end>
          <span aria-hidden>🗺️</span>Karte
        </NavLink>
        <NavLink to="/aufgaben">
          <span aria-hidden>✅</span>Aufgaben{dueCount > 0 && <b className="badge">{dueCount}</b>}
        </NavLink>
        <NavLink to="/arten">
          <span aria-hidden>🌱</span>Arten
        </NavLink>
        <NavLink to="/einstellungen">
          <span aria-hidden>⚙️</span>Mehr
        </NavLink>
      </nav>
    </div>
  );
}

export function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <AppDataProvider>
          <Shell />
        </AppDataProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}
