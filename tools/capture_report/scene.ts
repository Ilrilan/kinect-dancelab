// Анализ сцены по записи глубины (папка прогона depthcolor): шум датчика, плоскости пола и стен, стабильность неподвижных поверхностей.
// Запуск: node tools/capture_report/scene.ts <папка прогона> [--json]
// Нужны run.json, depth.bin; skeleton.jsonl даёт параметры камеры и плоскость пола трекера для сравнения.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

export interface Intrinsics {
  fx: number;
  fy: number;
  cx: number;
  cy: number;
}
export interface DepthCube {
  width: number;
  height: number;
  count: number;
  data: Uint16Array; // count * width * height, миллиметры, 0 = нет данных
}

// Типовые параметры камеры глубины Kinect v2, если в записи нет своих.
export const DEFAULT_INTRINSICS: Intrinsics = { fx: 366.0, fy: 366.0, cx: 256.0, cy: 212.0 };

export interface Plane {
  n: [number, number, number]; // единичная нормаль
  d: number; // n·p + d = 0, метры
}

export function readDepth(dir: string): DepthCube {
  const run = JSON.parse(readFileSync(join(dir, 'run.json'), 'utf8'));
  const info = run.frames?.depth;
  if (!info || !info.count) throw new Error('в run.json нет кадров глубины');
  const buf = readFileSync(join(dir, 'depth.bin'));
  const expected = info.count * info.width * info.height * 2;
  if (buf.length !== expected) throw new Error(`depth.bin: ${buf.length} байт, ожидалось ${expected}`);
  const data = new Uint16Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.length));
  return { width: info.width, height: info.height, count: info.count, data };
}

export function readSceneHints(dir: string): { intrinsics: Intrinsics | null; floor: number[] | null } {
  const p = join(dir, 'skeleton.jsonl');
  if (!existsSync(p)) return { intrinsics: null, floor: null };
  let intrinsics: Intrinsics | null = null;
  const floors: number[][] = [];
  for (const line of readFileSync(p, 'utf8').split(/\r?\n/)) {
    if (!line || !line.includes('"frame"')) continue;
    const f = JSON.parse(line);
    const o = f.scene?.other;
    if (!intrinsics && o && typeof o.depthFx === 'number') intrinsics = { fx: o.depthFx, fy: o.depthFy, cx: o.depthCx, cy: o.depthCy };
    if (Array.isArray(f.scene?.floorPlane)) floors.push(f.scene.floorPlane);
  }
  const floor = floors.length ? [0, 1, 2, 3].map((i) => floors.reduce((s, p2) => s + p2[i], 0) / floors.length) : null;
  return { intrinsics, floor };
}

export interface PixelStats {
  mean: Float32Array;
  std: Float32Array;
  validFrac: Float32Array;
}

export function pixelStats(c: DepthCube): PixelStats {
  const n = c.width * c.height;
  const mean = new Float32Array(n);
  const m2 = new Float64Array(n);
  const cnt = new Uint32Array(n);
  const mu = new Float64Array(n);
  for (let f = 0; f < c.count; f++) {
    const base = f * n;
    for (let i = 0; i < n; i++) {
      const v = c.data[base + i];
      if (v === 0) continue;
      cnt[i]++;
      const delta = v - mu[i];
      mu[i] += delta / cnt[i];
      m2[i] += delta * (v - mu[i]);
    }
  }
  const std = new Float32Array(n);
  const validFrac = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    mean[i] = mu[i];
    std[i] = cnt[i] > 1 ? Math.sqrt(m2[i] / (cnt[i] - 1)) : 0;
    validFrac[i] = cnt[i] / c.count;
  }
  return { mean, std, validFrac };
}

export interface NoiseBin {
  fromM: number;
  toM: number;
  pixels: number;
  stdMmMedian: number;
  stdMmP90: number;
}

