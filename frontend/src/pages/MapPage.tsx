import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useAppData } from "../AppData";
import { ObjectDetail } from "../components/ObjectDetail";
import { ObjectForm } from "../components/ObjectForm";
import { Sheet } from "../components/Sheet";
import { TaskDetail } from "../components/TaskDetail";
import { TaskForm } from "../components/TaskForm";
import { useToast } from "../components/Toast";
import { api, errorMessage } from "../lib/api";
import { toIsoDate } from "../lib/dates";
import { DRAW_HINTS, GEOMETRY_LABELS } from "../lib/geo";
import type { GardenObject, GardenTask, Geometry, GeometryKind } from "../lib/types";
import { GardenMap } from "../map/GardenMap";

type Purpose = "object" | "task" | "boundary";

type Mode =
  | { kind: "idle" }
  | { kind: "add" }
  | { kind: "menu" }
  | { kind: "chooseGeometry"; purpose: Purpose; typeId?: number; options: GeometryKind[] }
  | { kind: "drawing"; purpose: Purpose; geometryKind: GeometryKind; typeId?: number }
  | { kind: "objectForm"; typeId: number; geometry: Geometry; object?: GardenObject }
  | { kind: "taskForm"; geometry: Geometry | null; objectId: number | null; task?: GardenTask }
  | { kind: "object"; id: number }
  | { kind: "task"; task: GardenTask }
  | { kind: "editGeometry"; target: "object"; object: GardenObject }
  | { kind: "editGeometry"; target: "task"; task: GardenTask }
  | { kind: "editGeometry"; target: "boundary"; geometry: Geometry };

const ALL_KINDS: GeometryKind[] = ["Point", "LineString", "Polygon"];
const HIDDEN_TYPES_KEY = "beetwerk.hiddenTypes";

function loadHiddenTypes(): Set<number> {
  try {
    return new Set(JSON.parse(localStorage.getItem(HIDDEN_TYPES_KEY) ?? "[]") as number[]);
  } catch {
    return new Set();
  }
}

function saveHiddenTypes(ids: Set<number>) {
  try {
    localStorage.setItem(HIDDEN_TYPES_KEY, JSON.stringify([...ids]));
  } catch {
    // Nur eine Komfortfunktion – ohne Speicher gilt der Filter eben nur bis zum Neuladen.
  }
}

