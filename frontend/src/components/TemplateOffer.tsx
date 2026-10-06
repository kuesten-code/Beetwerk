import { useState } from "react";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import { describeRecurrence } from "../lib/dates";
import type { TaskTemplate } from "../lib/types";
import { useToast } from "./Toast";

interface TemplateOfferProps {
  objectId: number;
  speciesName: string;
  templates: TaskTemplate[];
  onDone: () => void;
}

/** Bietet die typischen Aufgaben einer Pflanzenart zur Übernahme an. */
export function TemplateOffer({ objectId, speciesName, templates, onDone }: TemplateOfferProps) {
  const { reloadTasks } = useAppData();
  const toast = useToast();
  const [selected, setSelected] = useState<Set<number>>(() => new Set(templates.map((t) => t.id)));

  async function apply() {
    try {
      const created = await api.applyTemplates(objectId, [...selected]);
      await reloadTasks();
      toast.show(`${created.length} Aufgabe(n) angelegt.`);
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  function toggle(id: number) {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setSelected(next);
  }

  return (
    <div className="template-offer">
      <strong>Typische Aufgaben für {speciesName}</strong>
      {templates.map((t) => (
        <label key={t.id} className="check">
          <input type="checkbox" checked={selected.has(t.id)} onChange={() => toggle(t.id)} />
          <span>
            {t.title}
            {describeRecurrence(t) && <small className="muted"> · {describeRecurrence(t)}</small>}
          </span>
        </label>
      ))}
      <div className="actions">
        <button type="button" onClick={onDone}>
          Nicht jetzt
        </button>
        <button type="button" className="primary" disabled={selected.size === 0} onClick={apply}>
          Übernehmen
        </button>
      </div>
    </div>
  );
}
