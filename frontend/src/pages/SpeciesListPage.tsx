import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAppData } from "../AppData";
import { useToast } from "../components/Toast";
import { api, errorMessage } from "../lib/api";

export function SpeciesListPage() {
  const { species, reloadSpecies } = useAppData();
  const toast = useToast();
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [newName, setNewName] = useState("");

  const filtered = species.filter((s) =>
    `${s.name} ${s.scientificName ?? ""}`.toLowerCase().includes(query.trim().toLowerCase()),
  );

  async function create(event: FormEvent) {
    event.preventDefault();
    try {
      const created = await api.createSpecies({ name: newName, scientificName: null, notes: null, externalUrl: null });
      await reloadSpecies();
      setNewName("");
      navigate(`/arten/${created.id}`);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  return (
    <div className="page">
      <header className="page-header">
        <h1>Pflanzenarten</h1>
      </header>
      <form className="row" onSubmit={create}>
        <input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="Neue Art, z. B. Tomate" aria-label="Name der neuen Art" />
        <button type="submit" className="primary" disabled={!newName.trim()}>
          Anlegen
        </button>
      </form>
      {species.length > 8 && (
        <input type="search" className="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Suchen …" aria-label="Arten durchsuchen" />
      )}
      {species.length === 0 ? (
        <p className="empty">
          Noch keine Pflanzenarten. Arten und ihre guten oder schlechten Nachbarn trägst du selbst ein – Beetwerk liefert bewusst keine
          vorgefertigten Daten mit.
        </p>
      ) : (
        <ul className="link-list">
          {filtered.map((s) => (
            <li key={s.id}>
              <Link to={`/arten/${s.id}`}>
                <strong>{s.name}</strong>
                {s.scientificName && <small className="muted"> {s.scientificName}</small>}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
