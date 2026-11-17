// Анализ рук в записи skeleton.jsonl: как ведут себя плечо, локоть и кисть, когда рука направлена на сенсор.
// Запуск: node tools/capture_report/arms.ts <путь к skeleton.jsonl>
import { readFileSync } from 'node:fs';
import { parse, type Frame, type Joint } from './report.ts';

export interface Vec {
  x: number;
  y: number;
  z: number;
}

const sub = (a: Vec, b: Vec): Vec => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const len = (a: Vec): number => Math.hypot(a.x, a.y, a.z);
const dot = (a: Vec, b: Vec): number => a.x * b.x + a.y * b.y + a.z * b.z;

/** Угол в градусах между осью руки (плечо→кисть) и направлением от плеча к сенсору (начало координат). 0 означает: кисть направлена прямо на сенсор. */
export function armToSensorAngleDeg(shoulder: Vec, wrist: Vec): number {
  const arm = sub(wrist, shoulder);
  const toSensor = { x: -shoulder.x, y: -shoulder.y, z: -shoulder.z };
  const c = dot(arm, toSensor) / (len(arm) * len(toSensor) || 1);
  return (Math.acos(Math.max(-1, Math.min(1, c))) * 180) / Math.PI;
}

const NAMES: Record<string, Record<'L' | 'R', [string, string, string]>> = {
  'kinect-sdk-2': { L: ['ShoulderLeft', 'ElbowLeft', 'WristLeft'], R: ['ShoulderRight', 'ElbowRight', 'WristRight'] },
  nuitrack: { L: ['LeftShoulder', 'LeftElbow', 'LeftWrist'], R: ['RightShoulder', 'RightElbow', 'RightWrist'] },
};

const missing = (j: Joint): boolean => j.conf === 0 || (Math.abs(j.x) < 0.01 && Math.abs(j.y) < 0.01 && Math.abs(j.z) < 0.01);
const median = (a: number[]): number => {
  const s = [...a].sort((p, q) => p - q);
  return s.length ? s[Math.floor(s.length / 2)] : 0;
};

export interface ArmSample {
  t: number;
  angle: number;
  upperDev: number;
  foreDev: number;
  elbowSpeed: number;
  wristSpeed: number;
  elbowWeak: boolean;
  wristWeak: boolean;
  elbowConf: number | null;
  /** Остаток положения локтя после сглаживания окном из пяти кадров, мм. */
  elbowJitterMm: number;
  wristJitterMm: number;
}

const jitterMm = (pts: Vec[], i: number): number => {
  if (i < 2 || i + 2 >= pts.length) return 0;
  const m = { x: 0, y: 0, z: 0 };
  for (let k = i - 2; k <= i + 2; k++) {
    m.x += pts[k].x / 5;
    m.y += pts[k].y / 5;
    m.z += pts[k].z / 5;
  }
  return len(sub(pts[i], m)) * 1000;
};

export function armSamples(trackerId: string, frames: Frame[], side: 'L' | 'R'): ArmSample[] {
  const [sn, en, wn] = NAMES[trackerId][side];
  const rows: { t: number; s: Joint; e: Joint; w: Joint }[] = [];
  const t0 = frames[0].deviceTimeUs;
  for (const f of frames) {
    const u = f.users[0];
    if (!u) continue;
    const s = u.joints.find((j) => j.name === sn);
    const e = u.joints.find((j) => j.name === en);
    const w = u.joints.find((j) => j.name === wn);
    if (!s || !e || !w || missing(s) || missing(e) || missing(w)) continue;
    rows.push({ t: (f.deviceTimeUs - t0) / 1e6, s, e, w });
  }
  const upperMed = median(rows.map((r) => len(sub(r.e, r.s))));
  const foreMed = median(rows.map((r) => len(sub(r.w, r.e))));
  const out: ArmSample[] = [];
  const elbows = rows.map((r) => r.e);
  const wrists = rows.map((r) => r.w);
  for (let i = 1; i < rows.length; i++) {
    const r = rows[i];
    const p = rows[i - 1];
    const dt = r.t - p.t;
    if (dt <= 0) continue;
    out.push({
      t: r.t,
      angle: armToSensorAngleDeg(r.s, r.w),
      upperDev: Math.abs(len(sub(r.e, r.s)) - upperMed) * 100,
      foreDev: Math.abs(len(sub(r.w, r.e)) - foreMed) * 100,
      elbowSpeed: len(sub(r.e, p.e)) / dt,
      wristSpeed: len(sub(r.w, p.w)) / dt,
      elbowWeak: r.e.state === 'inferred' || (typeof r.e.conf === 'number' && r.e.conf < 0.3),
      wristWeak: r.w.state === 'inferred' || (typeof r.w.conf === 'number' && r.w.conf < 0.3),
      elbowConf: typeof r.e.conf === 'number' ? r.e.conf : null,
      elbowJitterMm: jitterMm(elbows, i),
      wristJitterMm: jitterMm(wrists, i),
    });
  }
  return out;
}

