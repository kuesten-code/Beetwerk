import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAppData } from "../AppData";
import { api } from "../lib/api";
import { formatDate } from "../lib/dates";
import { formatMeters } from "../lib/neighbors";
import type { GardenObject, PlantSpeciesDetail } from "../lib/types";
import { DevicePanel } from "./DevicePanel";
import { NeighborList } from "./NeighborList";
import { ObjectHistory } from "./ObjectHistory";
import { TaskRow, useTaskActions } from "./TaskList";
import { TemplateOffer } from "./TemplateOffer";

interface ObjectDetailProps {
  object: GardenObject;
  /** Direkt nach dem Anlegen werden die typischen Aufgaben der Art von sich aus angeboten. */
  justCreated?: boolean;
  onEdit: () => void;
  onEditGeometry: () => void;
  onAddTask: () => void;
  onDelete: () => void;
}

export function ObjectDetail({ object, justCreated = false, onEdit, onEditGeometry, onAddTask, onDelete }: ObjectDetailProps) {
  const { typeById, objectById, openTasks, objects, conflicts, devices } = useAppData();
  const { complete } = useTaskActions();
  const [species, setSpecies] = useState<PlantSpeciesDetail | null>(null);
  const [offerTemplates, setOfferTemplates] = useState(justCreated);
  const type = typeById.get(object.objectTypeId);
  const parent = object.parentObjectId ? objectById.get(object.parentObjectId) : null;
  const children = objects.filter((o) => o.parentObjectId === object.id);
  const tasks = openTasks.filter((t) => t.objectId === object.id);
  const speciesField = type?.fields.find((f) => f.type === "Species");
  const tooClose = conflicts
    .filter((c) => c.a.id === object.id || c.b.id === object.id)
    .map((c) => ({ other: c.a.id === object.id ? c.b : c.a, distance: c.distance }));
  const attributes = type?.fields.filter((f) => f.type !== "Species" && object.attributes[f.key]) ?? [];

  useEffect(() => {
    setSpecies(null);
    if (object.plantSpeciesId) api.speciesDetail(object.plantSpeciesId).then(setSpecies, () => setSpecies(null));
  }, [object.plantSpeciesId]);

  return (
    <div className="detail">
      <p className="muted">
        {type?.icon} {type?.name}
        {parent && <> · liegt in {parent.name}</>}
      </p>

      {tooClose.length > 0 && (
        <div className="warning" role="alert">
          <strong>⚠ Schlechte Nachbarn in der Nähe</strong>
          <ul>
            {tooClose.map(({ other, distance }) => (
              <li key={other.id}>
                {other.name} ({other.plantSpeciesName}) – {formatMeters(distance)}
              </li>
            ))}
          </ul>
        </div>
      )}

      {(type?.name === "Gerät" || devices.has(object.id)) && (
        <section className="device-section">
          <h3>Gerät</h3>
          <DevicePanel object={object} />
        </section>
      )}

      {offerTemplates && species && species.templates.length > 0 && (
        <TemplateOffer
          objectId={object.id}
          speciesName={species.species.name}
          templates={species.templates}
          onDone={() => setOfferTemplates(false)}
        />
      )}

      {(speciesField && object.plantSpeciesId) || attributes.length > 0 ? (
        <dl className="facts">
          {speciesField && object.plantSpeciesId && (
            <div className="fact">
              <dt>{speciesField.label}</dt>
              <dd>
                <Link to={`/arten/${object.plantSpeciesId}`}>{object.plantSpeciesName}</Link>
              </dd>
            </div>
          )}
          {attributes.map((f) => (
            <div key={f.key} className="fact">
              <dt>{f.label}</dt>
              <dd>{f.type === "Date" ? formatDate(object.attributes[f.key]) : object.attributes[f.key]}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      {object.notes && <p className="notes">{object.notes}</p>}

      {children.length > 0 && (
        <>
          <h3>Enthält</h3>
          <p>{children.map((c) => c.name).join(", ")}</p>
        </>
      )}

      {species && species.neighbors.length > 0 && (
        <>
          <h3>Nachbarschaft</h3>
          <NeighborList neighbors={species.neighbors} />
        </>
      )}

      <h3>Offene Aufgaben</h3>
      {tasks.length === 0 ? (
        <p className="muted">Keine offenen Aufgaben.</p>
      ) : (
        <ul className="task-list">
          {tasks.map((t) => (
            <TaskRow key={t.id} task={t} onComplete={complete} showObject={false} />
          ))}
        </ul>
      )}

      <div className="actions wrap">
        <button type="button" className="primary" onClick={onAddTask}>
          + Aufgabe
        </button>
        {!offerTemplates && species && species.templates.length > 0 && (
          <button type="button" onClick={() => setOfferTemplates(true)}>
            Typische Aufgaben …
          </button>
        )}
        <button type="button" onClick={onEdit}>
          Bearbeiten
        </button>
        <button type="button" onClick={onEditGeometry}>
          Form ändern
        </button>
        <button type="button" className="danger" onClick={onDelete}>
          Löschen
        </button>
      </div>

      <h3>Verlauf &amp; Fotos</h3>
      <ObjectHistory objectId={object.id} refreshKey={tasks.length} />
    </div>
  );
}
