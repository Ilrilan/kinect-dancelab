// Ведёт по набору записей из specs/capture/index.md: показывает инструкцию, ждёт Enter, запускает sensor_probe.
// Запуск: node tools/sensor_probe/session.ts --tracker kinect|nuitrack [--only <номер,номер>] [--countdown 5]
// Перед запуском соберите решение: dotnet build DanceLab.sln
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createInterface } from 'node:readline/promises';

interface Step {
  id: string;
  scenario: string;
  mode: 'skeleton' | 'depthcolor';
  duration: number;
  text: string;
}

export const STEPS: Step[] = [
  { id: '1', scenario: 'static-front', mode: 'skeleton', duration: 10, text: 'Встаньте лицом к сенсору на расстоянии 2,5 м, ступни на ширине плеч, руки вдоль тела. Стойте неподвижно 10 секунд.' },
  { id: '2', scenario: 'static-side45', mode: 'skeleton', duration: 10, text: 'Повернитесь боком на 45 градусов к сенсору. Стойте неподвижно 10 секунд.' },
  { id: '3', scenario: 'static-side90', mode: 'skeleton', duration: 10, text: 'Повернитесь боком на 90 градусов к сенсору. Стойте неподвижно 10 секунд.' },
  { id: '4', scenario: 'turn180', mode: 'skeleton', duration: 14, text: 'Лицом к сенсору. После отсчёта медленно, равномерно развернитесь на месте на 180 градусов примерно за 10 секунд (спиной к сенсору к концу записи).' },
  { id: '5', scenario: 'arms-raise-x6', mode: 'skeleton', duration: 30, text: 'Лицом к сенсору, руки вдоль тела. Метроном на телефоне 60 ударов в минуту. 3 секунды неподвижно, затем 6 раз: 2 удара поднять руки в стороны до горизонтали (T), 2 удара опустить. После последнего повтора 3 секунды неподвижно.' },
  { id: '6', scenario: 'half-squat-x6', mode: 'skeleton', duration: 30, text: 'Лицом к сенсору, ступни на ширине плеч. Метроном 60 ударов в минуту. 3 секунды неподвижно, затем 6 раз: 2 удара полуприсед (колени до 90 градусов, спина прямая, руки вперёд), 2 удара встать. После этого 3 секунды неподвижно.' },
  { id: '7', scenario: 'side-step', mode: 'skeleton', duration: 14, text: 'Лицом к сенсору. Медленно шаг влево на 1 метр и обратно вправо за 10 секунд.' },
  { id: '8', scenario: 'depthcolor-empty', mode: 'depthcolor', duration: 8, text: 'Выйдите из кадра. Запись глубины и цвета пустой комнаты, 8 секунд (для анализа сцены: пол и стены).' },
  { id: '9', scenario: 'depthcolor-person', mode: 'depthcolor', duration: 8, text: 'Встаньте лицом к сенсору, руки вдоль тела. Запись глубины и цвета с человеком, 8 секунд.' },
];

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const opt = (name: string, def: string): string => {
    const i = args.indexOf(`--${name}`);
    return i >= 0 && args[i + 1] ? args[i + 1] : def;
  };
  const tracker = opt('tracker', '');
  if (tracker !== 'kinect' && tracker !== 'nuitrack') {
    console.error('Использование: node tools/sensor_probe/session.ts --tracker kinect|nuitrack [--only 1,2] [--countdown 5]');
    process.exit(1);
  }
  const exe = 'tools/sensor_probe/bin/x64/Debug/net48/sensor_probe.exe';
  if (!existsSync(exe)) {
    console.error(`Нет ${exe}. Соберите: dotnet build DanceLab.sln`);
    process.exit(1);
  }
  const only = opt('only', '').split(',').filter(Boolean);
  const countdown = opt('countdown', '5');
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  for (const step of STEPS.filter((s) => only.length === 0 || only.includes(s.id))) {
    console.log(`\n=== Запись ${step.id}: ${step.scenario} (${step.duration} с) ===`);
    console.log(step.text);
    const answer = await rl.question('Когда будете готовы, нажмите Enter (s пропустить, q выйти): ');
    if (answer.trim() === 'q') break;
    if (answer.trim() === 's') continue;
    const r = spawnSync(exe, ['--tracker', tracker, '--scenario', step.scenario, '--mode', step.mode, '--duration', String(step.duration), '--countdown', countdown], { stdio: 'inherit' });
    if (r.status !== 0) {
      console.error(`Прогон ${step.id} завершился с кодом ${r.status}.`);
      const again = await rl.question('Продолжить следующими записями? (y/n): ');
      if (again.trim().toLowerCase() !== 'y') break;
    }
  }
  rl.close();
}

if (process.argv[1] && process.argv[1].endsWith('session.ts')) {
  await main();
}
