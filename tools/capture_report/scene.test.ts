import assert from 'node:assert/strict';
import { test } from 'node:test';
import { analyzeScene, backproject, DEFAULT_INTRINSICS, findPlanes, fitPlane, noiseByDistance, pixelStats, rng, type DepthCube, type Plane } from './scene.ts';
import { depthToRgb, encodePng, yuy2ToRgb } from './preview.ts';

const K = DEFAULT_INTRINSICS;
const W = 512;
const H = 424;

// Синтетическая комната: пол на 0,62 м ниже сенсора, потолок на 1,5 м выше, задняя стена на 4,5 м, правая стена на 1,8 м.
// Сенсор без наклона, координаты камеры: x вправо, y вверх, z вперёд.
const planes: Plane[] = [
  { n: [0, 1, 0], d: 0.62 }, // пол: y = -0.62
  { n: [0, -1, 0], d: 1.5 }, // потолок: y = 1.5
  { n: [0, 0, -1], d: 4.5 }, // задняя стена: z = 4.5
  { n: [-1, 0, 0], d: 1.8 }, // правая стена: x = 1.8
];

function makeCube(frames: number, noiseMm: number, seed = 1, floorOffsetMmPerFrame = 0): DepthCube {
  const r = rng(seed);
  const gauss = (): number => Math.sqrt(-2 * Math.log(r() + 1e-12)) * Math.cos(2 * Math.PI * r());
  const data = new Uint16Array(frames * W * H);
  for (let f = 0; f < frames; f++) {
    for (let v = 0; v < H; v++) {
      for (let u = 0; u < W; u++) {
        const ray = [(u - K.cx) / K.fx, -(v - K.cy) / K.fy, 1];
        let best = Infinity;
        for (const p of planes) {
          const dn = p.n[0] * ray[0] + p.n[1] * ray[1] + p.n[2] * ray[2];
          if (dn >= -1e-9) continue;
          const z = -(p.d + (p === planes[0] ? (floorOffsetMmPerFrame * f) / 1000 : 0)) / dn;
          if (z > 0.4 && z < best) best = z;
        }
        if (best === Infinity || best > 8) continue;
        data[f * W * H + v * W + u] = Math.round(best * 1000 + gauss() * noiseMm);
      }
    }
  }
  return { width: W, height: H, count: frames, data };
}

test('обратная проекция центра кадра лежит на оптической оси', () => {
  const p = backproject(K.cx, K.cy, 2000, K);
  assert.ok(Math.abs(p[0]) < 1e-9 && Math.abs(p[1]) < 1e-9);
  assert.equal(p[2], 2);
});

test('плоскость по точкам восстанавливается', () => {
  const pts = new Float32Array([0, -0.5, 1, 1, -0.5, 1, 0, -0.5, 2, 1, -0.5, 2, 0.5, -0.5, 1.5]);
  const pl = fitPlane(pts, [0, 1, 2, 3, 4]);
  assert.ok(Math.abs(Math.abs(pl.n[1]) - 1) < 1e-6);
  assert.ok(Math.abs(Math.abs(pl.d) - 0.5) < 1e-6);
});

test('findPlanes находит три главные плоскости куба точек', () => {
  const r = rng(3);
  const pts: number[] = [];
  for (let i = 0; i < 3000; i++) pts.push(r() * 3 - 1.5, -0.6 + (r() - 0.5) * 0.004, 1 + r() * 3);
  for (let i = 0; i < 3000; i++) pts.push(r() * 3 - 1.5, r() * 2 - 0.6, 4 + (r() - 0.5) * 0.004);
  for (let i = 0; i < 3000; i++) pts.push(1.5 + (r() - 0.5) * 0.004, r() * 2 - 0.6, 1 + r() * 3);
  const found = findPlanes(Float32Array.from(pts), 0.02, 5, 0.1);
  assert.equal(found.length, 3);
  const ds = found.map((f) => Math.abs(f.plane.d)).sort();
  assert.ok(Math.abs(ds[0] - 0.6) < 0.02);
});

test('шум по расстоянию совпадает с заданным у синтетических данных', () => {
  const c = makeCube(40, 3, 5);
  const bins = noiseByDistance(c, pixelStats(c));
  const all = bins.flatMap((b) => Array(b.pixels).fill(b.stdMmMedian));
  const med = all.sort((a, b) => a - b)[Math.floor(all.length / 2)];
  assert.ok(med > 2.4 && med < 3.6, 'медиана шума ' + med);
});

test('комната: пол, потолок, стены, высота помещения и сравнение с трекером', () => {
  const c = makeCube(40, 2, 9);
  const rep = analyzeScene(c, { intrinsics: K, floor: [0, 1, 0, 0.62] });
  const floor = rep.planes.find((p) => p.kind === 'floor');
  const ceiling = rep.planes.find((p) => p.kind === 'ceiling');
  const walls = rep.planes.filter((p) => p.kind === 'wall');
  assert.ok(floor && Math.abs(floor.distanceM - 0.62) < 0.01, 'пол');
  assert.ok(ceiling && Math.abs(ceiling.distanceM - 1.5) < 0.02, 'потолок');
  assert.ok(rep.roomHeightM !== null && Math.abs(rep.roomHeightM - 2.12) < 0.03, 'высота ' + rep.roomHeightM);
  assert.equal(walls.length, 2);
  assert.deepEqual(walls.map((w) => w.distanceM).sort((a, b) => a - b).map((v) => Math.round(v * 10) / 10), [1.8, 4.5]);
  assert.ok(rep.floorVsTracker && rep.floorVsTracker.normalAngleDeg < 0.5 && Math.abs(rep.floorVsTracker.heightDiffMm) < 15);
  const angle = rep.wallAnglesDeg[0].deg;
  assert.ok(Math.abs(angle - 90) < 1, 'угол между стенами ' + angle);
});

test('дрейф плоскости измеряется в мм в секунду', () => {
  const c = makeCube(60, 1, 11, 0.5); // пол уходит на 0,5 мм за кадр
  const rep = analyzeScene(c, { intrinsics: K, floor: [0, 1, 0, 0.62] }, 30);
  const floor = rep.planes.find((p) => p.kind === 'floor');
  assert.ok(floor && floor.driftMmPerS !== null);
  assert.ok(Math.abs(Math.abs(floor.driftMmPerS!) - 15) < 2, 'дрейф ' + floor.driftMmPerS);
});

test('PNG: сигнатура и размеры', () => {
  const png = encodePng(2, 2, new Uint8Array(12));
  assert.deepEqual([...png.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
  assert.equal(png.readUInt32BE(16), 2);
  assert.equal(png.readUInt32BE(20), 2);
});

test('превью: глубина ноль чёрная, YUY2 серый остаётся серым', () => {
  const rgb = depthToRgb(new Uint16Array([0, 1000]), 0, 2, 1);
  assert.deepEqual([...rgb.subarray(0, 3)], [0, 0, 0]);
  assert.ok(rgb[3] + rgb[4] + rgb[5] > 0);
  const gray = yuy2ToRgb(new Uint8Array([100, 128, 100, 128]), 0, 2, 1);
  assert.deepEqual([...gray], [100, 100, 100, 100, 100, 100]);
});
