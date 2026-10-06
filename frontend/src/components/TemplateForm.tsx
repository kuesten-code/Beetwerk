import { useState, type FormEvent } from "react";
import { api, errorMessage } from "../lib/api";
import { MONTHS, describeRecurrence } from "../lib/dates";
import type { Frequency, TaskTemplate, TaskTemplateInput } from "../lib/types";
import { useToast } from "./Toast";

const FREQUENCIES: { value: Frequency; label: string }[] = [
  { value: "None", label: "Einmalig" },
  { value: "Daily", label: "Täglich" },
  { value: "Weekly", label: "Wöchentlich" },
  { value: "Monthly", label: "Monatlich" },
  { value: "Yearly", label: "Jährlich" },
];

export function describeTemplate(t: TaskTemplate): string {
  const start = t.startMonth ? `ab ${t.startDay}. ${MONTHS[t.startMonth - 1]}` : null;
  return [describeRecurrence(t) ?? "einmalig", start].filter(Boolean).join(" · ");
}

interface TemplateFormProps {
  speciesId: number;
  initial?: TaskTemplate;
  onDone: () => void;
}

export function TemplateForm({ speciesId, initial, onDone }: TemplateFormProps) {
  const toast = useToast();
  const [form, setForm] = useState<TaskTemplateInput>(
    initial ?? {
      title: "",
      description: null,
      frequency: "None",
      interval: 1,
      seasonStartMonth: null,
      seasonEndMonth: null,
      startMonth: null,
      startDay: 1,
      notify: true,
      leadDays: 0,
    },
  );
  const set = (patch: Partial<TaskTemplateInput>) => setForm({ ...form, ...patch });
  const recurring = form.frequency !== "None";
  const seasonAllowed = recurring && form.frequency !== "Yearly";

  async function submit(event: FormEvent) {
    event.preventDefault();
    const input: TaskTemplateInput = {
      ...form,
      seasonStartMonth: seasonAllowed ? form.seasonStartMonth : null,
      seasonEndMonth: seasonAllowed ? form.seasonEndMonth : null,
    };
    try {
      if (initial) await api.updateTemplate(initial.id, input);
      else await api.createTemplate(speciesId, input);
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function remove() {
    if (!initial || !window.confirm(`Vorlage „${initial.title}“ löschen? Bereits angelegte Aufgaben bleiben erhalten.`)) return;
    try {
      await api.deleteTemplate(initial.id);
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  const monthOptions = MONTHS.map((m, i) => (
    <option key={m} value={i + 1}>
      {m}
    </option>
  ));

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        <span>Titel</span>
        <input value={form.title} onChange={(e) => set({ title: e.target.value })} required autoFocus={!initial} placeholder="z. B. Ausgeizen" />
      </label>
      <label className="field">
        <span>Wiederholung</span>
        <select value={form.frequency} onChange={(e) => set({ frequency: e.target.value as Frequency })}>
          {FREQUENCIES.map((f) => (
            <option key={f.value} value={f.value}>
              {f.label}
            </option>
          ))}
        </select>
      </label>
      {recurring && (
        <label className="field">
          <span>Intervall</span>
          <input type="number" min={1} max={365} value={form.interval} onChange={(e) => set({ interval: Number(e.target.value) })} />
        </label>
      )}
      {seasonAllowed && (
        <>
          <label className="check">
            <input
              type="checkbox"
              checked={form.seasonStartMonth !== null}
              onChange={(e) => set(e.target.checked ? { seasonStartMonth: 3, seasonEndMonth: 5 } : { seasonStartMonth: null, seasonEndMonth: null })}
            />
            Nur in einem Saisonfenster
          </label>
          {form.seasonStartMonth !== null && (
            <div className="row">
              <select value={form.seasonStartMonth} onChange={(e) => set({ seasonStartMonth: Number(e.target.value) })} aria-label="Saison von">
                {monthOptions}
              </select>
              <span>bis</span>
              <select value={form.seasonEndMonth ?? 5} onChange={(e) => set({ seasonEndMonth: Number(e.target.value) })} aria-label="Saison bis">
                {monthOptions}
              </select>
            </div>
          )}
        </>
      )}
      <label className="check">
        <input type="checkbox" checked={form.startMonth !== null} onChange={(e) => set({ startMonth: e.target.checked ? 3 : null })} />
        Fester Termin im Jahr
      </label>
      {form.startMonth !== null && (
        <div className="row">
          <input
            type="number"
            min={1}
            max={31}
            value={form.startDay}
            onChange={(e) => set({ startDay: Number(e.target.value) })}
            aria-label="Tag"
            className="day-input"
          />
          <select value={form.startMonth} onChange={(e) => set({ startMonth: Number(e.target.value) })} aria-label="Monat">
            {monthOptions}
          </select>
        </div>
      )}
      <p className="hint">
        Erster Termin: {form.startMonth !== null ? "der nächste feste Termin" : form.seasonStartMonth !== null ? "heute bzw. der nächste Saisonbeginn" : "der Tag, an dem die Vorlage übernommen wird"}.
      </p>
      <label className="check">
        <input type="checkbox" checked={form.notify} onChange={(e) => set({ notify: e.target.checked })} />
        Push-Benachrichtigung
      </label>
      {form.notify && (
        <label className="field">
          <span>Tage vorher erinnern</span>
          <input type="number" min={0} max={365} value={form.leadDays} onChange={(e) => set({ leadDays: Number(e.target.value) })} />
        </label>
      )}
      <label className="field">
        <span>Beschreibung</span>
        <textarea value={form.description ?? ""} onChange={(e) => set({ description: e.target.value || null })} rows={3} />
      </label>
      <div className="actions">
        {initial && (
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
