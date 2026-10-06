import * as maplibregl from "maplibre-gl";
import type { GeoJSONSource, StyleSpecification } from "maplibre-gl";
import maplibreWorkerUrl from "maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url";
import {
  TerraDraw,
  TerraDrawLineStringMode,
  TerraDrawPointMode,
  TerraDrawPolygonMode,
  TerraDrawSelectMode,
  type GeoJSONStoreFeatures,
} from "terra-draw";
import { TerraDrawMapLibreGLAdapter } from "terra-draw-maplibre-gl-adapter";
import { dueBucket, type DueBucket } from "../lib/dates";
import { anchorOf, boundsOf } from "../lib/geo";
import type { AppConfig, Garden, GardenObject, GardenTask, Geometry, GeometryKind, ObjectType } from "../lib/types";

export interface MapCallbacks {
  onSelectObject: (id: number) => void;
  onSelectTask: (id: number) => void;
  onBackgroundClick: () => void;
}

export interface MapData {
  objects: GardenObject[];
  typeById: Map<number, ObjectType>;
  tasks: GardenTask[];
  boundary: Geometry | null;
  visibleTypeIds: Set<number>;
  showTasks: boolean;
  today: string;
  selectedObjectId: number | null;
  selectedTaskId: number | null;
  hiddenObjectId: number | null;
  hiddenTaskId: number | null;
}

// MapLibre leitet die Worker-Adresse sonst aus import.meta.url ab, die nach dem Bündeln ins Leere zeigt.
maplibregl.setWorkerUrl(maplibreWorkerUrl);

const DRAW_MODE: Record<GeometryKind, string> = { Point: "point", LineString: "linestring", Polygon: "polygon" };
const DRAW_COLOR = "#ffd600";
const CLICK_LAYERS = ["objects-fill", "objects-line", "task-areas-fill", "task-areas-line"];
const BUCKET_RANK: Record<DueBucket, number> = { overdue: 0, today: 1, week: 2, later: 3 };

type FeatureCollection = GeoJSON.FeatureCollection<GeoJSON.Geometry, Record<string, unknown>>;
const emptyCollection = (): FeatureCollection => ({ type: "FeatureCollection", features: [] });

function buildStyle(config: AppConfig): StyleSpecification {
  const { primary, fallback } = config.map;
  const sources: StyleSpecification["sources"] = {
    primary: { type: "raster", tiles: [primary.url], tileSize: 256, maxzoom: primary.maxZoom, attribution: primary.attribution },
  };
  const layers: StyleSpecification["layers"] = [{ id: "background", type: "background", paint: { "background-color": "#1b2a1b" } }];
  if (fallback) {
    sources.fallback = { type: "raster", tiles: [fallback.url], tileSize: 256, maxzoom: fallback.maxZoom, attribution: fallback.attribution };
    layers.push({ id: "fallback", type: "raster", source: "fallback" });
  }
  // Primärquelle oben: außerhalb ihres Abdeckungsgebiets liefert sie transparente Kacheln und der Fallback scheint durch.
  layers.push({ id: "primary", type: "raster", source: "primary" });
  return { version: 8, sources, layers };
}

function approximateArea(geometry: Geometry): number {
  const [[minLng, minLat], [maxLng, maxLat]] = boundsOf(geometry);
  return (maxLng - minLng) * (maxLat - minLat);
}

export class GardenMap {
  readonly map: maplibregl.Map;
  private draw: TerraDraw | null = null;
  private markers: maplibregl.Marker[] = [];
  private loaded: Promise<void>;
  private pendingDraw: { resolve: (geometry: Geometry | null) => void } | null = null;
  private editingFeatureId: string | number | null = null;
  private interactive = true;
  private interactiveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(container: HTMLElement, config: AppConfig, garden: Garden, private callbacks: MapCallbacks) {
    this.map = new maplibregl.Map({
      container,
      style: buildStyle(config),
      center: [garden.centerLongitude, garden.centerLatitude],
      zoom: garden.zoom,
      maxZoom: 22,
      attributionControl: { compact: true },
      dragRotate: false,
      pitchWithRotate: false,
    });
    this.map.touchZoomRotate.disableRotation();
    this.map.addControl(new maplibregl.NavigationControl({ showCompass: false }), "top-right");
    this.map.addControl(
      new maplibregl.GeolocateControl({ positionOptions: { enableHighAccuracy: true }, trackUserLocation: true }),
      "top-right",
    );
    this.map.addControl(new maplibregl.ScaleControl({ unit: "metric" }), "bottom-left");

    this.loaded = new Promise((resolve) => {
      this.map.on("load", () => {
        this.addLayers();
        this.draw = this.createDraw();
        this.draw.start();
        resolve();
      });
    });

    this.map.on("click", (event) => this.handleClick(event));
  }