export function MapPage({ active }: { active: boolean }) {
  const data = useAppData();
  const { config, garden, objectTypes, objects, openTasks, typeById, objectById, reloadObjects, reloadTasks, setGarden } = data;
  const toast = useToast();
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<GardenMap | null>(null);
  const [mode, setMode] = useState<Mode>({ kind: "idle" });
  const [hiddenTypes, setHiddenTypes] = useState<Set<number>>(loadHiddenTypes);
  const [showTasks, setShowTasks] = useState(true);
  const [searchParams, setSearchParams] = useSearchParams();

  const selectObject = useCallback((id: number) => setMode({ kind: "object", id }), []);
  const selectTask = useCallback(
    (id: number) => {
      api.task(id).then(
        (task) => setMode({ kind: "task", task }),
        (e) => toast.error(errorMessage(e)),
      );
    },
    [toast],
  );

  const callbacks = useRef({ onSelectObject: selectObject, onSelectTask: selectTask, onBackgroundClick: () => setMode({ kind: "idle" }) });
  callbacks.current.onSelectObject = selectObject;
  callbacks.current.onSelectTask = selectTask;

  useEffect(() => {
    const map = new GardenMap(containerRef.current!, config, garden, {
      onSelectObject: (id) => callbacks.current.onSelectObject(id),
      onSelectTask: (id) => callbacks.current.onSelectTask(id),
      onBackgroundClick: () => callbacks.current.onBackgroundClick(),
    });
    mapRef.current = map;
    return () => {
      map.destroy();
      mapRef.current = null;
    };
    // Die Karte wird nur einmal erzeugt; spätere Änderungen laufen über setData.
  }, []);

  useEffect(() => {
    if (active) mapRef.current?.resize();
  }, [active]);

  const visibleTypeIds = useMemo(() => new Set(objectTypes.filter((t) => !hiddenTypes.has(t.id)).map((t) => t.id)), [objectTypes, hiddenTypes]);

  useEffect(() => {
    const editing = mode.kind === "editGeometry" ? mode : null;
    void mapRef.current?.setData({
      objects,
      typeById,
      tasks: openTasks,
      boundary: editing?.target === "boundary" ? null : garden.boundary,
      visibleTypeIds,
      showTasks,
      today: toIsoDate(new Date()),
      selectedObjectId: mode.kind === "object" ? mode.id : null,
      selectedTaskId: mode.kind === "task" ? mode.task.id : null,
      hiddenObjectId: editing?.target === "object" ? editing.object.id : null,
      hiddenTaskId: editing?.target === "task" ? editing.task.id : null,
    });
  }, [objects, typeById, openTasks, garden.boundary, visibleTypeIds, showTasks, mode]);

  // Deep-Links aus Aufgabenliste und Push-Benachrichtigungen: /?objekt=1, /?aufgabe=2, /?aktion=aufgabe
  useEffect(() => {
    const objectId = Number(searchParams.get("objekt"));
    const taskId = Number(searchParams.get("aufgabe"));
    const action = searchParams.get("aktion");
    if (!objectId && !taskId && !action) return;
    setSearchParams({}, { replace: true });
    if (objectId && objectById.has(objectId)) {
      setMode({ kind: "object", id: objectId });
      mapRef.current?.focus(objectById.get(objectId)!.geometry);
    } else if (taskId) {
      api.task(taskId).then((task) => {
        setMode({ kind: "task", task });
        const geometry = task.geometry ?? (task.objectId ? objectById.get(task.objectId)?.geometry : null);
        if (geometry) mapRef.current?.focus(geometry);
      });
    } else if (action === "aufgabe") {
      setMode({ kind: "chooseGeometry", purpose: "task", options: ALL_KINDS });
    }
  }, [searchParams, setSearchParams, objectById]);

  const drawingKey = mode.kind === "drawing" ? `${mode.purpose}:${mode.geometryKind}:${mode.typeId}` : null;
  useEffect(() => {
    if (mode.kind !== "drawing") return;
    const { purpose, geometryKind, typeId } = mode;
    let cancelled = false;
    mapRef.current!.drawGeometry(geometryKind).then(async (geometry) => {
      if (cancelled || !geometry) return;
      if (purpose === "object") setMode({ kind: "objectForm", typeId: typeId!, geometry });
      else if (purpose === "task") setMode({ kind: "taskForm", geometry, objectId: null });
      else await saveBoundary(geometry);
    });
    return () => {
      cancelled = true;
      mapRef.current?.cancelDraw();
    };
  }, [drawingKey]);

  async function saveBoundary(boundary: Geometry | null) {
    try {
      setGarden(await api.saveGarden({ ...garden, boundary }));
      toast.show(boundary ? "Gartengrenze gespeichert." : "Gartengrenze entfernt.");
    } catch (e) {
      toast.error(errorMessage(e));
    }
    setMode({ kind: "idle" });
  }

  async function saveStartView() {
    try {
      setGarden(await api.saveGarden({ ...garden, ...mapRef.current!.view() }));
      toast.show("Aktuelle Ansicht ist jetzt die Startansicht.");
    } catch (e) {
      toast.error(errorMessage(e));
    }
    setMode({ kind: "idle" });
  }

  function startObject(typeId: number) {
    const options = typeById.get(typeId)!.allowedGeometries;
    if (options.length === 1) setMode({ kind: "drawing", purpose: "object", geometryKind: options[0], typeId });
    else setMode({ kind: "chooseGeometry", purpose: "object", typeId, options });
  }

  async function startEditGeometry(next: Extract<Mode, { kind: "editGeometry" }>) {
    const geometry = next.target === "object" ? next.object.geometry : next.target === "task" ? next.task.geometry! : next.geometry;
    try {
      await mapRef.current!.startEdit(geometry);
      setMode(next);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function finishEditGeometry() {
    if (mode.kind !== "editGeometry") return;
    const geometry = mapRef.current!.finishEdit();
    if (!geometry) return setMode({ kind: "idle" });
    try {
      if (mode.target === "object") {
        const o = mode.object;
        await api.updateObject(o.id, { ...o, geometry });
        await reloadObjects();
        setMode({ kind: "object", id: o.id });
      } else if (mode.target === "task") {
        const t = mode.task;
        const updated = await api.updateTask(t.id, { ...t, geometry, interval: t.interval });
        await reloadTasks();
        setMode({ kind: "task", task: updated });
      } else {
        await saveBoundary(geometry);
      }
    } catch (e) {
      toast.error(errorMessage(e));
      setMode({ kind: "idle" });
    }
  }

  function cancelEditGeometry() {
    mapRef.current?.cancelDraw();
    if (mode.kind !== "editGeometry") return;
    if (mode.target === "object") setMode({ kind: "object", id: mode.object.id });
    else if (mode.target === "task") setMode({ kind: "task", task: mode.task });
    else setMode({ kind: "idle" });
  }

  async function deleteObject(object: GardenObject) {
    const taskCount = openTasks.filter((t) => t.objectId === object.id).length;
    const warning = taskCount > 0 ? `\n${taskCount} offene Aufgabe(n) werden mitgelöscht.` : "";
    if (!window.confirm(`„${object.name}“ löschen?${warning}`)) return;
    try {
      await api.deleteObject(object.id);
      await Promise.all([reloadObjects(), reloadTasks()]);
      setMode({ kind: "idle" });
      toast.show("Objekt gelöscht.");
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function deleteTask(task: GardenTask) {
    if (!window.confirm(`Aufgabe „${task.title}“ löschen?`)) return;
    try {
      await api.deleteTask(task.id);
      await reloadTasks();
      setMode({ kind: "idle" });
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  function toggleType(id: number) {
    const next = new Set(hiddenTypes);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setHiddenTypes(next);
    saveHiddenTypes(next);
  }

  const close = () => setMode({ kind: "idle" });
  const selectedObject = mode.kind === "object" ? objectById.get(mode.id) : undefined;

  return (
    <div className="map-page" hidden={!active}>
      <div ref={containerRef} className="map-container" />

      {mode.kind === "idle" && (
        <div className="map-fabs">
          <button type="button" className="fab secondary" onClick={() => setMode({ kind: "menu" })} aria-label="Kartenoptionen">
            ☰
          </button>
          <button type="button" className="fab" onClick={() => setMode({ kind: "add" })} aria-label="Hinzufügen">
            ＋
          </button>
        </div>
      )}

      {mode.kind === "drawing" && (
        <div className="draw-banner">
          <p>{DRAW_HINTS[mode.geometryKind]}</p>
          <button type="button" onClick={close}>
            Abbrechen
          </button>
        </div>
      )}

      {mode.kind === "editGeometry" && (
        <div className="draw-banner">
          <p>Form verschieben oder Eckpunkte ziehen. Zwischenpunkte fügen neue Ecken hinzu.</p>
          <div className="row">
            <button type="button" onClick={cancelEditGeometry}>
              Abbrechen
            </button>
            <button type="button" className="primary" onClick={finishEditGeometry}>
              Übernehmen
            </button>
          </div>
        </div>
      )}

      {mode.kind === "add" && (
        <Sheet title="Hinzufügen" onClose={close}>
          <div className="choice-grid">
            {objectTypes.map((t) => (
              <button type="button" key={t.id} className="choice" onClick={() => startObject(t.id)}>
                <span className="choice-icon" style={{ background: t.color }}>
                  {t.icon}
                </span>
                {t.name}
              </button>
            ))}
            <button type="button" className="choice" onClick={() => setMode({ kind: "chooseGeometry", purpose: "task", options: ALL_KINDS })}>
              <span className="choice-icon task">✓</span>
              Aufgabe an einem Ort
            </button>
          </div>
        </Sheet>
      )}

      {mode.kind === "chooseGeometry" && (
        <Sheet title="Wie einzeichnen?" onClose={close}>
          <div className="actions wrap">
            {mode.options.map((kind) => (
              <button
                type="button"
                key={kind}
                className="primary"
                onClick={() => setMode({ kind: "drawing", purpose: mode.purpose, geometryKind: kind, typeId: mode.typeId })}
              >
                {GEOMETRY_LABELS[kind]}
              </button>
            ))}
          </div>
        </Sheet>
      )}

      {mode.kind === "menu" && (
        <Sheet title="Karte" onClose={close}>
          <h3>Anzeigen</h3>
          <div className="chips">
            {objectTypes.map((t) => (
              <button type="button" key={t.id} className={`chip${hiddenTypes.has(t.id) ? "" : " on"}`} onClick={() => toggleType(t.id)} aria-pressed={!hiddenTypes.has(t.id)}>
                {t.icon} {t.name}
              </button>
            ))}
            <button type="button" className={`chip${showTasks ? " on" : ""}`} onClick={() => setShowTasks(!showTasks)} aria-pressed={showTasks}>
              ✓ Aufgaben
            </button>
          </div>
          <h3>Garten</h3>
          <div className="actions wrap">
            <button type="button" onClick={saveStartView}>
              Ansicht als Start speichern
            </button>
            {garden.boundary ? (
              <>
                <button type="button" onClick={() => startEditGeometry({ kind: "editGeometry", target: "boundary", geometry: garden.boundary! })}>
                  Gartengrenze bearbeiten
                </button>
                <button type="button" className="danger" onClick={() => window.confirm("Gartengrenze entfernen?") && saveBoundary(null)}>
                  Gartengrenze entfernen
                </button>
              </>
            ) : (
              <button type="button" onClick={() => setMode({ kind: "drawing", purpose: "boundary", geometryKind: "Polygon" })}>
                Gartengrenze zeichnen
              </button>
            )}
          </div>
        </Sheet>
      )}

      {mode.kind === "objectForm" && (
        <Sheet title={mode.object ? "Objekt bearbeiten" : `Neu: ${typeById.get(mode.typeId)?.name}`} onClose={close}>
          <ObjectForm
            typeId={mode.typeId}
            geometry={mode.geometry}
            initial={mode.object}
            onSaved={(o) => setMode({ kind: "object", id: o.id })}
            onCancel={() => (mode.object ? setMode({ kind: "object", id: mode.object.id }) : close())}
          />
        </Sheet>
      )}

      {mode.kind === "taskForm" && (
        <Sheet title={mode.task ? "Aufgabe bearbeiten" : "Neue Aufgabe"} onClose={close}>
          <TaskForm
            initial={mode.task}
            objectId={mode.objectId}
            geometry={mode.geometry}
            onSaved={(task) => setMode({ kind: "task", task })}
            onCancel={close}
          />
        </Sheet>
      )}

      {selectedObject && (
        <Sheet title={selectedObject.name} onClose={close}>
          <ObjectDetail
            object={selectedObject}
            onEdit={() => setMode({ kind: "objectForm", typeId: selectedObject.objectTypeId, geometry: selectedObject.geometry, object: selectedObject })}
            onEditGeometry={() => startEditGeometry({ kind: "editGeometry", target: "object", object: selectedObject })}
            onAddTask={() => setMode({ kind: "taskForm", geometry: null, objectId: selectedObject.id })}
            onDelete={() => deleteObject(selectedObject)}
          />
        </Sheet>
      )}

      {mode.kind === "task" && (
        <Sheet title={mode.task.title} onClose={close}>
          <TaskDetail
            task={mode.task}
            onChanged={() => selectTask(mode.task.id)}
            onEdit={() => setMode({ kind: "taskForm", geometry: mode.task.geometry, objectId: mode.task.objectId, task: mode.task })}
            onEditGeometry={() => startEditGeometry({ kind: "editGeometry", target: "task", task: mode.task })}
            onDelete={() => deleteTask(mode.task)}
          />
        </Sheet>
      )}
    </div>
  );
}
