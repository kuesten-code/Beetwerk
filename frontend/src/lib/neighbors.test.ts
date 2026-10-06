import { describe, expect, it } from "vitest";
import { fitWithin } from "./image";
import { distanceBetween, findConflicts, formatMeters } from "./neighbors";
import type { GardenObject, Geometry, Position } from "./types";

// Bei 54° N entspricht 0,00001° Breite etwa 1,11 m und 0,00001° Länge etwa 0,65 m.
const LAT = 54.47;
const LNG = 8.91;
const point = (dLng: number, dLat: number): Geometry => ({ type: "Point", coordinates: [LNG + dLng, LAT + dLat] });
const square = (dLng: number, dLat: number, size: number): Geometry => {
  const ring: Position[] = [
    [LNG + dLng, LAT + dLat],
    [LNG + dLng + size, LAT + dLat],
    [LNG + dLng + size, LAT + dLat + size],
    [LNG + dLng, LAT + dLat + size],
    [LNG + dLng, LAT + dLat],
  ];
  return { type: "Polygon", coordinates: [ring] };
};

const plant = (id: number, speciesId: number | null, geometry: Geometry): GardenObject => ({
  id,
  name: `P${id}`,
  objectTypeId: 1,
  geometry,
  geometryKind: geometry.type,
  notes: null,
  parentObjectId: null,
  plantSpeciesId: speciesId,
  plantSpeciesName: null,
  attributes: {},
});

describe("distanceBetween", () => {
  it("measures point distances in meters", () => {
    expect(distanceBetween(point(0, 0), point(0, 0.00001))).toBeCloseTo(1.11, 1);
    expect(distanceBetween(point(0, 0), point(0.00001, 0))).toBeCloseTo(0.65, 1);
  });

  it("is zero for a point inside a polygon", () => {
    expect(distanceBetween(square(0, 0, 0.0001), point(0.00005, 0.00005))).toBe(0);
    expect(distanceBetween(point(0.00005, 0.00005), square(0, 0, 0.0001))).toBe(0);
  });

  it("measures to the nearest polygon edge", () => {
    expect(distanceBetween(square(0, 0, 0.0001), point(0.00005, 0.00011))).toBeCloseTo(1.11, 1);
  });

  it("detects crossing lines", () => {
    const a: Geometry = { type: "LineString", coordinates: [[LNG, LAT], [LNG + 0.0001, LAT + 0.0001]] };
    const b: Geometry = { type: "LineString", coordinates: [[LNG, LAT + 0.0001], [LNG + 0.0001, LAT]] };
    expect(distanceBetween(a, b)).toBe(0);
  });
});

describe("findConflicts", () => {
  const relations = [{ id: 1, speciesAId: 1, speciesBId: 2, rating: "Bad" as const }, { id: 2, speciesAId: 1, speciesBId: 3, rating: "Good" as const }];

  it("reports bad neighbors within the distance", () => {
    const objects = [plant(1, 1, point(0, 0)), plant(2, 2, point(0, 0.000005)), plant(3, 3, point(0, 0.000005)), plant(4, null, point(0, 0))];
    const conflicts = findConflicts(objects, relations, 1);
    expect(conflicts).toHaveLength(1);
    expect([conflicts[0].a.id, conflicts[0].b.id]).toEqual([1, 2]);
  });

  it("ignores bad neighbors farther away", () => {
    expect(findConflicts([plant(1, 1, point(0, 0)), plant(2, 2, point(0, 0.0001))], relations, 1)).toHaveLength(0);
  });

  it("is symmetric regardless of species order", () => {
    expect(findConflicts([plant(1, 2, point(0, 0)), plant(2, 1, point(0, 0))], relations, 1)).toHaveLength(1);
  });

  it("formats distances", () => {
    expect(formatMeters(0)).toBe("berühren sich");
    expect(formatMeters(0.84)).toBe("0,8 m");
  });
});

describe("fitWithin", () => {
  it("keeps aspect ratio and never enlarges", () => {
    expect(fitWithin(8000, 6000, 4096)).toEqual({ width: 4096, height: 3072 });
    expect(fitWithin(800, 600, 4096)).toEqual({ width: 800, height: 600 });
  });
});
