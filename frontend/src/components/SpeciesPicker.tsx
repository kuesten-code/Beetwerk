import { useState } from "react";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import { useToast } from "./Toast";

interface SpeciesPickerProps {
  label: string;
  value: number | null;
  onChange: (id: number | null) => void;
}

const NEW = "__new";

/** Auswahl einer Pflanzenart mit der Möglichkeit, direkt eine neue Art anzulegen. */
export function SpeciesPicker({ label, value, onChange }: SpeciesPickerProps) {
  const { species, reloadSpecies } = useAppData();
  const toast = useToast();
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");

  async function create() {
    try {
      const created = await api.createSpecies({ name, scientificName: null, notes: null, externalUrl: null });
      await reloadSpecies();
      onChange(created.id);
      setCreating(false);
      setName("");
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  if (creating)
    return (
      <div className="field">
        <span>{label} (neu)</span>
        <div className="row">
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="z. B. Tomate" autoFocus />
          <button type="button" className="primary" disabled={!name.trim()} onClick={create}>
            Anlegen
          </button>
          <button type="button" onClick={() => setCreating(false)}>
            Abbrechen
          </button>
        </div>
      </div>
    );

  return (
    <label className="field">
      <span>{label}</span>
      <select
        value={value ?? ""}
        onChange={(e) => {
          if (e.target.value === NEW) setCreating(true);
          else onChange(e.target.value ? Number(e.target.value) : null);
        }}
      >
        <option value="">– keine –</option>
        {species.map((s) => (
          <option key={s.id} value={s.id}>
            {s.name}
          </option>
        ))}
        <option value={NEW}>+ Neue Pflanzenart …</option>
      </select>
    </label>
  );
}
