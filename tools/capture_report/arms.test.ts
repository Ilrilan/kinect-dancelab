import assert from 'node:assert/strict';
import { test } from 'node:test';
import { armSamples, armToSensorAngleDeg } from './arms.ts';

test('кисть прямо на сенсор даёт угол около нуля, в сторону от сенсора около 180', () => {
  const shoulder = { x: 0, y: 0, z: 2 };
  assert.ok(armToSensorAngleDeg(shoulder, { x: 0, y: 0, z: 1.5 }) < 1);
  assert.ok(armToSensorAngleDeg(shoulder, { x: 0, y: 0, z: 2.5 }) > 179);
  const side = armToSensorAngleDeg(shoulder, { x: 0.5, y: 0, z: 2 });
  assert.ok(side > 85 && side < 95);
});

test('отсчёты руки: скачок локтя виден в скорости и в отклонении длины кости', () => {
  const frames = [];
  for (let i = 0; i < 30; i++) {
    const jump = i === 20 ? 0.3 : 0;
    frames.push({
      n: i,
      deviceTimeUs: i * 33333,
      hostTimeUtcTicks: i * 333330,
      users: [
        {
          id: 1,
          joints: [
            { name: 'ShoulderLeft', x: 0.2, y: 0.4, z: 2, state: 'tracked' },
            { name: 'ElbowLeft', x: 0.2 + jump, y: 0.1, z: 2, state: 'tracked' },
            { name: 'WristLeft', x: 0.2, y: -0.15, z: 2, state: 'tracked' },
          ],
        },
      ],
    });
  }
  const s = armSamples('kinect-sdk-2', frames as never, 'L');
  const spike = s.find((x) => Math.abs(x.t - 20 / 30) < 0.01);
  assert.ok(spike && spike.elbowSpeed > 5 && spike.upperDev > 3);
});
