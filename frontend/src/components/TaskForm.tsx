import { useState, type FormEvent } from "react";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import { MONTHS, toIsoDate } from "../lib/dates";
import { GEOMETRY_LABELS } from "../lib/geo";
import type { Frequency, GardenTask, Geometry } from "../lib/types";
import { useToast } from "./Toast";

interface TaskFormProps {
  initial?: GardenTask;
  objectId?: number | null;
  geometry?: Geometry | null;
  onSaved: (task: GardenTask) => void;
  onCancel: () => void;
}

const FREQUENCIES: { value: Frequency; label: string }[] = [
  { value: "None", label: "Einmalig" },
  { value: "Daily", label: "Täglich" },
  { value: "Weekly", label: "Wöchentlich" },
  { value: "Monthly", label: "Monatlich" },
  { value: "Yearly", label: "Jährlich" },
];

const UNIT_LABEL: Record<Frequency, string> = { None: "", Daily: "Tag(e)", Weekly: "Woche(n)", Monthly: "Monat(e)", Yearly: "Jahr(e)" };

export function TaskForm({ initial, objectId, geometry, onSaved, onCancel }: TaskFormProps) {
  const { objects, typeById, reloadTasks, config } = useAppData();
  const toast = useToast();
  const freeGeometry = geometry ?? initial?.geometry ?? null;
  const [title, setTitle] = useState(initial?.title ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [selectedObjectId, setSelectedObjectId] = useState<number | null>(initial?.objectId ?? objectId ?? null);
  const [dueDate, setDueDate] = useState(initial?.dueDate ?? toIsoDate(new Date()));
  const [frequency, setFrequency] = useState<Frequency>(initial?.frequency ?? "None");
  const [interval, setRepeatInterval] = useState(initial?.interval ?? 1);
  const [useSeason, setUseSeason] = useState(Boolean(initial?.seasonStartMonth));
  const [seasonStart, setSeasonStart] = useState(initial?.seasonStartMonth ?? 3);
  const [seasonEnd, setSeasonEnd] = useState(initial?.seasonEndMonth ?? 5);
  const [notify, setNotify] = useState(initial?.notify ?? config.pushEnabled);
  const [leadDays, setLeadDays] = useState(initial?.leadDays ?? 0);
  const [saving, setSaving] = useState(false);

  const hasFreeLocation = freeGeometry !== null && selectedObjectId === null;
  const canChooseObject = !freeGeometry;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    const recurring = frequency !== "None";
    const input = {
      title,
      description: description || null,
      objectId: selectedObjectId,
      geometry: selectedObjectId === null ? freeGeometry : null,
      dueDate,
      notify,
      leadDays,
      frequency,
      interval: recurring ? interval : 1,
      seasonStartMonth: recurring && useSeason && frequency !== "Yearly" ? seasonStart : null,
      seasonEndMonth: recurring && useSeason && frequency !== "Yearly" ? seasonEnd : null,
    };
    try {
      const saved = initial ? await api.updateTask(initial.id, input) : await api.createTask(input);
      await reloadTasks();
      onSaved(saved);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        <span>Titel</span>
        <input value={title} onChange={(e) => setTitle(e.target.value)} required autoFocus={!initial} placeholder="z. B. Tomaten gießen" />
      </label>

      {hasFreeLocation ? (
        <p className="hint">📍 Ort: {GEOMETRY_LABELS[freeGeometry!.type]} auf der Karte</p>
      ) : (
        canChooseObject && (
          <label className="field">
            <span>Objekt</span>
            <select value={selectedObjectId ?? ""} onChange={(e) => setSelectedObjectId(e.target.value ? Number(e.target.value) : null)} required>
              <option value="">– bitte wählen –</option>
              {objects.map((o) => (
                <option key={o.id} value={o.id}>
                  {typeById.get(o.objectTypeId)?.icon} {o.name}
                </option>
              ))}
            </select>
          </label>
        )
      )}

      <label className="field">
        <span>Fällig am</span>
        <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} required />
      </label>

      <label className="field">
        <span>Wiederholung</span>
        <select value={frequency} onChange={(e) => setFrequency(e.target.value as Frequency)}>
          {FREQUENCIES.map((f) => (
            <option key={f.value} value={f.value}>
              {f.label}
            </option>
          ))}
        </select>
      </label>

      {frequency !== "None" && (
        <>
          <label className="field">
            <span>Alle</span>
            <div className="row">
              <input type="number" min={1} max={365} value={interval} onChange={(e) => setRepeatInterval(Number(e.target.value))} />
              <span>{UNIT_LABEL[frequency]}</span>
            </div>
          </label>
          {frequency !== "Yearly" && (
            <>
              <label className="check">
                <input type="checkbox" checked={useSeason} onChange={(e) => setUseSeason(e.target.checked)} />
                Nur in einem Saisonfenster
              </label>
              {useSeason && (
                <div className="row">
                  <select value={seasonStart} onChange={(e) => setSeasonStart(Number(e.target.value))} aria-label="Saison von">
                    {MONTHS.map((m, i) => (
                      <option key={m} value={i + 1}>
                        {m}
                      </option>
                    ))}
                  </select>
                  <span>bis</span>
                  <select value={seasonEnd} onChange={(e) => setSeasonEnd(Number(e.target.value))} aria-label="Saison bis">
                    {MONTHS.map((m, i) => (
                      <option key={m} value={i + 1}>
                        {m}
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </>
          )}
        </>
      )}

      <label className="check">
        <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} />
        Push-Benachrichtigung
      </label>
      {notify && (
        <label className="field">
          <span>Tage vorher erinnern</span>
          <input type="number" min={0} max={365} value={leadDays} onChange={(e) => setLeadDays(Number(e.target.value))} />
        </label>
      )}

      <label className="field">
        <span>Beschreibung</span>
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
      </label>

      <div className="actions">
        <button type="button" onClick={onCancel}>
          Abbrechen
        </button>
        <button type="submit" className="primary" disabled={saving}>
          Speichern
        </button>
      </div>
    </form>
  );
}
