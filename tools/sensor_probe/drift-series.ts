// Серия коротких записей глубины и цвета с паузами для оценки дрейфа при прогреве сенсора.
// Запуск: node tools/sensor_probe/drift-series.ts --tracker kinect [--runs 10] [--gap 150] [--duration 10]
// Между прогонами сенсор закрывается, пауза в секундах. Результаты: data/captures/<время>_<трекер>_drift-N.
import { spawnSync } from 'node:child_process';

const args = process.argv.slice(2);
const opt = (name: string, def: string): string => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] ? args[i + 1] : def;
};
const tracker = opt('tracker', 'kinect');
const runs = Number(opt('runs', '10'));
const gap = Number(opt('gap', '150'));
const duration = opt('duration', '10');
const exe = 'tools/sensor_probe/bin/x64/Debug/net48/sensor_probe.exe';

for (let i = 1; i <= runs; i++) {
  const started = new Date().toISOString();
  const r = spawnSync(exe, ['--tracker', tracker, '--scenario', `drift-${String(i).padStart(2, '0')}`, '--mode', 'depthcolor', '--duration', duration, '--countdown', '0'], { stdio: 'inherit' });
  console.log(`${started} прогон ${i}/${runs} код ${r.status}`);
  if (r.status === 3) break;
  if (i < runs) await new Promise((resolve) => setTimeout(resolve, gap * 1000));
}
