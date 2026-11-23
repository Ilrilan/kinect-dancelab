# E29 — MainWindow (tools/player_trial): тёмные стили, кольцо камер, статус-строка, карта конца без оценки

Задача: T26 из specs/player/trial.md (бриф ПМ от визуального аудита).
Дата: 2026-11-23.
Исполнитель: implementer (E29).

Пункты:
1. Тёмный стиль Button (S23) — window-level default, template с hover-оверлеем.
1b. Тёмные ComboBox (опционально).
2. Кольцо камер через CameraRing.Layout (S24).
3. Countdown box 40px, по центру (S24).
4. Основные кнопки FontSize 20 (S24).
5. Status line: Margin, MaxWidth 640, SetStatus + tooltip (S25).
6. Карта конца без оценки: «Трек закончен / без оценки» (S28).
7. HUD без сенсора: без строки «пол» (S30).
8. sceneStats: FontSize 15, LineHeight 24, фон #FF1B1C22 (S29).
9. Статус в режиме сцены сенсора (S29).

Прогон: dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests,
tests/DanceLab.Core.Tests, tests/DanceLab.Capture.Tests; два скриншота
player_trial (--shot-after 4 / 9).

## Прогресс

- [x] Создан файл статуса, прочитаны спека (S23-S30, T26) и CameraRing.cs.
- [x] Правки MainWindow.cs внесены: стиль Button (S23), кольцо из CameraRing.Layout (S24),
      поле отсчёта, FontSize 20 основных кнопок, SetStatus + MaxWidth 640 (S25),
      финал без оценки (S28), HUD без «пол» (S30), sceneStats (S29), статус сцены (S29).
      22 места status.Text -> SetStatus (проверено grep: осталось только тело SetStatus).
- [x] Сборка и тесты.
- [x] Скриншоты: data/preview/trial-shot-e29-toolbar.png (--shot-after 4),
      data/preview/trial-shot-e29-end.png (--shot-after 9), оба exit=0.

## Отчёт

### Сделал (tools/player_trial/MainWindow.cs — единственный правленый файл)

1. Тёмный стиль Button (S23): метод CreateDarkButtonStyle (стр. 530-576) — сеттеры
   Background=RingIdle, Foreground=Text, BorderBrush #FF3A3D47, BorderThickness 1,
   FocusVisualStyle null; шаблон кодом (FrameworkElementFactory): Border bg с
   TemplateBinding и CornerRadius 3, Border hover (White, Opacity 0, IsHitTestVisible
   false), ContentPresenter по центру. Триггеры IsMouseOver/IsPressed/IsEnabled=false
   в Template.Triggers (TargetName hover/bg, Opacity 0.12/0.24/0.4) — первая попытка
   в Style.Triggers падала в рантайме (TargetName допустим только в Template.Triggers),
   исправлено. Регистрация window-level: Resources.Add(typeof(Button), ...) (стр. 321).
2. Кольцо камер (S24): angleRing = CameraRing.CanvasW/CanvasH, Margin(10,0,14,0)
   (стр. 98); кнопки Width/Height = CameraRing.ButtonW/ButtonH, FontSize 11, Padding 0,
   позиция из CameraRing.Layout(i) (стр. 1406-1421); танцор: точка 12x12 в
   CenterX-6/CenterY-14, подпись «танцор» шириной CanvasW на CenterY+2 (стр. 346-353);
   цвета через StyleRingButton (стр. 1427-1432) в PopulateAngles и SwitchAngle
   (стр. 1477-1480): выбранная RingActive + #FF101114, остальные RingIdle/Text.
   CameraYawStepDeg остался — используется в YawRad (азимут не тронут).
3. Поле отсчёта (S24): countdownBox Height 40, VerticalAlignment/VerticalContentAlignment
   Center, FontSize 18 (стр. 85).
4. Основные кнопки (S24): playButton/restartButton/attemptButton FontSize 20 (стр. 80-82).
5. Строка состояния (S25): status Margin(12,0,12,0), MaxWidth 640 (стр. 166); метод
   SetStatus (стр. 584-588) ставит Text + ToolTip; заменены все 22 присваивания
   status.Text (grep: осталось только тело SetStatus).
6. Финал без оценки (S28): finalTitle — поле (стр. 138), в конструкторе вместо inline
   TextBlock (стр. 448); ShowFinalOverlay (стр. 1340-1373) без раннего return: без
   оценки — «Трек закончен», «без оценки» (FontSize 40), текст «Сенсор не видел
   танцора…» + «Ещё раз»/Space; с оценкой — как раньше (FontSize 72); в обоих случаях
   карточка Visible. Комментарии у полей и MediaEnded обновлены.
7. HUD без сенсора (S30): ветка NoSensor/session==null — «без сенсора» + время, без
   строки «пол» (стр. 990).
8. Статистика сцены (S29): sceneStats FontSize 15, FontWeight Normal, LineHeight 24,
   BlockLineHeight, Padding(14,10,14,12), Margin(10,0,10,10), Background #FF1B1C22
   (стр. 57-67); присваивание sceneStats.Text (стр. 1211) жирного не ставит.
9. Статус в режиме сцены (S29): StartAsync ставит «Сцена сенсора: трекер …» при
   AppMode.Scene, иначе старый текст (стр. 1790-1793).

1b (ComboBox): не подтвердил — см. ниже.

### Прогнал

- dotnet build DanceLab.sln — успешно, 0 предупреждений, 0 ошибок.
- dotnet test tests/DanceLab.View.Tests — пройдено 21 / всего 21, 0 не пройдено.
- dotnet test tests/DanceLab.Core.Tests — пройдено 38 / всего 38, 0 не пройдено.
- dotnet test tests/DanceLab.Capture.Tests — пройдено 45 / всего 45, 0 не пройдено.
- player_trial.exe --no-sensor --shot data/preview/trial-shot-e29-toolbar.png
  --shot-after 4 — exit=0.
- player_trial.exe --no-sensor --shot data/preview/trial-shot-e29-end.png
  --shot-after 9 — exit=0.

### Не подтвердил / проблемы

- Пункт 1b (тёмные ComboBox) отменён по праву брифа: надёжно стилизовать дропдаун
  без полной замены шаблона нельзя — в шаблоне Aero2 рамка дропдауна красится
  SystemColors.WindowBrushKey (белым), а ховер ComboBoxItem — SystemColors.HighlightBrushKey
  триггером шаблона, который перебивает ItemContainerStyle; точечный полустайлинг
  даёт белые/голубые вспышки, то есть повторяет дефект S23 на списках. Полная замена
  шаблона ComboBox кодом не влезает в бюджет задачи.
- Первая попытка стиля Button падала в рантайме (TargetName-сеттеры в Style.Triggers);
  исправлено переносом триггеров в Template.Triggers, после пересборки оба прогона
  скриншотов прошли. Больше падений не было.


1b (ComboBox): не делаю — надёжная тёмная тема дропдауна требует полной замены шаблона
ComboBox (в Aero2 DropDownBorder красится SystemColors.WindowBrushKey, а ховер
ComboBoxItem — SystemColors.HighlightBrushKey триггером шаблона, который перебивает
ItemContainerStyle); точечный полустайлинг даёт белые/голубые вспышки. По брифу пункт
отменяю в пользу 2-9.
