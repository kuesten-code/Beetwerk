import type { GardenObject, Geometry, Position, Relation } from "./types";

export interface NeighborConflict {
  a: GardenObject;
  b: GardenObject;
  distance: number;
}

type Xy = [number, number];

const EARTH_RADIUS = 6_371_008.8;

/** Lokale Projektion in Meter um einen Bezugspunkt – im Maßstab eines Gartens genauer als nötig. */
function projector(origin: Position) {
  const cosLat = Math.cos((origin[1] * Math.PI) / 180);
  return ([lng, lat]: Position): Xy => [
    ((lng - origin[0]) * Math.PI * EARTH_RADIUS * cosLat) / 180,
    ((lat - origin[1]) * Math.PI * EARTH_RADIUS) / 180,
  ];
}

function rings(geometry: Geometry): Position[][] {
  switch (geometry.type) {
    case "Point":
      return [[geometry.coordinates]];
    case "LineString":
      return [geometry.coordinates];
    case "Polygon":
      return geometry.coordinates;
  }
}

function segments(points: Xy[]): [Xy, Xy][] {
  if (points.length === 1) return [[points[0], points[0]]];
  return points.slice(1).map((p, i) => [points[i], p]);
}

function pointSegmentDistance(p: Xy, [a, b]: [Xy, Xy]): number {
  const dx = b[0] - a[0];
  const dy = b[1] - a[1];
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / lengthSquared));
  return Math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy));
}

function segmentsIntersect([a, b]: [Xy, Xy], [c, d]: [Xy, Xy]): boolean {
  const cross = (o: Xy, p: Xy, q: Xy) => (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0]);
  const d1 = cross(c, d, a);
  const d2 = cross(c, d, b);
  const d3 = cross(a, b, c);
  const d4 = cross(a, b, d);
  return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
}

function insidePolygon(p: Xy, polygon: Xy[][]): boolean {
  let inside = false;
  for (const ring of polygon) {
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, yi] = ring[i];
      const [xj, yj] = ring[j];
      if (yi > p[1] !== yj > p[1] && p[0] < ((xj - xi) * (p[1] - yi)) / (yj - yi) + xi) inside = !inside;
    }
  }
  return inside;
}

/** Kleinster Abstand zweier Geometrien in Metern; 0, wenn sie sich berühren oder überlappen. */
export function distanceBetween(a: Geometry, b: Geometry): number {
  const project = projector(rings(a)[0][0]);
  const ringsA = rings(a).map((r) => r.map(project));
  const ringsB = rings(b).map((r) => r.map(project));

  if (a.type === "Polygon" && ringsB.some((r) => r.some((p) => insidePolygon(p, ringsA)))) return 0;
  if (b.type === "Polygon" && ringsA.some((r) => r.some((p) => insidePolygon(p, ringsB)))) return 0;

  const segmentsA = ringsA.flatMap(segments);
  const segmentsB = ringsB.flatMap(segments);
  let best = Infinity;
  for (const sa of segmentsA) {
    for (const sb of segmentsB) {
      if (segmentsIntersect(sa, sb)) return 0;
      best = Math.min(best, pointSegmentDistance(sa[0], sb), pointSegmentDistance(sa[1], sb), pointSegmentDistance(sb[0], sa), pointSegmentDistance(sb[1], sa));
    }
  }
  return best;
}

/** Paare von Objekten, deren Pflanzenarten als schlechte Nachbarn eingetragen sind und die zu nah beieinander liegen. */
export function findConflicts(objects: GardenObject[], relations: Relation[], maxDistance: number): NeighborConflict[] {
  const bad = new Set(relations.filter((r) => r.rating === "Bad").map((r) => `${r.speciesAId}:${r.speciesBId}`));
  const plants = objects.filter((o) => o.plantSpeciesId !== null);
  const conflicts: NeighborConflict[] = [];

  for (let i = 0; i < plants.length; i++) {
    for (let j = i + 1; j < plants.length; j++) {
      const a = plants[i];
      const b = plants[j];
      const [low, high] = [a.plantSpeciesId!, b.plantSpeciesId!].sort((x, y) => x - y);
      if (!bad.has(`${low}:${high}`)) continue;
      const distance = distanceBetween(a.geometry, b.geometry);
      if (distance <= maxDistance) conflicts.push({ a, b, distance });
    }
  }
  return conflicts;
}

export function formatMeters(distance: number): string {
  return distance === 0 ? "berühren sich" : `${distance.toLocaleString("de-DE", { maximumFractionDigits: 1 })} m`;
}