export function noiseByDistance(c: DepthCube, s: PixelStats, binM = 0.5, minValid = 0.9): NoiseBin[] {
  const bins = new Map<number, number[]>();
  for (let i = 0; i < s.mean.length; i++) {
    if (s.validFrac[i] < minValid) continue;
    const b = Math.floor(s.mean[i] / 1000 / binM);
    const arr = bins.get(b) ?? [];
    arr.push(s.std[i]);
    bins.set(b, arr);
  }
  const q = (a: number[], p: number): number => a.sort((x, y) => x - y)[Math.min(a.length - 1, Math.floor(p * a.length))];
  return [...bins.entries()].sort((a, b) => a[0] - b[0]).map(([b, arr]) => ({
    fromM: b * binM,
    toM: (b + 1) * binM,
    pixels: arr.length,
    stdMmMedian: q(arr, 0.5),
    stdMmP90: q(arr, 0.9),
  }));
}

export function backproject(u: number, v: number, zMm: number, k: Intrinsics): [number, number, number] {
  const z = zMm / 1000;
  return [((u - k.cx) / k.fx) * z, (-(v - k.cy) / k.fy) * z, z];
}

function cross(a: number[], b: number[]): [number, number, number] {
  return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}

// Собственный вектор наименьшего собственного значения симметричной матрицы 3x3 (метод Якоби).
function smallestEigenvector(m: number[][]): [number, number, number] {
  const a = m.map((r) => r.slice());
  const v = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
  for (let sweep = 0; sweep < 30; sweep++) {
    let off = 0;
    for (let i = 0; i < 3; i++) for (let j = i + 1; j < 3; j++) off += a[i][j] ** 2;
    if (off < 1e-24) break;
    for (let p = 0; p < 2; p++) for (let q = p + 1; q < 3; q++) {
      if (Math.abs(a[p][q]) < 1e-30) continue;
      const theta = (a[q][q] - a[p][p]) / (2 * a[p][q]);
      const t = Math.sign(theta || 1) / (Math.abs(theta) + Math.sqrt(theta * theta + 1));
      const c = 1 / Math.sqrt(t * t + 1);
      const s = t * c;
      for (let k = 0; k < 3; k++) {
        const akp = a[k][p];
        const akq = a[k][q];
        a[k][p] = c * akp - s * akq;
        a[k][q] = s * akp + c * akq;
      }
      for (let k = 0; k < 3; k++) {
        const apk = a[p][k];
        const aqk = a[q][k];
        a[p][k] = c * apk - s * aqk;
        a[q][k] = s * apk + c * aqk;
      }
      for (let k = 0; k < 3; k++) {
        const vkp = v[k][p];
        const vkq = v[k][q];
        v[k][p] = c * vkp - s * vkq;
        v[k][q] = s * vkp + c * vkq;
      }
    }
  }
  let best = 0;
  for (let i = 1; i < 3; i++) if (a[i][i] < a[best][best]) best = i;
  return [v[0][best], v[1][best], v[2][best]];
}

export function fitPlane(pts: Float32Array, idx: number[]): Plane {
  let cx = 0;
  let cy = 0;
  let cz = 0;
  for (const i of idx) {
    cx += pts[3 * i];
    cy += pts[3 * i + 1];
    cz += pts[3 * i + 2];
  }
  cx /= idx.length;
  cy /= idx.length;
  cz /= idx.length;
  const m = [[0, 0, 0], [0, 0, 0], [0, 0, 0]];
  for (const i of idx) {
    const d = [pts[3 * i] - cx, pts[3 * i + 1] - cy, pts[3 * i + 2] - cz];
    for (let r = 0; r < 3; r++) for (let q = 0; q < 3; q++) m[r][q] += d[r] * d[q];
  }
  const n = smallestEigenvector(m);
  return { n, d: -(n[0] * cx + n[1] * cy + n[2] * cz) };
}

