import type {
  AppConfig,
  AppUser,
  Garden,
  GardenObject,
  GardenObjectInput,
  GardenTask,
  GardenTaskInput,
  NeighborRelationInput,
  ObjectType,
  ObjectTypeInput,
  PlantSpecies,
  PlantSpeciesDetail,
  PlantSpeciesInput,
} from "./types";

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

function redirectToLogin(): never {
  const returnUrl = window.location.pathname + window.location.search;
  window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  throw new ApiError(401, "Nicht angemeldet");
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: "same-origin",
    headers: body === undefined ? { Accept: "application/json" } : { Accept: "application/json", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 401) redirectToLogin();
  if (!response.ok) {
    const payload = await response.json().catch(() => null);
    throw new ApiError(response.status, payload?.error ?? `Fehler ${response.status}`);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

const get = <T>(url: string) => request<T>("GET", url);
const post = <T>(url: string, body?: unknown) => request<T>("POST", url, body ?? {});
const put = <T>(url: string, body: unknown) => request<T>("PUT", url, body);
const del = (url: string) => request<void>("DELETE", url);

export const api = {
  config: () => get<AppConfig>("/api/config"),
  logout: () => fetch("/logout", { method: "POST", credentials: "same-origin" }),

  garden: () => get<Garden>("/api/garden"),
  saveGarden: (garden: Garden) => put<Garden>("/api/garden", garden),

  objectTypes: () => get<ObjectType[]>("/api/object-types"),
  createObjectType: (input: ObjectTypeInput) => post<ObjectType>("/api/object-types", input),
  updateObjectType: (id: number, input: ObjectTypeInput) => put<ObjectType>(`/api/object-types/${id}`, input),
  deleteObjectType: (id: number) => del(`/api/object-types/${id}`),

  objects: () => get<GardenObject[]>("/api/objects"),
  createObject: (input: GardenObjectInput) => post<GardenObject>("/api/objects", input),
  updateObject: (id: number, input: GardenObjectInput) => put<GardenObject>(`/api/objects/${id}`, input),
  deleteObject: (id: number) => del(`/api/objects/${id}`),

  species: () => get<PlantSpecies[]>("/api/species"),
  speciesDetail: (id: number) => get<PlantSpeciesDetail>(`/api/species/${id}`),
  createSpecies: (input: PlantSpeciesInput) => post<PlantSpecies>("/api/species", input),
  updateSpecies: (id: number, input: PlantSpeciesInput) => put<PlantSpecies>(`/api/species/${id}`, input),
  deleteSpecies: (id: number) => del(`/api/species/${id}`),

  createRelation: (input: NeighborRelationInput) => post<{ id: number }>("/api/relations", input),
  updateRelation: (id: number, input: NeighborRelationInput) => put<{ id: number }>(`/api/relations/${id}`, input),
  deleteRelation: (id: number) => del(`/api/relations/${id}`),

  tasks: (status: "open" | "done" | "all" = "open", objectId?: number) =>
    get<GardenTask[]>(`/api/tasks?status=${status}${objectId ? `&objectId=${objectId}` : ""}`),
  task: (id: number) => get<GardenTask>(`/api/tasks/${id}`),
  createTask: (input: GardenTaskInput) => post<GardenTask>("/api/tasks", input),
  updateTask: (id: number, input: GardenTaskInput) => put<GardenTask>(`/api/tasks/${id}`, input),
  completeTask: (id: number) => post<{ completed: GardenTask; next: GardenTask | null }>(`/api/tasks/${id}/complete`),
  reopenTask: (id: number) => post<GardenTask>(`/api/tasks/${id}/reopen`),
  deleteTask: (id: number) => del(`/api/tasks/${id}`),

  subscribePush: (subscription: PushSubscriptionJSON, deviceLabel: string) =>
    post<void>("/api/push/subscriptions", { endpoint: subscription.endpoint, keys: subscription.keys, deviceLabel }),
  unsubscribePush: (endpoint: string) => del(`/api/push/subscriptions?endpoint=${encodeURIComponent(endpoint)}`),
  testPush: () => post<{ delivered: number }>("/api/push/test"),

  users: () => get<AppUser[]>("/api/users"),
  createUser: (username: string, password: string) => post<AppUser>("/api/users", { username, password }),
  setPassword: (username: string, password: string) => put<void>(`/api/users/${encodeURIComponent(username)}/password`, { password }),
  deleteUser: (username: string) => del(`/api/users/${encodeURIComponent(username)}`),
};

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