  private addLayers() {
    const map = this.map;
    for (const id of ["boundary", "objects", "task-areas", "selection"]) map.addSource(id, { type: "geojson", data: emptyCollection() });

    map.addLayer({
      id: "boundary-line",
      type: "line",
      source: "boundary",
      paint: { "line-color": "#ffffff", "line-width": 2, "line-dasharray": [3, 2], "line-opacity": 0.9 },
    });
    map.addLayer({
      id: "objects-fill",
      type: "fill",
      source: "objects",
      filter: ["==", ["geometry-type"], "Polygon"],
      paint: { "fill-color": ["get", "color"], "fill-opacity": 0.35 },
    });
    map.addLayer({
      id: "objects-outline",
      type: "line",
      source: "objects",
      filter: ["==", ["geometry-type"], "Polygon"],
      paint: { "line-color": ["get", "color"], "line-width": 2 },
    });
    map.addLayer({
      id: "objects-line",
      type: "line",
      source: "objects",
      filter: ["==", ["geometry-type"], "LineString"],
      layout: { "line-cap": "round", "line-join": "round" },
      paint: { "line-color": ["get", "color"], "line-width": 6 },
    });
    map.addLayer({
      id: "task-areas-fill",
      type: "fill",
      source: "task-areas",
      filter: ["==", ["geometry-type"], "Polygon"],
      paint: { "fill-color": "#ff9800", "fill-opacity": 0.15 },
    });
    map.addLayer({
      id: "task-areas-line",
      type: "line",
      source: "task-areas",
      paint: { "line-color": "#ff9800", "line-width": 3, "line-dasharray": [2, 1.5] },
    });
    map.addLayer({
      id: "selection-line",
      type: "line",
      source: "selection",
      filter: ["!=", ["geometry-type"], "Point"],
      layout: { "line-cap": "round", "line-join": "round" },
      paint: { "line-color": DRAW_COLOR, "line-width": 4 },
    });
  }

  private createDraw(): TerraDraw {
    const editable = { draggable: true, coordinates: { midpoints: true, draggable: true, deletable: true } };
    const draw = new TerraDraw({
      adapter: new TerraDrawMapLibreGLAdapter({ map: this.map }),
      modes: [
        new TerraDrawPointMode({ styles: { pointColor: DRAW_COLOR, pointWidth: 10, pointOutlineColor: "#000000", pointOutlineWidth: 2 } }),
        new TerraDrawLineStringMode({ styles: { lineStringColor: DRAW_COLOR, lineStringWidth: 4, closingPointColor: DRAW_COLOR, closingPointWidth: 10 } }),
        new TerraDrawPolygonMode({
          styles: { fillColor: DRAW_COLOR, fillOpacity: 0.25, outlineColor: DRAW_COLOR, outlineWidth: 3, closingPointColor: DRAW_COLOR, closingPointWidth: 10 },
        }),
        new TerraDrawSelectMode({
          flags: { point: { feature: { draggable: true } }, linestring: { feature: editable }, polygon: { feature: editable } },
          styles: {
            selectedPolygonColor: DRAW_COLOR,
            selectedPolygonOutlineColor: DRAW_COLOR,
            selectedLineStringColor: DRAW_COLOR,
            selectedPointColor: DRAW_COLOR,
            selectionPointWidth: 9,
            midPointWidth: 7,
          },
        }),
      ],
    });

    draw.on("finish", (id, context) => {
      if (!this.pendingDraw || context.action !== "draw") return;
      const feature = draw.getSnapshotFeature(id);
      const resolve = this.pendingDraw.resolve;
      this.pendingDraw = null;
      draw.clear();
      draw.setMode("static");
      this.setInteractive(true);
      resolve(feature ? (feature.geometry as Geometry) : null);
    });
    return draw;
  }