// Генератор псевдослучайных чисел с зерном: результат воспроизводим.
export function rng(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6d2b79f5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export interface FoundPlane {
  plane: Plane;
  inlierIdx: number[];
  inliers: number;
  share: number; // доля от всех точек
  rmsMm: number;
}

export function findPlanes(pts: Float32Array, thresholdM = 0.02, maxPlanes = 10, minShare = 0.02, seed = 7): FoundPlane[] {
  const total = pts.length / 3;
  const rand = rng(seed);
  let alive: number[] = Array.from({ length: total }, (_, i) => i);
  const out: FoundPlane[] = [];
  for (let k = 0; k < maxPlanes && alive.length > total * minShare; k++) {
    let best: Plane | null = null;
    let bestCount = 0;
    for (let it = 0; it < 400; it++) {
      const a = alive[Math.floor(rand() * alive.length)];
      const b = alive[Math.floor(rand() * alive.length)];
      const c = alive[Math.floor(rand() * alive.length)];
      if (a === b || b === c || a === c) continue;
      const ab = [pts[3 * b] - pts[3 * a], pts[3 * b + 1] - pts[3 * a + 1], pts[3 * b + 2] - pts[3 * a + 2]];
      const ac = [pts[3 * c] - pts[3 * a], pts[3 * c + 1] - pts[3 * a + 1], pts[3 * c + 2] - pts[3 * a + 2]];
      const nn = cross(ab, ac);
      const len = Math.hypot(nn[0], nn[1], nn[2]);
      if (len < 1e-9) continue;
      const n: [number, number, number] = [nn[0] / len, nn[1] / len, nn[2] / len];
      const d = -(n[0] * pts[3 * a] + n[1] * pts[3 * a + 1] + n[2] * pts[3 * a + 2]);
      let count = 0;
      for (const i of alive) if (Math.abs(n[0] * pts[3 * i] + n[1] * pts[3 * i + 1] + n[2] * pts[3 * i + 2] + d) < thresholdM) count++;
      if (count > bestCount) {
        bestCount = count;
        best = { n, d };
      }
    }
    if (!best || bestCount < total * minShare) break;
    // уточнение по найденным точкам, затем пересчёт точек плоскости
    let inl = alive.filter((i) => Math.abs(best!.n[0] * pts[3 * i] + best!.n[1] * pts[3 * i + 1] + best!.n[2] * pts[3 * i + 2] + best!.d) < thresholdM);
    const refined = fitPlane(pts, inl);
    inl = alive.filter((i) => Math.abs(refined.n[0] * pts[3 * i] + refined.n[1] * pts[3 * i + 1] + refined.n[2] * pts[3 * i + 2] + refined.d) < thresholdM);
    const fin = fitPlane(pts, inl);
    let ss = 0;
    for (const i of inl) ss += (fin.n[0] * pts[3 * i] + fin.n[1] * pts[3 * i + 1] + fin.n[2] * pts[3 * i + 2] + fin.d) ** 2;
    out.push({ plane: fin, inlierIdx: inl, inliers: inl.length, share: inl.length / total, rmsMm: Math.sqrt(ss / inl.length) * 1000 });
    const dead = new Set(inl);
    alive = alive.filter((i) => !dead.has(i));
  }
  return out;
}

export function meanCloud(c: DepthCube, s: PixelStats, k: Intrinsics, step = 3, minValid = 0.9, minRangeMm = 400): { pts: Float32Array; px: number[] } {
  const pts: number[] = [];
  const px: number[] = [];
  for (let v = 0; v < c.height; v += step) {
    for (let u = 0; u < c.width; u += step) {
      const i = v * c.width + u;
      if (s.validFrac[i] < minValid || s.mean[i] < minRangeMm) continue;
      const p = backproject(u, v, s.mean[i], k);
      pts.push(p[0], p[1], p[2]);
      px.push(i);
    }
  }
  return { pts: Float32Array.from(pts), px };
}

/** Протяжённость плоскости (5-95 процентиль по двум осям в плоскости): вертикаль относительно up и горизонталь. */
export function planeExtent(pts: Float32Array, f: FoundPlane, up: number[]): [number, number] {
  const n = f.plane.n;
  const upn = Math.abs(up[0] * n[0] + up[1] * n[1] + up[2] * n[2]);
  const ref = upn > 0.95 ? [0, 0, 1] : up;
  const k = ref[0] * n[0] + ref[1] * n[1] + ref[2] * n[2];
  let u = [ref[0] - k * n[0], ref[1] - k * n[1], ref[2] - k * n[2]];
  const ul = Math.hypot(u[0], u[1], u[2]);
  u = u.map((x) => x / ul);
  const h = cross(n, u);
  const us: number[] = [];
  const hs: number[] = [];
  for (const i of f.inlierIdx) {
    us.push(pts[3 * i] * u[0] + pts[3 * i + 1] * u[1] + pts[3 * i + 2] * u[2]);
    hs.push(pts[3 * i] * h[0] + pts[3 * i + 1] * h[1] + pts[3 * i + 2] * h[2]);
  }
  const span = (a: number[]): number => {
    a.sort((x, y) => x - y);
    return a[Math.floor(a.length * 0.95)] - a[Math.floor(a.length * 0.05)];
  };
  return [span(hs), span(us)];
}

const MIN_EXTENT_M = 0.8;
const dot = (a: number[], b: number[]): number => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const angleDeg = (a: number[], b: number[]): number => (Math.acos(Math.min(1, Math.abs(dot(a, b)))) * 180) / Math.PI;

export interface SceneReport {
  frames: number;
  size: [number, number];
  intrinsics: Intrinsics;
  intrinsicsFromTracker: boolean;
  invalidPixelShare: number;
  noise: NoiseBin[];
  floorFromTracker: { normal: number[]; heightM: number } | null;
  planes: {
    kind: 'floor' | 'ceiling' | 'wall' | 'surface';
    extentM: [number, number];
    normal: number[];
    distanceM: number;
    share: number;
    rmsMm: number;
    angleToTrackerFloorDeg: number | null;
    driftMmPerS: number | null;
    perFrameOffsetStdMm: number | null;
  }[];
  floorVsTracker: { normalAngleDeg: number; heightDiffMm: number } | null;
  roomHeightM: number | null;
  wallAnglesDeg: { a: number; b: number; deg: number }[];
}

export function analyzeScene(c: DepthCube, hints: { intrinsics: Intrinsics | null; floor: number[] | null }, fps = 30, minRangeM = 1.0): SceneReport {
  const k = hints.intrinsics ?? DEFAULT_INTRINSICS;
  const s = pixelStats(c);
  const n = c.width * c.height;
  let invalid = 0;
  for (let i = 0; i < n; i++) if (s.validFrac[i] < 0.5) invalid++;
  const { pts } = meanCloud(c, s, k, 3, 0.9, minRangeM * 1000);
  const found = findPlanes(pts);

  let floorN: number[] | null = null;
  let floorH: number | null = null;
  if (hints.floor) {
    const l = Math.hypot(hints.floor[0], hints.floor[1], hints.floor[2]);
    floorN = [hints.floor[0] / l, hints.floor[1] / l, hints.floor[2] / l];
    floorH = Math.abs(hints.floor[3]) / l;
  }

  const classified = found.map((f) => {
    // нормаль направляем от плоскости к сенсору (d > 0), чтобы сравнивать направления одинаково
    const flip = f.plane.d < 0 ? -1 : 1;
    const normal = f.plane.n.map((x) => x * flip);
    const dist = Math.abs(f.plane.d);
    let kind: 'floor' | 'ceiling' | 'wall' | 'surface' = 'surface';
    const ref = floorN ?? [0, 1, 0];
    const cosToUp = dot(normal, ref);
    const extent = planeExtent(pts, f, ref);
    const big = extent[0] >= MIN_EXTENT_M && extent[1] >= MIN_EXTENT_M;
    if (big && cosToUp > 0.94) kind = 'floor';
    else if (big && cosToUp < -0.94) kind = 'ceiling';
    else if (big && Math.abs(cosToUp) < 0.26) kind = 'wall';
    return { f, normal, dist, kind, extent, angleToTrackerFloor: floorN ? angleDeg(normal, floorN) : null };
  });

  // стабильность неподвижных поверхностей: смещение плоскости по каждому кадру относительно среднего положения
  const planes = classified.map((cl) => {
    const idx: number[] = [];
    const g = meanCloud(c, s, k, 3, 0.9, minRangeM * 1000);
    for (let i = 0; i < g.px.length; i++) {
      const x = g.pts[3 * i];
      const y = g.pts[3 * i + 1];
      const z = g.pts[3 * i + 2];
      if (Math.abs(cl.f.plane.n[0] * x + cl.f.plane.n[1] * y + cl.f.plane.n[2] * z + cl.f.plane.d) < 0.02) idx.push(i);
    }
    const offsets: number[] = [];
    const times: number[] = [];
    for (let f = 0; f < c.count; f += Math.max(1, Math.floor(c.count / 60))) {
      let sum = 0;
      let cnt = 0;
      for (const i of idx) {
        const v = c.data[f * n + g.px[i]];
        if (v === 0) continue;
        const p = backproject(g.px[i] % c.width, Math.floor(g.px[i] / c.width), v, k);
        sum += cl.f.plane.n[0] * p[0] + cl.f.plane.n[1] * p[1] + cl.f.plane.n[2] * p[2] + cl.f.plane.d;
        cnt++;
      }
      if (cnt > 20) {
        offsets.push((sum / cnt) * 1000);
        times.push(f / fps);
      }
    }
    let drift: number | null = null;
    let offStd: number | null = null;
    if (offsets.length > 3) {
      const mt = times.reduce((a, b) => a + b, 0) / times.length;
      const mo = offsets.reduce((a, b) => a + b, 0) / offsets.length;
      let num = 0;
      let den = 0;
      for (let i = 0; i < times.length; i++) {
        num += (times[i] - mt) * (offsets[i] - mo);
        den += (times[i] - mt) ** 2;
      }
      drift = den > 0 ? num / den : 0;
      offStd = Math.sqrt(offsets.reduce((a, b) => a + (b - mo) ** 2, 0) / (offsets.length - 1));
    }
    return {
      kind: cl.kind,
      extentM: [Number(cl.extent[0].toFixed(2)), Number(cl.extent[1].toFixed(2))] as [number, number],
      normal: cl.normal.map((x) => Number(x.toFixed(4))),
      distanceM: Number(cl.dist.toFixed(3)),
      share: Number(cl.f.share.toFixed(3)),
      rmsMm: Number(cl.f.rmsMm.toFixed(1)),
      angleToTrackerFloorDeg: cl.angleToTrackerFloor === null ? null : Number(cl.angleToTrackerFloor.toFixed(2)),
      driftMmPerS: drift === null ? null : Number(drift.toFixed(3)),
      perFrameOffsetStdMm: offStd === null ? null : Number(offStd.toFixed(3)),
    };
  });

  const floorPlane = classified.find((cl) => cl.kind === 'floor');
  const ceilingPlane = classified.find((cl) => cl.kind === 'ceiling');
  const walls = classified.filter((cl) => cl.kind === 'wall');
  const wallAngles: SceneReport['wallAnglesDeg'] = [];
  for (let i = 0; i < walls.length; i++) for (let j = i + 1; j < walls.length; j++) wallAngles.push({ a: i, b: j, deg: Number(angleDeg(walls[i].normal, walls[j].normal).toFixed(2)) });

  return {
    frames: c.count,
    size: [c.width, c.height],
    intrinsics: k,
    intrinsicsFromTracker: hints.intrinsics !== null,
    invalidPixelShare: Number((invalid / n).toFixed(3)),
    noise: noiseByDistance(c, s),
    floorFromTracker: floorN && floorH !== null ? { normal: floorN.map((x) => Number(x.toFixed(4))), heightM: Number(floorH.toFixed(3)) } : null,
    planes,
    roomHeightM: floorPlane && ceilingPlane ? Number((floorPlane.dist + ceilingPlane.dist).toFixed(3)) : null,
    floorVsTracker: floorPlane && floorN && floorH !== null ? { normalAngleDeg: Number(angleDeg(floorPlane.normal, floorN).toFixed(2)), heightDiffMm: Number(((floorPlane.dist - floorH) * 1000).toFixed(1)) } : null,
    wallAnglesDeg: wallAngles,
  };
}

export function formatScene(r: SceneReport): string {
  const out: string[] = [];
  out.push(`Кадров глубины: ${r.frames}, ${r.size[0]}x${r.size[1]}, параметры камеры ${r.intrinsicsFromTracker ? 'от трекера' : 'типовые'}: fx ${r.intrinsics.fx.toFixed(1)}, fy ${r.intrinsics.fy.toFixed(1)}, cx ${r.intrinsics.cx.toFixed(1)}, cy ${r.intrinsics.cy.toFixed(1)}`);
  out.push(`Пикселей без данных больше чем в половине кадров: ${(r.invalidPixelShare * 100).toFixed(1)}%`);
  out.push('Шум глубины (std по времени у неподвижных пикселей), мм:');
  for (const b of r.noise) out.push(`  ${b.fromM.toFixed(1)}-${b.toM.toFixed(1)} м: медиана ${b.stdMmMedian.toFixed(2)}, 90-й процентиль ${b.stdMmP90.toFixed(2)}, пикселей ${b.pixels}`);
  out.push(r.floorFromTracker ? `Пол по данным трекера: нормаль ${r.floorFromTracker.normal.join(', ')}, высота сенсора ${r.floorFromTracker.heightM} м` : 'Пол по данным трекера: нет');
  out.push('Найденные плоскости (по глубине):');
  for (const p of r.planes) {
    out.push(`  ${p.kind}: протяжённость ${p.extentM[0]} на ${p.extentM[1]} м, расстояние ${p.distanceM} м, доля точек ${(p.share * 100).toFixed(1)}%, невязка ${p.rmsMm} мм, нормаль ${p.normal.join(', ')}${p.angleToTrackerFloorDeg !== null ? `, угол к полу трекера ${p.angleToTrackerFloorDeg}°` : ''}`);
    if (p.driftMmPerS !== null) out.push(`      стабильность: дрейф ${p.driftMmPerS} мм/с, разброс по кадрам ${p.perFrameOffsetStdMm} мм`);
  }
  out.push(r.floorVsTracker ? `Пол по глубине против трекера: угол между нормалями ${r.floorVsTracker.normalAngleDeg}°, разница высоты ${r.floorVsTracker.heightDiffMm} мм` : 'Пол по глубине не найден или нет данных трекера для сравнения');
  if (r.roomHeightM !== null) out.push(`Высота помещения (пол плюс потолок по глубине): ${r.roomHeightM} м`);
  for (const a of r.wallAnglesDeg) out.push(`Угол между стенами ${a.a + 1} и ${a.b + 1}: ${a.deg}°`);
  return out.join('\n');
}

if (process.argv[1] && process.argv[1].endsWith('scene.ts')) {
  const dir = process.argv[2];
  if (!dir) {
    console.error('Использование: node tools/capture_report/scene.ts <папка прогона> [--json]');
    process.exit(1);
  }
  const report = analyzeScene(readDepth(dir), readSceneHints(dir));
  console.log(process.argv.includes('--json') ? JSON.stringify(report, null, 2) : formatScene(report));
}