const mean = (a: number[]): number => (a.length ? a.reduce((x, y) => x + y, 0) / a.length : 0);
const f1 = (x: number): string => x.toFixed(1);

/** Только кадры с движением (кисть быстрее 0,15 м/с): в покое все показатели малы и разбавляют средние. */
export function binReport(samples: ArmSample[]): string[] {
  const bins: [number, number][] = [
    [0, 20],
    [20, 40],
    [40, 70],
    [70, 180],
  ];
  const moving = samples.filter((x) => x.wristSpeed > 0.15);
  const lines: string[] = [
    `  кадров с движением: ${moving.length} из ${samples.length}`,
    '  угол руки к лучу | кадров | дрожание локтя, мм | дрожание кисти, мм | откл. длины плеча, см | предплечья, см | локоть достроен или уверенность низкая',
  ];
  for (const [lo, hi] of bins) {
    const s = moving.filter((x) => x.angle >= lo && x.angle < hi);
    if (s.length < 5) continue;
    const weak = (s.filter((x) => x.elbowWeak).length / s.length) * 100;
    lines.push(
      `  ${String(lo).padStart(3)}-${String(hi).padEnd(3)} град      | ${String(s.length).padStart(6)} | ${f1(mean(s.map((x) => x.elbowJitterMm))).padStart(10)}         | ${f1(mean(s.map((x) => x.wristJitterMm))).padStart(10)}         | ${f1(mean(s.map((x) => x.upperDev))).padStart(9)}             | ${f1(mean(s.map((x) => x.foreDev))).padStart(8)}       | ${weak.toFixed(0)}%`,
    );
  }
  return lines;
}

export function timeline(samples: ArmSample[], stepS = 3): string[] {
  const lines: string[] = ['  время, с | угол к лучу | откл. плеча см | откл. предпл. см | скорость локтя | скорость кисти'];
  const end = samples.length ? samples[samples.length - 1].t : 0;
  for (let t = 0; t < end; t += stepS) {
    const s = samples.filter((x) => x.t >= t && x.t < t + stepS);
    if (!s.length) continue;
    lines.push(
      `  ${String(t).padStart(4)}-${String(t + stepS).padEnd(4)} | ${f1(mean(s.map((x) => x.angle))).padStart(6)}     | ${f1(mean(s.map((x) => x.upperDev))).padStart(8)}       | ${f1(
        mean(s.map((x) => x.foreDev)),
      ).padStart(8)}         | ${f1(mean(s.map((x) => x.elbowSpeed))).padStart(6)}         | ${f1(mean(s.map((x) => x.wristSpeed))).padStart(6)}`,
    );
  }
  return lines;
}

function main(): void {
  const path = process.argv[2];
  if (!path) {
    console.error('Использование: node tools/capture_report/arms.ts <skeleton.jsonl>');
    process.exit(1);
  }
  const { header, frames } = parse(readFileSync(path, 'utf8'));
  const id = header.tracker.id;
  console.log(`Трекер: ${id}, кадров: ${frames.length}`);
  for (const side of ['L', 'R'] as const) {
    const s = armSamples(id, frames, side);
    console.log(`\nРука ${side === 'L' ? 'левая' : 'правая'} (кадров с полной рукой: ${s.length})`);
    for (const l of binReport(s)) console.log(l);
    console.log('');
    for (const l of timeline(s)) console.log(l);
  }
}

if (process.argv[1] && process.argv[1].endsWith('arms.ts')) main();
