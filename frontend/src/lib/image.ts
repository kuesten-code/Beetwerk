export interface ResizedImage {
  blob: Blob;
  width: number;
  height: number;
}

/** Größte Kantenlänge, die auch Handys sicher als WebGL-Textur darstellen. */
export const OVERLAY_MAX_EDGE = 4096;
export const PHOTO_MAX_EDGE = 2048;
export const THUMBNAIL_MAX_EDGE = 400;

export function fitWithin(width: number, height: number, maxEdge: number): { width: number; height: number } {
  const scale = Math.min(1, maxEdge / Math.max(width, height));
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/**
 * Verkleinert ein Bild im Browser. Dadurch braucht der Server keine Bildbibliothek, und Handyfotos
 * werden nicht in voller Kameraauflösung hochgeladen. Die EXIF-Ausrichtung wird beim Dekodieren angewendet.
 */
export async function resizeImage(source: Blob, maxEdge: number, keepTransparency = false): Promise<ResizedImage> {
  const bitmap = await createImageBitmap(source, { imageOrientation: "from-image" });
  try {
    const { width, height } = fitWithin(bitmap.width, bitmap.height, maxEdge);
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d")!;
    context.imageSmoothingQuality = "high";
    context.drawImage(bitmap, 0, 0, width, height);
    const type = keepTransparency ? "image/png" : "image/jpeg";
    const blob = await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("Das Bild konnte nicht verarbeitet werden."))), type, 0.88),
    );
    return { blob, width, height };
  } finally {
    bitmap.close();
  }
}
