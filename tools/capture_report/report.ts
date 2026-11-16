// Сводка по записи замеров слоя захвата (skeleton.jsonl).
// Запуск: node tools/capture_report/report.ts <skeleton.jsonl> [--json]
// Считает: частоту кадров, долю кадров с человеком, шум суставов, стабильность длин костей,
// доли состояний и уверенности, плоскость пола, смены левого и правого плеча (переворот), повторяемость движения рук.
import { readFileSync } from 'node:fs';

export interface Joint {
  name: string;
  x: number;
  y: number;
  z: number;
  conf?: number | null;
  state?: string;
}
export interface Frame {
  n: number;
  deviceTimeUs: number;
  hostTimeUtcTicks: number;
  users: { id: number; joints: Joint[] }[];
  scene?: { floorPlane?: number[]; other?: Record<string, unknown> };
}
export interface Header {
  tracker: { id: string; version: string; engine?: string };
  sensor: { id: string; serial?: string };
  scenario: string;
  recordedAtUtc: string;
}

export function parse(text: string): { header: Header; frames: Frame[] } {
  const lines = text.split(/\r?\n/).filter((l) => l.length > 0);
  const header = JSON.parse(lines[0]) as Header & { kind: string };
  if (header.kind !== 'header') throw new Error('первая строка не header');
  const frames = lines.slice(1).map((l) => JSON.parse(l) as Frame);
  return { header, frames };
}

const mean = (a: number[]): number => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : NaN);
const std = (a: number[]): number => {
  if (a.length < 2) return NaN;
  const m = mean(a);
  return Math.sqrt(a.reduce((s, v) => s + (v - m) ** 2, 0) / (a.length - 1));
};

// Кости для проверки стабильности длины: пары имён суставов у каждого трекера.
const BONES: Record<string, [string, string, string][]> = {
  'kinect-sdk-2': [
    ['upper-arm-L', 'ShoulderLeft', 'ElbowLeft'],
    ['forearm-L', 'ElbowLeft', 'WristLeft'],
    ['upper-arm-R', 'ShoulderRight', 'ElbowRight'],
    ['forearm-R', 'ElbowRight', 'WristRight'],
    ['thigh-L', 'HipLeft', 'KneeLeft'],
    ['shin-L', 'KneeLeft', 'AnkleLeft'],
    ['thigh-R', 'HipRight', 'KneeRight'],
    ['shin-R', 'KneeRight', 'AnkleRight'],
  ],
  nuitrack: [
    ['upper-arm-L', 'LeftShoulder', 'LeftElbow'],
    ['forearm-L', 'LeftElbow', 'LeftWrist'],
    ['upper-arm-R', 'RightShoulder', 'RightElbow'],
    ['forearm-R', 'RightElbow', 'RightWrist'],
    ['thigh-L', 'LeftHip', 'LeftKnee'],
    ['shin-L', 'LeftKnee', 'LeftAnkle'],
    ['thigh-R', 'RightHip', 'RightKnee'],
    ['shin-R', 'RightKnee', 'RightAnkle'],
  ],
};
const SHOULDERS: Record<string, [string, string]> = {
  'kinect-sdk-2': ['ShoulderLeft', 'ShoulderRight'],
  nuitrack: ['LeftShoulder', 'RightShoulder'],
};
const WRISTS: Record<string, [string, string]> = {
  'kinect-sdk-2': ['WristLeft', 'WristRight'],
  nuitrack: ['LeftWrist', 'RightWrist'],
};

function dist(a: Joint, b: Joint): number {
  return Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
}

export interface Report {
  tracker: string;
  version: string;
  engine: string | null;
  scenario: string;
  frames: number;
  durationS: number;
  fpsDevice: number;
  dtDeviceMs: { mean: number; std: number; max: number };
  fpsHost: number;
  framesWithUser: number;
  jointNoiseCm: Record<string, { x: number; y: number; z: number }>;
  boneLengthCm: Record<string, { mean: number; std: number }>;
  states: Record<string, number>;
  confMean: number | null;
  floorPlane: { mean: number[]; std: number[] } | null;
  shoulderSignChanges: number;
  repeatability: { peaks: number; periodS: { mean: number; std: number }; peakHeightCm: { mean: number; std: number } } | null;
}

