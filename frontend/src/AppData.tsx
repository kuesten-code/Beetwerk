import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, errorMessage } from "./lib/api";
import { findConflicts, type NeighborConflict } from "./lib/neighbors";
import type { AppConfig, DeviceLink, Garden, GardenObject, GardenTask, MapOverlay, ObjectType, PlantSpecies, Relation } from "./lib/types";

interface AppData {
  config: AppConfig;
  garden: Garden;
  objectTypes: ObjectType[];
  objects: GardenObject[];
  species: PlantSpecies[];
  openTasks: GardenTask[];
  overlays: MapOverlay[];
  conflicts: NeighborConflict[];
  devices: Map<number, DeviceLink>;
  typeById: Map<number, ObjectType>;
  objectById: Map<number, GardenObject>;
  setGarden: (garden: Garden) => void;
  setOverlays: (overlays: MapOverlay[]) => void;
  reloadTypes: () => Promise<void>;
  reloadObjects: () => Promise<void>;
  reloadSpecies: () => Promise<void>;
  reloadTasks: () => Promise<void>;
  reloadOverlays: () => Promise<void>;
  reloadRelations: () => Promise<void>;
  reloadDevices: () => Promise<void>;
}

const AppDataContext = createContext<AppData | null>(null);

export function useAppData(): AppData {
  const value = useContext(AppDataContext);
  if (!value) throw new Error("useAppData außerhalb von AppDataProvider");
  return value;
}

export function AppDataProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [garden, setGarden] = useState<Garden | null>(null);
  const [objectTypes, setObjectTypes] = useState<ObjectType[]>([]);
  const [objects, setObjects] = useState<GardenObject[]>([]);
  const [species, setSpecies] = useState<PlantSpecies[]>([]);
  const [openTasks, setOpenTasks] = useState<GardenTask[]>([]);
  const [overlays, setOverlays] = useState<MapOverlay[]>([]);
  const [relations, setRelations] = useState<Relation[]>([]);
  const [devices, setDevices] = useState<DeviceLink[]>([]);
  const [error, setError] = useState<string | null>(null);

  const reloadTypes = useCallback(async () => setObjectTypes(await api.objectTypes()), []);
  const reloadObjects = useCallback(async () => setObjects(await api.objects()), []);
  const reloadSpecies = useCallback(async () => setSpecies(await api.species()), []);
  const reloadTasks = useCallback(async () => setOpenTasks(await api.tasks("open")), []);
  const reloadOverlays = useCallback(async () => setOverlays(await api.overlays()), []);
  const reloadRelations = useCallback(async () => setRelations(await api.relations()), []);
  const reloadDevices = useCallback(async () => setDevices(await api.devices()), []);

  useEffect(() => {
    Promise.all([
      api.config(),
      api.garden(),
      reloadTypes(),
      reloadObjects(),
      reloadSpecies(),
      reloadTasks(),
      reloadOverlays(),
      reloadRelations(),
      reloadDevices(),
    ])
      .then(([cfg, g]) => {
        setConfig(cfg);
        setGarden(g);
      })
      .catch((e) => setError(errorMessage(e)));
  }, [reloadTypes, reloadObjects, reloadSpecies, reloadTasks, reloadOverlays, reloadRelations, reloadDevices]);

  // Beim Zurückkehren in die App (z. B. nach einer Push-Nachricht) Aufgaben und Geräte auffrischen.
  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState !== "visible") return;
      reloadTasks().catch(() => undefined);
      reloadDevices().catch(() => undefined);
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [reloadTasks, reloadDevices]);

  // Gerätestatus liest nur den Zwischenspeicher des Servers – die Anbieter selbst fragt der Server im eigenen Takt ab.
  // Aufgaben mit, weil Gerätefehler im Hintergrund neue Aufgaben anlegen.
  useEffect(() => {
    const timer = window.setInterval(() => {
      if (document.visibilityState !== "visible") return;
      reloadDevices().catch(() => undefined);
      reloadTasks().catch(() => undefined);
    }, 30_000);
    return () => window.clearInterval(timer);
  }, [reloadDevices, reloadTasks]);

  const conflicts = useMemo(
    () => (garden ? findConflicts(objects, relations, garden.neighborWarningDistance) : []),
    [objects, relations, garden],
  );

  const value = useMemo<AppData | null>(() => {
    if (!config || !garden) return null;
    return {
      config,
      garden,
      objectTypes,
      objects,
      species,
      openTasks,
      overlays,
      conflicts,
      devices: new Map(devices.map((d) => [d.objectId, d])),
      typeById: new Map(objectTypes.map((t) => [t.id, t])),
      objectById: new Map(objects.map((o) => [o.id, o])),
      setGarden,
      setOverlays,
      reloadTypes,
      reloadObjects,
      reloadSpecies,
      reloadTasks,
      reloadOverlays,
      reloadRelations,
      reloadDevices,
    };
  }, [config, garden, objectTypes, objects, species, openTasks, overlays, conflicts, devices, reloadTypes, reloadObjects, reloadSpecies, reloadTasks, reloadOverlays, reloadRelations, reloadDevices]);

  if (error) return <div className="splash error">Beetwerk konnte nicht geladen werden: {error}</div>;
  if (!value) return <div className="splash">🌱 Beetwerk wird geladen …</div>;
  return <AppDataContext.Provider value={value}>{children}</AppDataContext.Provider>;
}