  private handleClick(event: maplibregl.MapMouseEvent) {
    if (!this.interactive) return;
    const { x, y } = event.point;
    const tolerance = 10;
    const features = this.map.queryRenderedFeatures(
      [
        [x - tolerance, y - tolerance],
        [x + tolerance, y + tolerance],
      ],
      { layers: CLICK_LAYERS },
    );
    const hit = features[0];
    if (!hit) return this.callbacks.onBackgroundClick();
    const id = Number(hit.properties.id);
    if (hit.source === "objects") this.callbacks.onSelectObject(id);
    else this.callbacks.onSelectTask(id);
  }

  async setData(data: MapData) {
    await this.loaded;
    const visibleObjects = data.objects.filter((o) => data.visibleTypeIds.has(o.objectTypeId) && o.id !== data.hiddenObjectId);

    // Große Flächen zuerst, damit kleinere (z. B. Pflanze im Beet) darüber liegen und zuerst angetippt werden.
    const areaFeatures = visibleObjects
      .filter((o) => o.geometry.type !== "Point")
      .sort((a, b) => approximateArea(b.geometry) - approximateArea(a.geometry))
      .map((o) => ({
        type: "Feature" as const,
        geometry: o.geometry,
        properties: { id: o.id, color: data.typeById.get(o.objectTypeId)?.color ?? "#9e9e9e" },
      }));
    this.source("objects").setData({ type: "FeatureCollection", features: areaFeatures });

    const freeTasks = data.showTasks ? data.tasks.filter((t) => t.objectId === null && t.geometry && t.id !== data.hiddenTaskId) : [];
    this.source("task-areas").setData({
      type: "FeatureCollection",
      features: freeTasks
        .filter((t) => t.geometry!.type !== "Point")
        .map((t) => ({ type: "Feature" as const, geometry: t.geometry!, properties: { id: t.id } })),
    });

    this.source("boundary").setData(
      data.boundary ? { type: "FeatureCollection", features: [{ type: "Feature", geometry: data.boundary, properties: {} }] } : emptyCollection(),
    );

    const selected =
      data.selectedObjectId !== null
        ? data.objects.find((o) => o.id === data.selectedObjectId)?.geometry
        : data.selectedTaskId !== null
          ? data.tasks.find((t) => t.id === data.selectedTaskId)?.geometry ?? undefined
          : undefined;
    this.source("selection").setData(
      selected ? { type: "FeatureCollection", features: [{ type: "Feature", geometry: selected, properties: {} }] } : emptyCollection(),
    );

    this.renderMarkers(visibleObjects, data, freeTasks);
  }

  private renderMarkers(objects: GardenObject[], data: MapData, freeTasks: GardenTask[]) {
    for (const marker of this.markers) marker.remove();
    this.markers = [];

    for (const obj of objects) {
      if (obj.geometry.type !== "Point") continue;
      const type = data.typeById.get(obj.objectTypeId);
      const element = document.createElement("button");
      element.type = "button";
      element.className = "map-pin" + (obj.id === data.selectedObjectId ? " selected" : "");
      element.style.setProperty("--pin-color", type?.color ?? "#9e9e9e");
      element.textContent = type?.icon ?? "📍";
      element.title = obj.name;
      element.setAttribute("aria-label", obj.name);
      this.addMarker(element, anchorOf(obj.geometry), () => this.callbacks.onSelectObject(obj.id));
    }

    if (!data.showTasks) return;

    const visibleObjectIds = new Set(objects.map((o) => o.id));
    const byObject = new Map<number, GardenTask[]>();
    for (const task of data.tasks) {
      if (task.objectId === null || !visibleObjectIds.has(task.objectId)) continue;
      byObject.set(task.objectId, [...(byObject.get(task.objectId) ?? []), task]);
    }
    for (const [objectId, tasks] of byObject) {
      const obj = objects.find((o) => o.id === objectId)!;
      const onClick = tasks.length === 1 ? () => this.callbacks.onSelectTask(tasks[0].id) : () => this.callbacks.onSelectObject(objectId);
      this.addMarker(this.taskBadge(tasks, data.today), anchorOf(obj.geometry), onClick, [14, -14]);
    }
    for (const task of freeTasks) {
      this.addMarker(this.taskBadge([task], data.today, task.id === data.selectedTaskId), anchorOf(task.geometry!), () =>
        this.callbacks.onSelectTask(task.id),
      );
    }
  }

