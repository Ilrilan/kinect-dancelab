// Агрегация сводки FineDance (summary.csv из tools/aist_import/finedance_summary.py): жанры, длительность, интенсивность.
// Запуск: node tools/finedance/summary-agg.ts <summary.csv>
import { readFileSync } from 'node:fs';

interface Row {
  id: string;
  song: string;
  style1: string;
  style2: string;
  sec: number;
  speed_mean: number;
  speed_p95: number;
  low_frac: number;
  travel_per_min: number;
}

const NUMERIC = new Set(['sec', 'speed_mean', 'speed_p95', 'low_frac', 'travel_per_min']);

function readRows(path: string): Row[] {
  const lines = readFileSync(path, 'utf8')
    .split('\n')
    .map((s) => s.replace('\r', ''))
    .filter((s) => s.length > 0);
  const header = lines[0].split(',');
  return lines.slice(1).map((line) => {
    const cells = line.split(',');
    const o: Record<string, string | number> = {};
    header.forEach((key, i) => {
      o[key] = NUMERIC.has(key) ? Number(cells[i]) : cells[i];
    });
    return o as unknown as Row;
  });
}

const quantile = (a: number[], p: number): number => {
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.floor(p * s.length))];
};
const sum = (a: number[]): number => a.reduce((x, y) => x + y, 0);
const fmt = (x: number, d = 2): string => x.toFixed(d);

const path = process.argv[2];
if (!path) {
  console.error('Использование: node tools/finedance/summary-agg.ts <summary.csv>');
  process.exit(1);
}
const rows = readRows(path);

console.log('style1 x style2 (файлов, мин, ср. с, ср. скорость конечностей м/с, p95 скорости, доля низких кадров)');
const groups = new Map<string, Row[]>();
for (const r of rows) {
  const key = `${r.style1}/${r.style2}`;
  groups.set(key, [...(groups.get(key) ?? []), r]);
}
for (const [key, v] of [...groups.entries()].sort((a, b) => b[1].length - a[1].length)) {
  const secs = v.map((r) => r.sec);
  console.log(
    key.padEnd(22),
    String(v.length).padStart(3),
    fmt(sum(secs) / 60, 0).padStart(4),
    fmt(sum(secs) / v.length, 0).padStart(4),
    fmt(sum(v.map((r) => r.speed_mean)) / v.length).padStart(5),
    fmt(sum(v.map((r) => r.speed_p95)) / v.length).padStart(5),
    fmt(sum(v.map((r) => r.low_frac)) / v.length, 3),
  );
}

const speeds = rows.map((r) => r.speed_mean);
console.log('\nскорость конечностей, средняя по файлу: p10/25/50/75/90', [0.1, 0.25, 0.5, 0.75, 0.9].map((p) => fmt(quantile(speeds, p))).join(' '));
const terciles = [quantile(speeds, 1 / 3), quantile(speeds, 2 / 3)];
console.log(
  'терцили',
  terciles.map((x) => fmt(x)),
);
const level = (r: Row): string => (r.speed_mean < terciles[0] ? 'низкая' : r.speed_mean < terciles[1] ? 'средняя' : 'высокая');
console.log('\nстиль1 по терцилям интенсивности (низкая/средняя/высокая):');
for (const s of new Set(rows.map((r) => r.style1))) {
  const v = rows.filter((r) => r.style1 === s);
  console.log(s.padEnd(8), ['низкая', 'средняя', 'высокая'].map((t) => v.filter((r) => level(r) === t).length).join('/'));
}

const secs = rows.map((r) => r.sec);
console.log(
  '\nдлительность, с: p10/50/90',
  [0.1, 0.5, 0.9].map((p) => quantile(secs, p)).join('/'),
  ' коротких <60 с:',
  rows.filter((r) => r.sec < 60).length,
  ' 60-120:',
  rows.filter((r) => r.sec >= 60 && r.sec < 120).length,
  ' 120-240:',
  rows.filter((r) => r.sec >= 120 && r.sec < 240).length,
  ' >=240:',
  rows.filter((r) => r.sec >= 240).length,
);
console.log(
  'работа на полу (low_frac>0.1):',
  rows
    .filter((r) => r.low_frac > 0.1)
    .map((r) => `${r.id}:${r.style1}`)
    .join(' '),
);
console.log(
  'перемещение, м/мин: p10/50/90',
  [0.1, 0.5, 0.9]
    .map((p) =>
      fmt(
        quantile(
          rows.map((r) => r.travel_per_min),
          p,
        ),
        1,
      ),
    )
    .join('/'),
);
console.log(
  'топ-5 интенсивных:',
  [...rows]
    .sort((a, b) => b.speed_mean - a.speed_mean)
    .slice(0, 5)
    .map((r) => `${r.id} ${r.song} ${r.style1}/${r.style2} ${fmt(r.speed_mean)}`)
    .join('; '),
);
console.log(
  'топ-5 спокойных:',
  [...rows]
    .sort((a, b) => a.speed_mean - b.speed_mean)
    .slice(0, 5)
    .map((r) => `${r.id} ${r.song} ${r.style1}/${r.style2} ${fmt(r.speed_mean)}`)
    .join('; '),
);