export function analyze(header: Header, frames: Frame[]): Report {
  const tracker = header.tracker.id;
  const dt: number[] = [];
  for (let i = 1; i < frames.length; i++) dt.push((frames[i].deviceTimeUs - frames[i - 1].deviceTimeUs) / 1000);
  const durationS = frames.length > 1 ? (frames[frames.length - 1].deviceTimeUs - frames[0].deviceTimeUs) / 1e6 : 0;
  const hostS = frames.length > 1 ? (frames[frames.length - 1].hostTimeUtcTicks - frames[0].hostTimeUtcTicks) / 1e7 : 0;

  // первый человек в кадре
  const people = frames.map((f) => f.users[0]).filter((u) => u !== undefined);
  const byJoint = new Map<string, Joint[]>();
  for (const u of people)
    for (const j of u.joints) {
      const arr = byJoint.get(j.name) ?? [];
      arr.push(j);
      byJoint.set(j.name, arr);
    }
  const jointNoiseCm: Report['jointNoiseCm'] = {};
  for (const [name, arr] of byJoint) {
    jointNoiseCm[name] = { x: std(arr.map((j) => j.x)) * 100, y: std(arr.map((j) => j.y)) * 100, z: std(arr.map((j) => j.z)) * 100 };
  }

  const boneLengthCm: Report['boneLengthCm'] = {};
  for (const [label, a, b] of BONES[tracker] ?? []) {
    const lens: number[] = [];
    for (const u of people) {
      const ja = u.joints.find((j) => j.name === a);
      const jb = u.joints.find((j) => j.name === b);
      if (ja && jb) lens.push(dist(ja, jb) * 100);
    }
    if (lens.length) boneLengthCm[label] = { mean: mean(lens), std: std(lens) };
  }

  const states: Record<string, number> = {};
  const confs: number[] = [];
  let total = 0;
  for (const u of people)
    for (const j of u.joints) {
      total++;
      const s = j.state ?? 'none';
      states[s] = (states[s] ?? 0) + 1;
      if (typeof j.conf === 'number') confs.push(j.conf);
    }
  for (const k of Object.keys(states)) states[k] = states[k] / total;

  const planes = frames.map((f) => f.scene?.floorPlane).filter((p): p is number[] => Array.isArray(p) && p.length === 4);
  const floorPlane = planes.length
    ? { mean: [0, 1, 2, 3].map((i) => mean(planes.map((p) => p[i]))), std: [0, 1, 2, 3].map((i) => std(planes.map((p) => p[i]))) }
    : null;

  // смена знака разности x плеч: признак переворота левой и правой сторон
  let signChanges = 0;
  const sh = SHOULDERS[tracker];
  if (sh) {
    let prev = 0;
    for (const u of people) {
      const l = u.joints.find((j) => j.name === sh[0]);
      const r = u.joints.find((j) => j.name === sh[1]);
      if (!l || !r) continue;
      const s = Math.sign(l.x - r.x);
      if (s !== 0 && prev !== 0 && s !== prev) signChanges++;
      if (s !== 0) prev = s;
    }
  }

  // повторяемость движения рук: пики средней высоты запястий
  let repeatability: Report['repeatability'] = null;
  const wr = WRISTS[tracker];
  if (wr) {
    const series: { t: number; y: number }[] = [];
    for (let i = 0; i < frames.length; i++) {
      const u = frames[i].users[0];
      if (!u) continue;
      const l = u.joints.find((j) => j.name === wr[0]);
      const r = u.joints.find((j) => j.name === wr[1]);
      if (l && r) series.push({ t: frames[i].deviceTimeUs / 1e6, y: (l.y + r.y) / 2 });
    }
    if (series.length > 10) {
      const ys = series.map((s) => s.y);
      const lo = Math.min(...ys);
      const hi = Math.max(...ys);
      if (hi - lo > 0.15) {
        const mid = (lo + hi) / 2;
        const peaks: { t: number; y: number }[] = [];
        let above = false;
        let best: { t: number; y: number } | null = null;
        for (const s of series) {
          if (s.y > mid) {
            above = true;
            if (!best || s.y > best.y) best = s;
          } else if (above) {
            if (best) peaks.push(best);
            best = null;
            above = false;
          }
        }
        if (peaks.length >= 2) {
          const periods = peaks.slice(1).map((p, i) => p.t - peaks[i].t);
          repeatability = {
            peaks: peaks.length,
            periodS: { mean: mean(periods), std: std(periods) },
            peakHeightCm: { mean: mean(peaks.map((p) => p.y * 100)), std: std(peaks.map((p) => p.y * 100)) },
          };
        }
      }
    }
  }

  return {
    tracker,
    version: header.tracker.version,
    engine: header.tracker.engine ?? null,
    scenario: header.scenario,
    frames: frames.length,
    durationS,
    fpsDevice: durationS > 0 ? (frames.length - 1) / durationS : NaN,
    dtDeviceMs: { mean: mean(dt), std: std(dt), max: dt.length ? Math.max(...dt) : NaN },
    fpsHost: hostS > 0 ? (frames.length - 1) / hostS : NaN,
    framesWithUser: frames.filter((f) => f.users.length > 0).length,
    jointNoiseCm,
    boneLengthCm,
    states,
    confMean: confs.length ? mean(confs) : null,
    floorPlane,
    shoulderSignChanges: signChanges,
    repeatability,
  };
}

