import { useState, type FormEvent } from "react";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import type { GardenObject, Geometry } from "../lib/types";
import { SpeciesPicker } from "./SpeciesPicker";
import { useToast } from "./Toast";

interface ObjectFormProps {
  typeId: number;
  geometry: Geometry;
  initial?: GardenObject;
  onSaved: (object: GardenObject) => void;
  onCancel: () => void;
}

export function ObjectForm({ typeId, geometry, initial, onSaved, onCancel }: ObjectFormProps) {
  const { objectTypes, typeById, objects, reloadObjects } = useAppData();
  const toast = useToast();
  const [objectTypeId, setObjectTypeId] = useState(initial?.objectTypeId ?? typeId);
  const [name, setName] = useState(initial?.name ?? "");
  const [notes, setNotes] = useState(initial?.notes ?? "");
  const [parentObjectId, setParentObjectId] = useState<number | null>(initial?.parentObjectId ?? null);
  const [plantSpeciesId, setPlantSpeciesId] = useState<number | null>(initial?.plantSpeciesId ?? null);
  const [attributes, setAttributes] = useState<Record<string, string>>(initial?.attributes ?? {});
  const [saving, setSaving] = useState(false);

  const type = typeById.get(objectTypeId)!;
  const compatibleTypes = objectTypes.filter((t) => t.allowedGeometries.includes(geometry.type));
  const speciesField = type.fields.find((f) => f.type === "Species");
  const parentCandidates = objects.filter((o) => o.geometryKind === "Polygon" && o.id !== initial?.id);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    const input = {
      name,
      objectTypeId,
      geometry,
      notes: notes || null,
      parentObjectId,
      plantSpeciesId: speciesField ? plantSpeciesId : null,
      attributes: Object.fromEntries(
        Object.entries(attributes).filter(([key]) => type.fields.some((f) => f.key === key && f.type !== "Species")),
      ),
    };
    try {
      const saved = initial ? await api.updateObject(initial.id, input) : await api.createObject(input);
      await reloadObjects();
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
        <span>Name</span>
        <input value={name} onChange={(e) => setName(e.target.value)} required autoFocus={!initial} placeholder={type.name} />
      </label>
      <label className="field">
        <span>Typ</span>
        <select value={objectTypeId} onChange={(e) => setObjectTypeId(Number(e.target.value))}>
          {compatibleTypes.map((t) => (
            <option key={t.id} value={t.id}>
              {t.icon} {t.name}
            </option>
          ))}
        </select>
      </label>
      {speciesField && <SpeciesPicker label={speciesField.label} value={plantSpeciesId} onChange={setPlantSpeciesId} />}
      {type.fields
        .filter((f) => f.type !== "Species")
        .map((field) => (
          <label className="field" key={field.key}>
            <span>{field.label}</span>
            <input
              type={field.type === "Date" ? "date" : "text"}
              value={attributes[field.key] ?? ""}
              onChange={(e) => setAttributes({ ...attributes, [field.key]: e.target.value })}
            />
          </label>
        ))}
      <label className="field">
        <span>Liegt in</span>
        <select value={parentObjectId ?? ""} onChange={(e) => setParentObjectId(e.target.value ? Number(e.target.value) : null)}>
          <option value="">– nichts –</option>
          {parentCandidates.map((o) => (
            <option key={o.id} value={o.id}>
              {typeById.get(o.objectTypeId)?.icon} {o.name}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Notizen</span>
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
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
