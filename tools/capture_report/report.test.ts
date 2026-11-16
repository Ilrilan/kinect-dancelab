import assert from 'node:assert/strict';
import { test } from 'node:test';
import { analyze, format, parse } from './report.ts';

function synth(frameCount: number, repeatPeriodS: number): string {
  const lines: string[] = [];
  lines.push(JSON.stringify({ kind: 'header', formatVersion: 1, tracker: { id: 'kinect-sdk-2', version: '2.0' }, sensor: { id: 's' }, recordedAtUtc: '2026-11-16T00:00:00.000Z', scenario: 'arms' }));
  for (let i = 0; i < frameCount; i++) {
    const t = i / 30;
    const lift = 0.5 + 0.4 * Math.sin((2 * Math.PI * t) / repeatPeriodS);
    const joint = (name: string, x: number, y: number) => ({ name, x, y, z: 2.5, state: 'tracked' });
    lines.push(JSON.stringify({
      kind: 'frame',
      n: i,
      deviceTimeUs: Math.round(t * 1e6),
      hostTimeUtcTicks: 637000000000000000 + Math.round(t * 1e7),
      users: [{ id: 1, joints: [
        joint('ShoulderLeft', -0.2, 1.4), joint('ElbowLeft', -0.3, 1.2), joint('WristLeft', -0.4, lift + 0.6),
        joint('ShoulderRight', 0.2, 1.4), joint('ElbowRight', 0.3, 1.2), joint('WristRight', 0.4, lift + 0.6),
      ] }],
      scene: { floorPlane: [0, 0.98, 0.17, -1.2] },
    }));
  }
  return lines.join('\n') + '\n';
}

test('частота и длительность считаются по часам устройства', () => {
  const { header, frames } = parse(synth(301, 4));
  const r = analyze(header, frames);
  assert.equal(r.frames, 301);
  assert.ok(Math.abs(r.durationS - 10) < 0.01);
  assert.ok(Math.abs(r.fpsDevice - 30) < 0.1);
  assert.equal(r.framesWithUser, 301);
});

test('находятся повторы движения и их период', () => {
  const { header, frames } = parse(synth(600, 4));
  const r = analyze(header, frames);
  assert.ok(r.repeatability);
  assert.ok(r.repeatability.peaks >= 4);
  assert.ok(Math.abs(r.repeatability.periodS.mean - 4) < 0.1);
  assert.ok(r.repeatability.periodS.std < 0.1);
});

test('плоскость пола, кости и состояния попадают в отчёт', () => {
  const { header, frames } = parse(synth(100, 4));
  const r = analyze(header, frames);
  assert.deepEqual(r.floorPlane?.mean.map((v) => Number(v.toFixed(2))), [0, 0.98, 0.17, -1.2]);
  assert.ok(r.boneLengthCm['upper-arm-L']);
  assert.equal(r.states.tracked, 1);
  assert.equal(r.shoulderSignChanges, 0);
  assert.match(format(r), /Плоскость пола/);
});

test('смена сторон плеч считается', () => {
  const lines = synth(10, 4).split('\n').filter((l) => l);
  const out = [lines[0]];
  for (let i = 1; i < lines.length; i++) {
    const f = JSON.parse(lines[i]);
    if (i > 5) {
      for (const j of f.users[0].joints) j.x = -j.x;
    }
    out.push(JSON.stringify(f));
  }
  const { header, frames } = parse(out.join('\n'));
  assert.equal(analyze(header, frames).shoulderSignChanges, 1);
});

test('нестатичный файл без людей не падает', () => {
  const header = JSON.stringify({ kind: 'header', formatVersion: 1, tracker: { id: 'nuitrack', version: '1' }, sensor: { id: 's' }, recordedAtUtc: '2026-11-16T00:00:00.000Z', scenario: 'empty' });
  const fr = JSON.stringify({ kind: 'frame', n: 0, deviceTimeUs: 0, hostTimeUtcTicks: 1, users: [] });
  const { header: h, frames } = parse(header + '\n' + fr + '\n');
  const r = analyze(h, frames);
  assert.equal(r.framesWithUser, 0);
  assert.equal(r.repeatability, null);
});
