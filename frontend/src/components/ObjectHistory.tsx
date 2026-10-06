import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, errorMessage } from "../lib/api";
import { formatDate, toIsoDate } from "../lib/dates";
import { PHOTO_MAX_EDGE, THUMBNAIL_MAX_EDGE, resizeImage } from "../lib/image";
import type { HistoryEntry, HistoryKind } from "../lib/types";
import { useToast } from "./Toast";

const ICONS: Record<HistoryKind, string> = { Note: "📝", Created: "🌱", TaskCompleted: "✅", Photo: "📷" };

/** Verlauf eines Objekts: Tagebuchnotizen, Fotos und automatisch erfasste Ereignisse. */
export function ObjectHistory({ objectId, refreshKey }: { objectId: number; refreshKey?: unknown }) {
  const toast = useToast();
  const [entries, setEntries] = useState<HistoryEntry[] | null>(null);
  const [note, setNote] = useState("");
  const [uploading, setUploading] = useState(false);
  const [viewing, setViewing] = useState<HistoryEntry | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(() => {
    api.history(objectId).then(setEntries, (e) => toast.error(errorMessage(e)));
  }, [objectId, toast]);

  useEffect(load, [load, refreshKey]);

  async function addNote(event: FormEvent) {
    event.preventDefault();
    try {
      await api.addNote(objectId, toIsoDate(new Date()), note);
      setNote("");
      load();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function addPhoto(file: File) {
    setUploading(true);
    try {
      const [image, thumbnail] = await Promise.all([resizeImage(file, PHOTO_MAX_EDGE), resizeImage(file, THUMBNAIL_MAX_EDGE)]);
      const takenOn = file.lastModified ? toIsoDate(new Date(file.lastModified)) : toIsoDate(new Date());
      await api.uploadPhoto(objectId, image.blob, thumbnail.blob, "", takenOn);
      load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setUploading(false);
    }
  }

  async function remove(entry: HistoryEntry) {
    const what = entry.kind === "Photo" ? "Foto" : "Eintrag";
    if (!window.confirm(`${what} löschen?`)) return;
    try {
      await api.deleteHistoryEntry(entry.id);
      setViewing(null);
      load();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  const photos = entries?.filter((e) => e.photo) ?? [];

  return (
    <div className="history">
      <form className="row" onSubmit={addNote}>
        <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="Notiz, z. B. „Erste Ernte 2 kg“" aria-label="Neue Notiz" />
        <button type="submit" className="primary" disabled={!note.trim()}>
          +
        </button>
        <button type="button" onClick={() => fileInput.current?.click()} disabled={uploading} aria-label="Foto hinzufügen">
          {uploading ? "…" : "📷"}
        </button>
      </form>
      <input
        ref={fileInput}
        type="file"
        accept="image/*"
        capture="environment"
        hidden
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) void addPhoto(file);
        }}
      />

      {photos.length > 0 && (
        <div className="photo-strip">
          {photos.map((entry) => (
            <button type="button" key={entry.id} className="photo-thumb" onClick={() => setViewing(entry)} aria-label={`Foto vom ${formatDate(entry.date)}`}>
              <img src={entry.photo!.thumbnailUrl} alt="" loading="lazy" />
            </button>
          ))}
        </div>
      )}

      {entries === null ? (
        <p className="muted">Lädt …</p>
      ) : (
        <ul className="timeline">
          {entries.map((entry) => (
            <li key={entry.id}>
              <span className="timeline-icon" aria-hidden>
                {ICONS[entry.kind]}
              </span>
              <span className="timeline-text">
                <small className="muted">
                  {formatDate(entry.date)}
                  {entry.createdBy && ` · ${entry.createdBy}`}
                </small>
                <span>{entry.kind === "TaskCompleted" ? `Erledigt: ${entry.text}` : entry.text}</span>
              </span>
              {(entry.kind === "Note" || entry.kind === "Photo") && (
                <button type="button" className="icon-button small" onClick={() => remove(entry)} aria-label="Eintrag löschen">
                  🗑️
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {viewing?.photo && (
        <div className="lightbox" role="dialog" aria-label="Foto" onClick={() => setViewing(null)}>
          <img src={viewing.photo.imageUrl} alt={viewing.text} />
          <div className="lightbox-bar" onClick={(e) => e.stopPropagation()}>
            <span>{formatDate(viewing.date)}</span>
            <button type="button" className="danger" onClick={() => remove(viewing)}>
              Löschen
            </button>
            <button type="button" onClick={() => setViewing(null)}>
              Schließen
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
