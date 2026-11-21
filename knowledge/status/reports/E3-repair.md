# E3: ремонт сцен пробного плеера

E3: старт 2026-11-22 01:00

## Осмотр (снимки E2 глазами)
- scene: в правой панели «вид сверху (x, z)» подпись в левом верхнем углу наложена на осевую метку «5 м» — подтверждено.
- probe: HUD («без сенсора / Время / пол: оцениваю») перекрывает левую часть подписи «вид спереди (x, y), метки сетки 1 м», торчит только хвост — подтверждено.
- Topology.ScreenXSign в player_trial уже есть (Topology.cs:19, Kinect −1 / прочие +1); сенсорному SkeletonCanvas соответствует map x → cx + sign·x·scale.
- PlayerSession.TrackerId — string («kinect-sdk-2» | «nuitrack»).

## Решение
1. SceneCanvas top: подпись панели — к правому верхнему углу (правее меток оси), фронтовая остаётся слева (конфликта нет).
2. GhostCanvas: подпись панели — к правому верхнему углу, вне зоны HUD (HUD anchвер слева).
3. SceneCanvas: свойство TrackerId (по умолчанию Topology.Kinect → знак −1 → текущий вид без зеркала), map через Topology.ScreenXSign(trackerId); MainWindow.UpdateScene передаёт s?.TrackerId ?? Topology.Kinect в обе панели.

## Правки (01:10)
- SceneCanvas.cs: свойство TrackerId (по умолчанию Topology.Kinect → знак −1 = прежний вид без зеркала), DrawFront/DrawTop через Topology.ScreenXSign(TrackerId); помощник MakeText; подпись top-панели — в правый верхний угол.
- GhostCanvas.cs: подпись панели — в правый верхний угол (HUD слева больше её не накрывает); помощник MakeText.
- MainWindow.cs: UpdateScene передаёт s?.TrackerId ?? Topology.Kinect в sceneFront и sceneTop.

## Верификация (01:20)
- dotnet build DanceLab.sln: успешно, 0 предупреждений / 0 ошибок.
- dotnet test tests/DanceLab.Core.Tests: 33/33 пройдено, 0 упало.
- data/preview/trial-shot-e3-probe.png (—shot-after 3, exit 0): HUD слева, подпись «вид спереди (x, y), метки сетки 1 м» целиком в правом углу — под HUD ничего нет.
- data/preview/trial-shot-e3-scene.png (—mode scene, —shot-after 2, exit 0): «вид сверху (x, z)» в правом верхнем углу правой панели, метка «5 м» — у левой кромки, не пересекаются; левая панель чистая.

## Не подтвердил
- Знак ScreenXSign для Nuitrack (+1, без зеркала как в sensor_view) визуально не проверяем без железа: с --no-sensor и дефолтным Kinect знак −1 = прежняя картинка (снимки это подтверждают отсутствием сдвига призраков).

## План
1. Прочитать SceneCanvas.cs, GhostCanvas.cs, SkeletonCanvas.cs (конвенция ScreenXSign), Topology.cs.
2. Посмотреть снимки data/preview/trial-shot-e2-probe.png и trial-shot-e2-scene.png.
3. Исправить: (a) коллизия подпись «вид сверху (x, z)» vs метка «5 м»; (b) HUD перекрывает подпись «вид спереди (x, y), метки сетки 1 м»; (c) ScreenXSign по трекеру.
4. Сборка + тесты + два снимка E3 + визуальная проверка.
