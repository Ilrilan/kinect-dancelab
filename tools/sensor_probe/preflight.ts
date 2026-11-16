// Проверка готовности к записи замеров: сенсор виден системе, улучшение звука выключено, достаточно памяти, консоль собрана.
// Запуск: node tools/sensor_probe/preflight.ts
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';

interface Check {
  name: string;
  ok: boolean;
  detail: string;
}

function powershell(script: string): string {
  const r = spawnSync('powershell', ['-NoProfile', '-Command', '[Console]::OutputEncoding=[Text.Encoding]::UTF8; ' + script], { encoding: 'utf8' });
  return (r.stdout ?? '').trim();
}

const checks: Check[] = [];

const devices = powershell(
  "Get-PnpDevice -PresentOnly | Where-Object { $_.FriendlyName -match 'Xbox NUI Sensor|WDF KinectSensor' } | ForEach-Object { $_.FriendlyName + '=' + $_.Status }",
)
  .split(/\r?\n/)
  .filter(Boolean);
checks.push({
  name: 'Kinect виден системе',
  ok: devices.some((d) => d.startsWith('Xbox NUI Sensor') && d.endsWith('OK')) && devices.some((d) => d.startsWith('WDF KinectSensor')),
  detail: devices.join('; ') || 'устройств нет (проверьте USB и питание)',
});

const fx = powershell(
  "$base='HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\MMDevices\\Audio\\Capture'; foreach($k in Get-ChildItem $base){ $p=Get-ItemProperty ($k.PSPath + '\\Properties') -ErrorAction SilentlyContinue; if($p -and (($p.PSObject.Properties.Value | Where-Object { $_ -is [string] }) -match 'Xbox NUI')){ $f=Get-ItemProperty ($k.PSPath + '\\FxProperties') -ErrorAction SilentlyContinue; $v=$f.'{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5'; 'endpoint ' + $k.PSChildName + ' disableSysFx=' + $v } }",
);
checks.push({
  name: 'Улучшение звука у Xbox NUI Sensor выключено',
  ok: /disableSysFx=1/.test(fx),
  detail: fx || 'микрофон сенсора не найден в реестре',
});

const free = Number(powershell('[math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)').replace(',', '.'));
checks.push({ name: 'Свободно не менее 6 ГБ памяти', ok: free >= 6, detail: `${free} ГБ` });

const exe = 'tools/sensor_probe/bin/x64/Debug/net48/sensor_probe.exe';
checks.push({ name: 'Консоль собрана', ok: existsSync(exe), detail: existsSync(exe) ? exe : 'выполните dotnet build DanceLab.sln' });

const busy = powershell(
  "Get-Process | Where-Object { $_.Name -match '^(KStudio|Nuitrack|nuitrack_sample|KinectV2ConfigurationVerifier)$' } | ForEach-Object { $_.Name }",
);
checks.push({ name: 'Сенсор не занят другими программами', ok: busy === '', detail: busy || 'нет' });

let failed = 0;
for (const c of checks) {
  console.log(`${c.ok ? 'OK ' : 'НЕТ'} ${c.name}: ${c.detail}`);
  if (!c.ok) failed++;
}
process.exit(failed === 0 ? 0 : 1);