  private taskBadge(tasks: GardenTask[], today: string, selected = false): HTMLElement {
    const worst = tasks.map((t) => dueBucket(t.dueDate, today)).sort((a, b) => BUCKET_RANK[a] - BUCKET_RANK[b])[0];
    const element = document.createElement("button");
    element.type = "button";
    element.className = `task-badge ${worst}` + (selected ? " selected" : "");
    element.textContent = tasks.length > 1 ? String(tasks.length) : "✓";
    element.title = tasks.map((t) => t.title).join(", ");
    element.setAttribute("aria-label", `Aufgaben: ${element.title}`);
    return element;
  }

  private addMarker(element: HTMLElement, position: [number, number], onClick: () => void, offset: [number, number] = [0, 0]) {
    element.addEventListener("click", (event) => {
      event.stopPropagation();
      if (this.interactive) onClick();
    });
    this.markers.push(new maplibregl.Marker({ element, offset }).setLngLat(position).addTo(this.map));
  }

  private source(id: string): GeoJSONSource {
    return this.map.getSource(id) as GeoJSONSource;
  }

  /**
   * Der Tipp, der eine Zeichnung abschließt, kommt anschließend noch als Klick bei der Karte an und würde
   * sonst als „Tipp ins Leere“ die gerade geöffnete Eingabemaske schließen. Daher erst verzögert wieder freigeben.
   */
  private setInteractive(enabled: boolean) {
    clearTimeout(this.interactiveTimer);
    // Beim Zeichnen dürfen Marker die Tipps nicht abfangen, sonst ließe sich auf ihnen kein Punkt setzen.
    const container = this.map.getContainer();
    if (!enabled) {
      this.interactive = false;
      container.classList.add("drawing");
      return;
    }
    this.interactiveTimer = setTimeout(() => {
      this.interactive = true;
      container.classList.remove("drawing");
    }, 400);
  }

  /** Startet das Zeichnen; das Ergebnis kommt, sobald die Geometrie abgeschlossen ist (oder null bei Abbruch). */
  async drawGeometry(kind: GeometryKind): Promise<Geometry | null> {
    await this.loaded;
    this.cancelDraw();
    this.setInteractive(false);
    this.draw!.setMode(DRAW_MODE[kind]);
    return new Promise((resolve) => (this.pendingDraw = { resolve }));
  }

  cancelDraw() {
    if (this.pendingDraw) {
      this.pendingDraw.resolve(null);
      this.pendingDraw = null;
    }
    this.draw?.clear();
    this.draw?.setMode("static");
    this.editingFeatureId = null;
    this.setInteractive(true);
  }

  async startEdit(geometry: Geometry) {
    await this.loaded;
    this.cancelDraw();
    this.setInteractive(false);
    const draw = this.draw!;
    draw.setMode("select");
    const feature = { type: "Feature", geometry, properties: { mode: DRAW_MODE[geometry.type] } } as GeoJSONStoreFeatures;
    const [result] = draw.addFeatures([feature]);
    if (!result?.valid) throw new Error("Die Geometrie kann nicht bearbeitet werden.");
    this.editingFeatureId = result.id!;
    draw.selectFeature(result.id!);
  }

  finishEdit(): Geometry | null {
    if (this.editingFeatureId === null || !this.draw) return null;
    const feature = this.draw.getSnapshotFeature(this.editingFeatureId);
    this.cancelDraw();
    return feature ? (feature.geometry as Geometry) : null;
  }

  focus(geometry: Geometry) {
    if (geometry.type === "Point") {
      this.map.easeTo({ center: geometry.coordinates, zoom: Math.max(this.map.getZoom(), 19) });
      return;
    }
    this.map.fitBounds(boundsOf(geometry), { padding: { top: 60, bottom: Math.round(window.innerHeight * 0.45), left: 40, right: 40 }, maxZoom: 21 });
  }

  view() {
    const center = this.map.getCenter();
    return { centerLatitude: center.lat, centerLongitude: center.lng, zoom: Math.round(this.map.getZoom() * 10) / 10 };
  }

  resize() {
    this.map.resize();
  }

  destroy() {
    clearTimeout(this.interactiveTimer);
    this.cancelDraw();
    this.draw?.stop();
    this.map.remove();
  }
}
