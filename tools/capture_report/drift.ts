// Дрейф сцены между прогонами: как меняются расстояния до пола, потолка и стен и шум глубины по мере прогрева сенсора.
// Запуск: node tools/capture_report/drift.ts <папка captures> <шаблон имени, например drift-> [--json]
// Берёт прогоны с именем, содержащим шаблон, в порядке времени, считает analyzeScene для каждого.
import { readdirSync } from 'node:fs';
import { join } from 'node:path';
import { analyzeScene, readDepth, readSceneHints, type SceneReport } from './scene.ts';

export interface DriftRow {
  run: string;
  tMin: number;
  floorM: number | null;
  ceilingM: number | null;
  farWallM: number | null;
  noise1to2MmMedian: number | null;
  trackerFloorM: number | null;
}

export function summarize(run: string, tMin: number, r: SceneReport): DriftRow {
  const floor = r.planes.find((p) => p.kind === 'floor');
  const ceiling = r.planes.find((p) => p.kind === 'ceiling');
  const walls = r.planes.filter((p) => p.kind === 'wall').sort((a, b) => b.distanceM - a.distanceM);
  const near = r.noise.filter((b) => b.fromM >= 1 && b.toM <= 2);
  const noise = near.length ? near.reduce((s, b) => s + b.stdMmMedian * b.pixels, 0) / near.reduce((s, b) => s + b.pixels, 0) : null;
  return {
    run,
    tMin,
    floorM: floor ? floor.distanceM : null,
    ceilingM: ceiling ? ceiling.distanceM : null,
    farWallM: walls.length ? walls[0].distanceM : null,
    noise1to2MmMedian: noise === null ? null : Number(noise.toFixed(2)),
    trackerFloorM: r.floorFromTracker ? r.floorFromTracker.heightM : null,
  };
}

export function slopePerMin(rows: DriftRow[], key: 'floorM' | 'ceilingM' | 'farWallM' | 'noise1to2MmMedian'): number | null {
  const pts = rows.filter((r) => r[key] !== null).map((r) => [r.tMin, r[key] as number]);
  if (pts.length < 3) return null;
  const mt = pts.reduce((s, p) => s + p[0], 0) / pts.length;
  const mv = pts.reduce((s, p) => s + p[1], 0) / pts.length;
  let num = 0;
  let den = 0;
  for (const [t, v] of pts) {
    num += (t - mt) * (v - mv);
    den += (t - mt) ** 2;
  }
  return den > 0 ? num / den : null;
}

if (process.argv[1] && process.argv[1].endsWith('drift.ts')) {
  const root = process.argv[2];
  const pattern = process.argv[3];
  if (!root || !pattern) {
    console.error('Использование: node tools/capture_report/drift.ts <папка captures> <шаблон> [--json]');
    process.exit(1);
  }
  const dirs = readdirSync(root)
    .filter((d) => d.includes(pattern))
    .sort();
  if (dirs.length === 0) {
    console.error('Нет прогонов с шаблоном ' + pattern);
    process.exit(1);
  }
  const parseT = (d: string): number => {
    const m = /^(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})/.exec(d);
    return m ? Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]) / 60000 : 0;
  };
  const t0 = parseT(dirs[0]);
  const rows: DriftRow[] = [];
  for (const d of dirs) {
    const dir = join(root, d);
    try {
      rows.push(summarize(d, Number((parseT(d) - t0).toFixed(1)), analyzeScene(readDepth(dir), readSceneHints(dir))));
    } catch (e) {
      console.error(`Пропущен ${d}: ${(e as Error).message}`);
    }
  }
  if (process.argv.includes('--json')) {
    console.log(JSON.stringify(rows, null, 2));
  } else {
    console.log('прогон                              мин   пол,м  потолок,м  дальняя стена,м  шум 1-2 м, мм');
    for (const r of rows)
      console.log(
        `${r.run.padEnd(34)} ${String(r.tMin).padStart(5)}  ${String(r.floorM).padStart(6)}  ${String(r.ceilingM).padStart(9)}  ${String(r.farWallM).padStart(15)}  ${String(r.noise1to2MmMedian).padStart(12)}`,
      );
    for (const k of ['floorM', 'ceilingM', 'farWallM', 'noise1to2MmMedian'] as const) {
      const s = slopePerMin(rows, k);
      console.log(`Наклон ${k}: ${s === null ? 'n/a' : k === 'noise1to2MmMedian' ? s.toFixed(3) + ' мм/мин' : (s * 1000).toFixed(2) + ' мм/мин'}`);
    }
  }
}
