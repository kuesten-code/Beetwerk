import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, errorMessage } from "./lib/api";
import type { AppConfig, Garden, GardenObject, GardenTask, ObjectType, PlantSpecies } from "./lib/types";

interface AppData {
  config: AppConfig;
  garden: Garden;
  objectTypes: ObjectType[];
  objects: GardenObject[];
  species: PlantSpecies[];
  openTasks: GardenTask[];
  typeById: Map<number, ObjectType>;
  objectById: Map<number, GardenObject>;
  setGarden: (garden: Garden) => void;
  reloadTypes: () => Promise<void>;
  reloadObjects: () => Promise<void>;
  reloadSpecies: () => Promise<void>;
  reloadTasks: () => Promise<void>;
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
  const [error, setError] = useState<string | null>(null);

  const reloadTypes = useCallback(async () => setObjectTypes(await api.objectTypes()), []);
  const reloadObjects = useCallback(async () => setObjects(await api.objects()), []);
  const reloadSpecies = useCallback(async () => setSpecies(await api.species()), []);
  const reloadTasks = useCallback(async () => setOpenTasks(await api.tasks("open")), []);

  useEffect(() => {
    Promise.all([api.config(), api.garden(), reloadTypes(), reloadObjects(), reloadSpecies(), reloadTasks()])
      .then(([cfg, g]) => {
        setConfig(cfg);
        setGarden(g);
      })
      .catch((e) => setError(errorMessage(e)));
  }, [reloadTypes, reloadObjects, reloadSpecies, reloadTasks]);

  // Beim Zurückkehren in die App (z. B. nach einer Push-Nachricht) Aufgaben auffrischen.
  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === "visible") reloadTasks().catch(() => undefined);
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [reloadTasks]);

  const value = useMemo<AppData | null>(() => {
    if (!config || !garden) return null;
    return {
      config,
      garden,
      objectTypes,
      objects,
      species,
      openTasks,
      typeById: new Map(objectTypes.map((t) => [t.id, t])),
      objectById: new Map(objects.map((o) => [o.id, o])),
      setGarden,
      reloadTypes,
      reloadObjects,
      reloadSpecies,
      reloadTasks,
    };
  }, [config, garden, objectTypes, objects, species, openTasks, reloadTypes, reloadObjects, reloadSpecies, reloadTasks]);

  if (error) return <div className="splash error">Beetwerk konnte nicht geladen werden: {error}</div>;
  if (!value) return <div className="splash">🌱 Beetwerk wird geladen …</div>;
  return <AppDataContext.Provider value={value}>{children}</AppDataContext.Provider>;
}
