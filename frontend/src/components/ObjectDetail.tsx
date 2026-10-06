import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAppData } from "../AppData";
import { api } from "../lib/api";
import { formatDate } from "../lib/dates";
import type { GardenObject, Neighbor } from "../lib/types";
import { NeighborList } from "./NeighborList";
import { TaskRow, useTaskActions } from "./TaskList";

interface ObjectDetailProps {
  object: GardenObject;
  onEdit: () => void;
  onEditGeometry: () => void;
  onAddTask: () => void;
  onDelete: () => void;
}

export function ObjectDetail({ object, onEdit, onEditGeometry, onAddTask, onDelete }: ObjectDetailProps) {
  const { typeById, objectById, openTasks, objects } = useAppData();
  const { complete } = useTaskActions();
  const [neighbors, setNeighbors] = useState<Neighbor[] | null>(null);
  const type = typeById.get(object.objectTypeId);
  const parent = object.parentObjectId ? objectById.get(object.parentObjectId) : null;
  const children = objects.filter((o) => o.parentObjectId === object.id);
  const tasks = openTasks.filter((t) => t.objectId === object.id);
  const speciesField = type?.fields.find((f) => f.type === "Species");

  useEffect(() => {
    setNeighbors(null);
    if (object.plantSpeciesId) api.speciesDetail(object.plantSpeciesId).then((d) => setNeighbors(d.neighbors), () => setNeighbors([]));
  }, [object.plantSpeciesId]);

  return (
    <div className="detail">
      <p className="muted">
        {type?.icon} {type?.name}
        {parent && <> · liegt in {parent.name}</>}
      </p>

      <dl className="facts">
        {speciesField && object.plantSpeciesId && (
          <div className="fact">
            <dt>{speciesField.label}</dt>
            <dd>
              <Link to={`/arten/${object.plantSpeciesId}`}>{object.plantSpeciesName}</Link>
            </dd>
          </div>
        )}
        {type?.fields
          .filter((f) => f.type !== "Species" && object.attributes[f.key])
          .map((f) => (
            <div key={f.key} className="fact">
              <dt>{f.label}</dt>
              <dd>{f.type === "Date" ? formatDate(object.attributes[f.key]) : object.attributes[f.key]}</dd>
            </div>
          ))}
      </dl>
      {object.notes && <p className="notes">{object.notes}</p>}

      {children.length > 0 && (
        <>
          <h3>Enthält</h3>
          <p>{children.map((c) => c.name).join(", ")}</p>
        </>
      )}

      {object.plantSpeciesId && neighbors && (
        <>
          <h3>Nachbarschaft</h3>
          <NeighborList neighbors={neighbors} />
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
    </div>
  );
}
