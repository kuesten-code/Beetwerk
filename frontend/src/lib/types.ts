export type GeometryKind = "Point" | "LineString" | "Polygon";

export type Position = [number, number];

export type Geometry =
  | { type: "Point"; coordinates: Position }
  | { type: "LineString"; coordinates: Position[] }
  | { type: "Polygon"; coordinates: Position[][] };

export interface TileSource {
  url: string;
  attribution: string;
  maxZoom: number;
}

export interface AppConfig {
  version: string;
  authMode: "login" | "none";
  username: string | null;
  pushEnabled: boolean;
  vapidPublicKey: string | null;
  map: { primary: TileSource; fallback: TileSource | null };
}

export interface Garden {
  name: string;
  centerLatitude: number;
  centerLongitude: number;
  zoom: number;
  boundary: Geometry | null;
  neighborWarningDistance: number;
}

export type FieldType = "Text" | "Date" | "Species";

export interface ObjectTypeField {
  key: string;
  label: string;
  type: FieldType;
}

export interface ObjectType {
  id: number;
  name: string;
  icon: string;
  color: string;
  allowedGeometries: GeometryKind[];
  fields: ObjectTypeField[];
}

export type ObjectTypeInput = Omit<ObjectType, "id">;

export interface GardenObject {
  id: number;
  name: string;
  objectTypeId: number;
  geometry: Geometry;
  geometryKind: GeometryKind;
  notes: string | null;
  parentObjectId: number | null;
  plantSpeciesId: number | null;
  plantSpeciesName: string | null;
  attributes: Record<string, string>;
}

export interface GardenObjectInput {
  name: string;
  objectTypeId: number;
  geometry: Geometry;
  notes: string | null;
  parentObjectId: number | null;
  plantSpeciesId: number | null;
  attributes: Record<string, string>;
}

export interface PlantSpecies {
  id: number;
  name: string;
  scientificName: string | null;
  notes: string | null;
  externalUrl: string | null;
}

export type PlantSpeciesInput = Omit<PlantSpecies, "id">;

export type NeighborRating = "Good" | "Bad";

export interface Neighbor {
  relationId: number;
  speciesId: number;
  speciesName: string;
  rating: NeighborRating;
  note: string | null;
  source: string | null;
}

export interface PlantSpeciesDetail {
  species: PlantSpecies;
  neighbors: Neighbor[];
  objectCount: number;
  templates: TaskTemplate[];
}

export interface NeighborRelationInput {
  speciesId: number;
  otherSpeciesId: number;
  rating: NeighborRating;
  note: string | null;
  source: string | null;
}

export type Frequency = "None" | "Daily" | "Weekly" | "Monthly" | "Yearly";

export interface GardenTask {
  id: number;
  title: string;
  description: string | null;
  objectId: number | null;
  objectName: string | null;
  geometry: Geometry | null;
  dueDate: string;
  status: "Open" | "Done";
  completedAt: string | null;
  notify: boolean;
  leadDays: number;
  frequency: Frequency;
  interval: number;
  seasonStartMonth: number | null;
  seasonEndMonth: number | null;
  rRule: string | null;
}

export interface GardenTaskInput {
  title: string;
  description: string | null;
  objectId: number | null;
  geometry: Geometry | null;
  dueDate: string;
  notify: boolean;
  leadDays: number;
  frequency: Frequency;
  interval: number;
  seasonStartMonth: number | null;
  seasonEndMonth: number | null;
}

export interface AppUser {
  username: string;
  isCurrent: boolean;
}

export type Corners = [Position, Position, Position, Position];

export interface MapOverlay {
  id: number;
  name: string;
  width: number;
  height: number;
  corners: Corners;
  opacity: number;
  visible: boolean;
  imageUrl: string;
}

export interface Relation {
  id: number;
  speciesAId: number;
  speciesBId: number;
  rating: NeighborRating;
}

export interface TaskTemplate {
  id: number;
  title: string;
  description: string | null;
  frequency: Frequency;
  interval: number;
  seasonStartMonth: number | null;
  seasonEndMonth: number | null;
  startMonth: number | null;
  startDay: number;
  notify: boolean;
  leadDays: number;
}

export type TaskTemplateInput = Omit<TaskTemplate, "id">;

export type HistoryKind = "Note" | "Created" | "TaskCompleted" | "Photo";

export interface HistoryEntry {
  id: number;
  date: string;
  kind: HistoryKind;
  text: string;
  createdBy: string | null;
  taskId: number | null;
  photo: { id: number; imageUrl: string; thumbnailUrl: string; caption: string | null } | null;
}
