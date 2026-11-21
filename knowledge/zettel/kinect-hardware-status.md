---
name: kinect-hardware-status
description: "Kinect v2 владельца: драйверы стоят, обрывы потока вызывал Voice Clarity на микрофоне сенсора, решено 2026-11-16"
metadata:
  node_type: memory
  type: project
  originSessionId: 21a2b4dc-a66d-4bad-a785-b00e04b48d0e
  modified: 2026-11-16T17:33:35.513Z
---

Kinect v2 работает на ноутбуке владельца (ASUS ROG Flow X13, Windows 11, контроллеры AMD USB 3.10). Kinect SDK 2.0 (1409) установлен 2026-11-16. Обрывы потока каждые 3-14 секунд (сенсор перезапускался, лампочка гасла) вызывал эффект Voice Clarity на микрофоне Xbox NUI Sensor. Владелец отключил «Улучшение звука» для этого входа в Параметры, Звук, Ввод: обрывы пропали (его слова, 2026-11-16).

**Why:** запись, чтобы не искать причину заново при смене компьютера, переустановке или обновлении Windows.

**How to apply:** после установки драйвера Kinect на любой машине сразу отключать улучшение звука на микрофоне Xbox NUI Sensor. Обновления Windows могут включить эффект снова. Разбор в `docs/kinect-v2-dropouts.md`. Версия SDK, установленная в `C:\Program Files\Microsoft SDKs\Kinect\v2.0_1409`. Сенсор исправен. См. [[general-working-rules]].