const f1 = (v: number): string => (Number.isFinite(v) ? v.toFixed(1) : 'n/a');
const f2 = (v: number): string => (Number.isFinite(v) ? v.toFixed(2) : 'n/a');

export function format(r: Report): string {
  const out: string[] = [];
  out.push(`Трекер: ${r.tracker} ${r.version}${r.engine ? ' (' + r.engine + ')' : ''}, сценарий: ${r.scenario}`);
  out.push(`Кадров: ${r.frames}, длительность: ${f1(r.durationS)} с, частота по часам устройства: ${f1(r.fpsDevice)} кадр/с, по часам хоста: ${f1(r.fpsHost)}`);
  out.push(`Интервал между кадрами, мс: среднее ${f1(r.dtDeviceMs.mean)}, разброс ${f1(r.dtDeviceMs.std)}, максимум ${f1(r.dtDeviceMs.max)}`);
  out.push(`Кадров с человеком: ${r.framesWithUser} из ${r.frames}`);
  out.push(
    `Состояния суставов: ${
      Object.entries(r.states)
        .map(([k, v]) => `${k} ${(v * 100).toFixed(1)}%`)
        .join(', ') || 'n/a'
    }${r.confMean !== null ? `, уверенность в среднем ${f2(r.confMean)}` : ''}`,
  );
  const noise = Object.entries(r.jointNoiseCm);
  if (noise.length) {
    const allStd = noise.map(([, v]) => Math.hypot(v.x, v.y, v.z));
    out.push(
      `Шум положения сустава (std по трём осям, см): среднее по суставам ${f2(allStd.reduce((a, b) => a + b, 0) / allStd.length)}, худший ${noise[allStd.indexOf(Math.max(...allStd))][0]} ${f2(Math.max(...allStd))}`,
    );
  }
  const bones = Object.entries(r.boneLengthCm);
  if (bones.length) {
    out.push('Длины костей, см (среднее, std):');
    for (const [k, v] of bones) out.push(`  ${k}: ${f1(v.mean)}, ${f2(v.std)}`);
  }
  out.push(
    r.floorPlane
      ? `Плоскость пола a,b,c,d: ${r.floorPlane.mean.map(f2).join(', ')} (std ${r.floorPlane.std.map(f2).join(', ')})`
      : 'Плоскость пола: трекер не отдаёт',
  );
  out.push(`Смен знака разности плеч по x (переворот сторон): ${r.shoulderSignChanges}`);
  out.push(
    r.repeatability
      ? `Повторы движения рук: пиков ${r.repeatability.peaks}, период ${f2(r.repeatability.periodS.mean)} с (std ${f2(r.repeatability.periodS.std)}), высота пика ${f1(r.repeatability.peakHeightCm.mean)} см (std ${f2(r.repeatability.peakHeightCm.std)})`
      : 'Повторы движения рук: не найдены (мало размаха или кадров)',
  );
  return out.join('\n');
}

if (process.argv[1] && process.argv[1].endsWith('report.ts')) {
  const path = process.argv[2];
  if (!path) {
    console.error('Использование: node tools/capture_report/report.ts <skeleton.jsonl> [--json]');
    process.exit(1);
  }
  const { header, frames } = parse(readFileSync(path, 'utf8'));
  const r = analyze(header, frames);
  console.log(process.argv.includes('--json') ? JSON.stringify(r, null, 2) : format(r));
}
