// Скачивание видео AIST для последовательностей, у которых есть motions.
// Запуск (Node 24, типы вырезаются автоматически):
//   node tools/aist_download/download-videos.ts [--bitrate 10M] [--concurrency 3]
// Входы (все внутри data/aist, в git не попадают):
//   lists/motions_have.txt              имена последовательностей из motions.zip
//   lists/refined_<bitrate>_all_video_url.csv   список ссылок AIST
// Выход: video/<bitrate>/<имя>.mp4. Повторный запуск пропускает готовое.
// Перед запуском прочитайте условия: https://aistdancedb.ongaaccel.jp/terms_of_use/

import { createWriteStream, existsSync, mkdirSync, readFileSync, renameSync, statSync, unlinkSync, appendFileSync } from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { basename, join } from 'node:path';

const args = process.argv.slice(2);
const opt = (name: string, def: string): string => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] ? args[i + 1] : def;
};
const bitrate = opt('bitrate', '10M');
const concurrency = Number(opt('concurrency', '3'));
const root = join(process.cwd(), 'data', 'aist');
const outDir = join(root, 'video', bitrate);
const logPath = join(root, `download-${bitrate}.log`);
mkdirSync(outDir, { recursive: true });

const lines = (path: string): string[] =>
  readFileSync(path, 'utf8').split(/\r?\n/).map((s) => s.trim()).filter(Boolean);

// gBR_sBM_cAll_d04_mBR0_ch01 -> gBR_sBM_c##_d04_mBR0_ch01 (девять ракурсов)
const wanted = new Set(lines(join(root, 'lists', 'motions_have.txt')).map((n) => n.replace('_cAll_', '_c##_')));
const urls = lines(join(root, 'lists', `refined_${bitrate}_all_video_url.csv`)).filter((u) => {
  const name = basename(u, '.mp4').replace(/_c0[1-9]_/, '_c##_');
  return wanted.has(name);
});

const log = (msg: string): void => {
  const line = `${new Date().toISOString()} ${msg}`;
  appendFileSync(logPath, line + '\n');
};

let done = 0;
let skipped = 0;
let failed = 0;
const total = urls.length;
log(`start: ${total} videos, bitrate ${bitrate}, concurrency ${concurrency}`);

async function fetchOne(url: string): Promise<void> {
  const file = join(outDir, basename(url));
  if (existsSync(file) && statSync(file).size > 0) {
    skipped++;
    return;
  }
  const part = file + '.part';
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const res = await fetch(url);
      if (!res.ok || !res.body) {
        if (res.status === 404) {
          failed++;
          log(`404 ${basename(url)}`);
          return;
        }
        throw new Error(`HTTP ${res.status}`);
      }
      await pipeline(Readable.fromWeb(res.body as never), createWriteStream(part));
      const expected = Number(res.headers.get('content-length') ?? 0);
      if (expected && statSync(part).size !== expected) throw new Error('size mismatch');
      renameSync(part, file);
      done++;
      if ((done + skipped + failed) % 25 === 0) log(`progress ${done + skipped + failed}/${total} (new ${done}, skipped ${skipped}, failed ${failed})`);
      return;
    } catch (e) {
      if (existsSync(part)) unlinkSync(part);
      if (attempt === 3) {
        failed++;
        log(`FAIL ${basename(url)}: ${(e as Error).message}`);
      } else {
        await new Promise((r) => setTimeout(r, 2000 * attempt));
      }
    }
  }
}

let next = 0;
async function worker(): Promise<void> {
  while (next < urls.length) {
    const url = urls[next++];
    await fetchOne(url);
  }
}

await Promise.all(Array.from({ length: concurrency }, worker));
log(`finished: new ${done}, skipped ${skipped}, failed ${failed}, total ${total}`);
