import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAppData } from "../AppData";
import { NeighborList } from "../components/NeighborList";
import { Sheet } from "../components/Sheet";
import { useToast } from "../components/Toast";
import { api, errorMessage } from "../lib/api";
import type { Neighbor, NeighborRating, PlantSpeciesDetail, PlantSpeciesInput } from "../lib/types";

export function SpeciesPage() {
  const id = Number(useParams().id);
  const navigate = useNavigate();
  const toast = useToast();
  const { reloadSpecies, reloadObjects } = useAppData();
  const [detail, setDetail] = useState<PlantSpeciesDetail | null>(null);
  const [editing, setEditing] = useState(false);
  const [relation, setRelation] = useState<Neighbor | "new" | null>(null);

  const load = useCallback(() => {
    api.speciesDetail(id).then(setDetail, (e) => toast.error(errorMessage(e)));
  }, [id, toast]);

  useEffect(load, [load]);

  async function remove() {
    if (!detail) return;
    const usage = detail.objectCount > 0 ? `\n${detail.objectCount} Objekt(e) verlieren dabei ihre Art-Zuordnung.` : "";
    if (!window.confirm(`Pflanzenart „${detail.species.name}“ samt ihrer Beziehungen löschen?${usage}`)) return;
    try {
      await api.deleteSpecies(id);
      await Promise.all([reloadSpecies(), reloadObjects()]);
      navigate("/arten", { replace: true });
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  if (!detail) return <div className="page muted">Lädt …</div>;
  const { species, neighbors } = detail;

  return (
    <div className="page">
      <header className="page-header">
        <Link to="/arten" className="back" aria-label="Zurück">
          ←
        </Link>
        <h1>{species.name}</h1>
      </header>

      {editing ? (
        <SpeciesForm
          initial={species}
          onSaved={() => {
            setEditing(false);
            load();
            reloadSpecies();
            reloadObjects();
          }}
          onCancel={() => setEditing(false)}
        />
      ) : (
        <div className="detail">
          {species.scientificName && <p className="muted scientific">{species.scientificName}</p>}
          {species.notes && <p className="notes">{species.notes}</p>}
          {detail.objectCount > 0 && <p className="muted">Im Garten: {detail.objectCount} Objekt(e)</p>}
          <div className="actions wrap">
            {species.externalUrl && (
              <a className="button" href={species.externalUrl} target="_blank" rel="noopener noreferrer">
                Externe Info öffnen ↗
              </a>
            )}
            <button type="button" onClick={() => setEditing(true)}>
              Bearbeiten
            </button>
            <button type="button" className="danger" onClick={remove}>
              Löschen
            </button>
          </div>
        </div>
      )}

      <section>
        <div className="section-header">
          <h2>Beziehungen zu anderen Arten</h2>
          <button type="button" className="primary" onClick={() => setRelation("new")}>
            + Beziehung
          </button>
        </div>
        <NeighborList neighbors={neighbors} onEdit={setRelation} />
      </section>

      {relation && (
        <Sheet title={relation === "new" ? "Neue Beziehung" : `Beziehung zu ${relation.speciesName}`} onClose={() => setRelation(null)}>
          <RelationForm
            speciesId={id}
            existing={relation === "new" ? undefined : relation}
            excluded={neighbors.map((n) => n.speciesId)}
            onDone={() => {
              setRelation(null);
              load();
            }}
          />
        </Sheet>
      )}
    </div>
  );
}

function SpeciesForm({ initial, onSaved, onCancel }: { initial: PlantSpeciesInput & { id: number }; onSaved: () => void; onCancel: () => void }) {
  const toast = useToast();
  const [form, setForm] = useState<PlantSpeciesInput>(initial);
  const set = (key: keyof PlantSpeciesInput) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value || null });

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      await api.updateSpecies(initial.id, form);
      onSaved();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        <span>Name</span>
        <input value={form.name ?? ""} onChange={set("name")} required />
      </label>
      <label className="field">
        <span>Wissenschaftlicher Name</span>
        <input value={form.scientificName ?? ""} onChange={set("scientificName")} />
      </label>
      <label className="field">
        <span>Externer Link (z. B. NaturaDB)</span>
        <input type="url" value={form.externalUrl ?? ""} onChange={set("externalUrl")} placeholder="https://…" />
      </label>
      <label className="field">
        <span>Notizen</span>
        <textarea value={form.notes ?? ""} onChange={set("notes")} rows={4} />
      </label>
      <div className="actions">
        <button type="button" onClick={onCancel}>
          Abbrechen
        </button>
        <button type="submit" className="primary">
          Speichern
        </button>
      </div>
    </form>
  );
}

interface RelationFormProps {
  speciesId: number;
  existing?: Neighbor;
  excluded: number[];
  onDone: () => void;
}

function RelationForm({ speciesId, existing, excluded, onDone }: RelationFormProps) {
  const { species } = useAppData();
  const toast = useToast();
  const [otherSpeciesId, setOtherSpeciesId] = useState<number | null>(existing?.speciesId ?? null);
  const [rating, setRating] = useState<NeighborRating>(existing?.rating ?? "Good");
  const [note, setNote] = useState(existing?.note ?? "");
  const [source, setSource] = useState(existing?.source ?? "");
  const candidates = species.filter((s) => s.id !== speciesId && (s.id === existing?.speciesId || !excluded.includes(s.id)));

  async function submit(event: FormEvent) {
    event.preventDefault();
    const input = { speciesId, otherSpeciesId: otherSpeciesId!, rating, note: note || null, source: source || null };
    try {
      if (existing) await api.updateRelation(existing.relationId, input);
      else await api.createRelation(input);
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function remove() {
    if (!existing || !window.confirm("Beziehung löschen?")) return;
    try {
      await api.deleteRelation(existing.relationId);
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  if (candidates.length === 0)
    return <p className="muted">Lege zuerst weitere Pflanzenarten an, um Beziehungen einzutragen.</p>;

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        <span>Andere Art</span>
        <select value={otherSpeciesId ?? ""} onChange={(e) => setOtherSpeciesId(Number(e.target.value) || null)} required>
          <option value="">– bitte wählen –</option>
          {candidates.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>
      </label>
      <fieldset className="segmented">
        <legend>Bewertung</legend>
        <label className={rating === "Good" ? "on good" : ""}>
          <input type="radio" name="rating" checked={rating === "Good"} onChange={() => setRating("Good")} />
          👍 gut
        </label>
        <label className={rating === "Bad" ? "on bad" : ""}>
          <input type="radio" name="rating" checked={rating === "Bad"} onChange={() => setRating("Bad")} />
          👎 schlecht
        </label>
      </fieldset>
      <label className="field">
        <span>Notiz</span>
        <input value={note} onChange={(e) => setNote(e.target.value)} />
      </label>
      <label className="field">
        <span>Quelle (Text oder URL)</span>
        <input value={source} onChange={(e) => setSource(e.target.value)} placeholder="z. B. NaturaDB-Artikel Mischkultur im Hochbeet" />
      </label>
      <div className="actions">
        {existing && (
          <button type="button" className="danger" onClick={remove}>
            Löschen
          </button>
        )}
        <button type="submit" className="primary">
          Speichern
        </button>
      </div>
    </form>
  );
}
