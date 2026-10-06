import { describe, expect, it } from "vitest";
import { anchorOf, boundsOf } from "./geo";

describe("geo", () => {
  it("anchors points on themselves", () => {
    expect(anchorOf({ type: "Point", coordinates: [10, 54] })).toEqual([10, 54]);
  });

  it("anchors polygons on the vertex centroid ignoring the closing point", () => {
    const square = { type: "Polygon" as const, coordinates: [[[0, 0], [2, 0], [2, 2], [0, 2], [0, 0]] as [number, number][]] };
    expect(anchorOf(square)).toEqual([1, 1]);
  });

  it("anchors lines on their middle vertex", () => {
    expect(anchorOf({ type: "LineString", coordinates: [[0, 0], [1, 1], [2, 2]] })).toEqual([1, 1]);
  });

  it("computes bounds", () => {
    expect(boundsOf({ type: "LineString", coordinates: [[3, 1], [1, 4]] })).toEqual([
      [1, 1],
      [3, 4],
    ]);
  });
});
