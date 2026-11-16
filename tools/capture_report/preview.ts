// Превью записи глубины и цвета в PNG, чтобы увидеть сцену глазами: depth.png (расстояние цветом), color.png (YUY2 или RGB).
// Запуск: node tools/capture_report/preview.ts <папка прогона> [--frame 0] [--out <папка>]
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { deflateSync } from 'node:zlib';

function crc32(buf: Uint8Array): number {
  let c: number;
  let crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Uint8Array): Buffer {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const t = Buffer.from(type, 'latin1');
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([t, data])));
  return Buffer.concat([len, t, Buffer.from(data), crc]);
}

/** Кодирует RGB (3 байта на пиксель, строки сверху вниз) в PNG. */
export function encodePng(width: number, height: number, rgb: Uint8Array): Buffer {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 3 + 1)] = 0;
    Buffer.from(rgb.buffer, rgb.byteOffset + y * width * 3, width * 3).copy(raw, y * (width * 3 + 1) + 1);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 2;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', new Uint8Array(0)),
  ]);
}

const clamp = (v: number): number => (v < 0 ? 0 : v > 255 ? 255 : Math.round(v));

/** Глубина в цвет: ближе теплее, дальше холоднее, 0 чёрный. Диапазон 0,5 до 5 м. */
export function depthToRgb(depth: Uint16Array, offset: number, width: number, height: number): Uint8Array {
  const out = new Uint8Array(width * height * 3);
  for (let i = 0; i < width * height; i++) {
    const v = depth[offset + i];
    if (v === 0) continue;
    const t = Math.min(1, Math.max(0, (v - 500) / 4500));
    // от красного (близко) через жёлтый, зелёный к синему (далеко)
    const r = clamp(255 * Math.min(1, Math.max(0, 1.5 - 3 * t)));
    const g = clamp(255 * Math.min(1, Math.max(0, t < 0.5 ? 3 * t : 3 - 3 * t)));
    const b = clamp(255 * Math.min(1, Math.max(0, 3 * t - 1.5)));
    out[3 * i] = r;
    out[3 * i + 1] = g;
    out[3 * i + 2] = b;
  }
  return out;
}

export function yuy2ToRgb(yuy2: Uint8Array, offset: number, width: number, height: number): Uint8Array {
  const out = new Uint8Array(width * height * 3);
  for (let i = 0; i < (width * height) / 2; i++) {
    const y0 = yuy2[offset + 4 * i];
    const u = yuy2[offset + 4 * i + 1] - 128;
    const y1 = yuy2[offset + 4 * i + 2];
    const v = yuy2[offset + 4 * i + 3] - 128;
    for (let k = 0; k < 2; k++) {
      const y = k === 0 ? y0 : y1;
      const o = 3 * (2 * i + k);
      out[o] = clamp(y + 1.402 * v);
      out[o + 1] = clamp(y - 0.344136 * u - 0.714136 * v);
      out[o + 2] = clamp(y + 1.772 * u);
    }
  }
  return out;
}

function downscale(rgb: Uint8Array, w: number, h: number, factor: number): { rgb: Uint8Array; w: number; h: number } {
  const nw = Math.floor(w / factor);
  const nh = Math.floor(h / factor);
  const out = new Uint8Array(nw * nh * 3);
  for (let y = 0; y < nh; y++) for (let x = 0; x < nw; x++) for (let c = 0; c < 3; c++) out[3 * (y * nw + x) + c] = rgb[3 * (y * factor * w + x * factor) + c];
  return { rgb: out, w: nw, h: nh };
}

if (process.argv[1] && process.argv[1].endsWith('preview.ts')) {
  const dir = process.argv[2];
  if (!dir) {
    console.error('Использование: node tools/capture_report/preview.ts <папка прогона> [--frame 0] [--out <папка>]');
    process.exit(1);
  }
  const arg = (name: string, def: string): string => {
    const i = process.argv.indexOf(`--${name}`);
    return i >= 0 ? process.argv[i + 1] : def;
  };
  const frame = Number(arg('frame', '0'));
  const outDir = arg('out', dir);
  const run = JSON.parse(readFileSync(join(dir, 'run.json'), 'utf8'));
  const d = run.frames.depth;
  const depthBuf = readFileSync(join(dir, 'depth.bin'));
  const depth = new Uint16Array(depthBuf.buffer.slice(depthBuf.byteOffset, depthBuf.byteOffset + depthBuf.length));
  writeFileSync(join(outDir, 'depth.png'), encodePng(d.width, d.height, depthToRgb(depth, frame * d.width * d.height, d.width, d.height)));
  const c = run.frames.color;
  if (c && c.count > 0) {
    const colorBuf = readFileSync(join(dir, 'color.bin'));
    const frameBytes = c.frameBytes;
    const slice = new Uint8Array(colorBuf.buffer, colorBuf.byteOffset + Math.min(frame, c.count - 1) * frameBytes, frameBytes);
    let rgb: Uint8Array;
    if (c.format === 'Yuy2') rgb = yuy2ToRgb(slice, 0, c.width, c.height);
    else if (c.format === 'Rgb24') rgb = slice;
    else throw new Error('формат цвета не поддержан превью: ' + c.format);
    const small = downscale(rgb, c.width, c.height, 2);
    writeFileSync(join(outDir, 'color.png'), encodePng(small.w, small.h, small.rgb));
  }
  console.log('Записаны depth.png и color.png в', outDir);
}
