import type { Geometry, GeometryKind, Position } from "./types";

/** Punkt, an dem Marker (Icons, Aufgaben-Badges) für eine Geometrie angezeigt werden. */
export function anchorOf(geometry: Geometry): Position {
  switch (geometry.type) {
    case "Point":
      return geometry.coordinates;
    case "LineString":
      return geometry.coordinates[Math.floor(geometry.coordinates.length / 2)];
    case "Polygon": {
      // Letzter Punkt des Rings wiederholt den ersten und würde den Mittelwert verzerren.
      const ring = geometry.coordinates[0].slice(0, -1);
      const sum = ring.reduce<[number, number]>((acc, [lng, lat]) => [acc[0] + lng, acc[1] + lat], [0, 0]);
      return [sum[0] / ring.length, sum[1] / ring.length];
    }
  }
}

export function boundsOf(geometry: Geometry): [Position, Position] {
  const positions: Position[] =
    geometry.type === "Point" ? [geometry.coordinates] : geometry.type === "LineString" ? geometry.coordinates : geometry.coordinates.flat();
  const lngs = positions.map((p) => p[0]);
  const lats = positions.map((p) => p[1]);
  return [
    [Math.min(...lngs), Math.min(...lats)],
    [Math.max(...lngs), Math.max(...lats)],
  ];
}

export const GEOMETRY_LABELS: Record<GeometryKind, string> = {
  Point: "Punkt",
  LineString: "Linie",
  Polygon: "Fläche",
};

export const DRAW_HINTS: Record<GeometryKind, string> = {
  Point: "Tippe auf die Karte, um den Punkt zu setzen.",
  LineString: "Tippe die Punkte der Linie nacheinander an. Zum Abschließen den letzten Punkt erneut antippen.",
  Polygon: "Tippe die Ecken der Fläche an. Zum Abschließen den ersten Punkt erneut antippen.",
};
