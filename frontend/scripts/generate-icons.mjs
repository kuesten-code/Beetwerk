// Rastert das Beetwerk-Icon (gleiches Motiv wie public/icons/icon.svg) ohne externe Abhängigkeiten zu PNGs.
// Aufruf: node scripts/generate-icons.mjs
import { writeFileSync } from "node:fs";
import { deflateSync } from "node:zlib";

const GREEN = [46, 125, 50];
const WHITE = [255, 255, 255];
const LIGHT = [197, 225, 165];

function insideRoundedRect(x, y, x0, y0, w, h, r) {
  if (x < x0 || y < y0 || x > x0 + w || y > y0 + h) return false;
  const cx = Math.min(Math.max(x, x0 + r), x0 + w - r);
  const cy = Math.min(Math.max(y, y0 + r), y0 + h - r);
  return (x - cx) ** 2 + (y - cy) ** 2 <= r * r;
}

function insideEllipse(x, y, cx, cy, rx, ry, degrees) {
  const a = (-degrees * Math.PI) / 180;
  const dx = x - cx;
  const dy = y - cy;
  const u = dx * Math.cos(a) - dy * Math.sin(a);
  const v = dx * Math.sin(a) + dy * Math.cos(a);
  return (u / rx) ** 2 + (v / ry) ** 2 <= 1;
}

/** Farbe im 100er-Koordinatensystem des SVG; null = transparent. */
function sample(x, y, { background, scale, mono }) {
  // Motiv für maskable Icons verkleinern, damit es in der sicheren Zone bleibt.
  const mx = 50 + (x - 50) / scale;
  const my = 50 + (y - 50) / scale;
  const fg = (color) => (mono ? WHITE : color);
  if (insideEllipse(mx, my, 64, 34, 20, 9.5, -35)) return fg(LIGHT);
  if (insideEllipse(mx, my, 36, 40, 17, 8.5, 35)) return fg(WHITE);
  if (insideRoundedRect(mx, my, 47, 44, 6, 38, 3)) return fg(WHITE);
  if (background === "rounded") return insideRoundedRect(x, y, 0, 0, 100, 100, 22) ? GREEN : null;
  if (background === "full") return GREEN;
  return null;
}

function render(size, options) {
  const ss = 4;
  const pixels = Buffer.alloc(size * size * 4);
  for (let py = 0; py < size; py++) {
    for (let px = 0; px < size; px++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < ss; sy++) {
        for (let sx = 0; sx < ss; sx++) {
          const color = sample(((px + (sx + 0.5) / ss) / size) * 100, ((py + (sy + 0.5) / ss) / size) * 100, options);
          if (!color) continue;
          r += color[0]; g += color[1]; b += color[2]; a += 1;
        }
      }
      const i = (py * size + px) * 4;
      if (a > 0) {
        pixels[i] = Math.round(r / a);
        pixels[i + 1] = Math.round(g / a);
        pixels[i + 2] = Math.round(b / a);
      }
      pixels[i + 3] = Math.round((a / (ss * ss)) * 255);
    }
  }
  return encodePng(size, pixels);
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

function encodePng(size, rgba) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // Bittiefe
  header[9] = 6; // RGBA
  const raw = Buffer.alloc((size * 4 + 1) * size);
  for (let y = 0; y < size; y++) rgba.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

const out = new URL("../public/icons/", import.meta.url);
const icons = [
  ["icon-192.png", 192, { background: "rounded", scale: 1 }],
  ["icon-512.png", 512, { background: "rounded", scale: 1 }],
  ["icon-maskable-512.png", 512, { background: "full", scale: 1.25 }],
  ["apple-touch-icon.png", 180, { background: "full", scale: 1.1 }],
  ["badge-96.png", 96, { background: "none", scale: 0.9, mono: true }],
];
for (const [name, size, options] of icons) {
  writeFileSync(new URL(name, out), render(size, options));
  console.log(`geschrieben: public/icons/${name}`);
}
