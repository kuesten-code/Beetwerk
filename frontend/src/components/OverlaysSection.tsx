import { Link } from "react-router-dom";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import type { MapOverlay } from "../lib/types";
import { useToast } from "./Toast";

/** Verwaltung der eigenen Luftbilder und Pläne. Hinzufügen geht auf der Karte, weil dort die Ansicht für die Platzierung steht. */
export function OverlaysSection() {
  const { overlays, setOverlays, reloadOverlays } = useAppData();
  const toast = useToast();

  async function save(overlay: MapOverlay) {
    setOverlays(overlays.map((o) => (o.id === overlay.id ? overlay : o)));
    try {
      await api.updateOverlay(overlay);
    } catch (e) {
      toast.error(errorMessage(e));
      await reloadOverlays();
    }
  }

  async function remove(overlay: MapOverlay) {
    if (!window.confirm(`„${overlay.name}“ löschen?`)) return;
    try {
      await api.deleteOverlay(overlay.id);
      await reloadOverlays();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  return (
    <section>
      <h2>Eigene Luftbilder &amp; Pläne</h2>
      {overlays.length === 0 ? (
        <p className="hint">
          Noch keine eigenen Bilder. Auf der <Link to="/">Karte</Link> über ☰ → „Bild hinzufügen …“ eine Drohnenaufnahme oder einen Gartenplan einfügen.
        </p>
      ) : (
        <ul className="user-list">
          {overlays.map((overlay) => (
            <li key={overlay.id} className="overlay-item">
              <img src={overlay.imageUrl} alt="" className="overlay-preview" />
              <div className="overlay-controls">
                <input
                  defaultValue={overlay.name}
                  aria-label="Name"
                  onBlur={(e) => e.target.value.trim() && e.target.value !== overlay.name && save({ ...overlay, name: e.target.value.trim() })}
                />
                <label className="opacity-row">
                  <span>Deckkraft</span>
                  <input
                    type="range"
                    min={0.2}
                    max={1}
                    step={0.05}
                    defaultValue={overlay.opacity}
                    // Erst beim Loslassen speichern, nicht bei jedem Zwischenwert während des Ziehens.
                    onPointerUp={(e) => save({ ...overlay, opacity: Number(e.currentTarget.value) })}
                    onKeyUp={(e) => save({ ...overlay, opacity: Number(e.currentTarget.value) })}
                  />
                </label>
                <div className="row wrap-row">
                  <label className="check">
                    <input type="checkbox" checked={overlay.visible} onChange={(e) => save({ ...overlay, visible: e.target.checked })} />
                    Anzeigen
                  </label>
                  <Link className="button" to={`/?luftbild=${overlay.id}`}>
                    Ausrichten
                  </Link>
                  <button type="button" className="danger" onClick={() => remove(overlay)}>
                    Löschen
                  </button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
