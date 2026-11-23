# E32: тёмные ComboBox и непрозрачность финальной карточки

Задача: добить тёмную тему тулбара в tools/player_trial — три ComboBox («Режим», трекер, «Отсчёт») в полном шаблоне через XamlReader.Parse; финальная карточка FinalCardBrush alpha 0xB8 → 0xF0.
Дата: 2026-11-23
Исполнитель: implementer (flash)
Файлы: только tools/player_trial/MainWindow.cs + этот отчёт.

Пункты брифа:
1. Тёмный ComboBox (обязательный): полный шаблон Aero2 (templateRoot/toggleButton/contentPresenter/PART_Popup/dropDownBorder), цвета бокса #FF2A2C34/#FFE6E6EA/#FF3A3D47, дропдаун #FF1B1C22, ComboBoxItem с триггерами IsMouseOver/#FF2F323C, IsSelected/#FF42A5F5, disabled Opacity 0.5. Регистрация в конструкторе рядом со стилем кнопки.
2. FinalCardBrush: alpha 0xB8 → 0xF0, больше ничего в карточке не менять.

Прогон: dotnet build DanceLab.sln; dotnet test tests/DanceLab.View.Tests; два скриншот-прогона exe (--shot trial-shot-e32-toolbar.png --shot-after 4, --shot trial-shot-e32-end.png --shot-after 9), все с кодом 0.

## Прогресс

- [2026-11-23] Прочитаны поля (строки 46/52/85), конструктор (регистрация стиля кнопки на 321), CreateDarkButtonStyle (530-578), FinalCardBrush (131).
- [2026-11-23] Пункт 2 сделан: FinalCardBrush alpha 0xb8 → 0xF0 (строка 131).
- [2026-11-23] Пункт 1 сделан: в конструкторе после стиля кнопки добавлены Resources.Add(typeof(ComboBox)) и Resources.Add(typeof(ComboBoxItem)); новые методы CreateDarkComboBoxStyle и CreateDarkComboBoxItemStyle (XamlReader.Parse, XAML-строки с одинарными кавычками). Исправлена опечатка в цвете Foreground (#FFE66EA → #FFE6E6EA) до сборки.
- [2026-11-23] Запущен dotnet build DanceLab.sln.
- [2026-11-23] Сборка: успешно, 0 предупреждений, 0 ошибок, exit 0.
- [2026-11-23] dotnet test tests/DanceLab.View.Tests: пройдено 21, не пройдено 0, пропущено 0, exit 0.
- [2026-11-23] Скриншот-прогон --shot trial-shot-e32-toolbar.png --shot-after 4: exit 0, PNG создан.
- [2026-11-23] Скриншот-прогон --shot trial-shot-e32-end.png --shot-after 9: exit 0, PNG создан.

## Отчёт

### Сделал
- Пункт 2: `tools/player_trial/MainWindow.cs:131` — FinalCardBrush alpha 0xb8 → 0xF0, остальное в карточке не тронуто.
- Пункт 1: `tools/player_trial/MainWindow.cs:326-327` — в конструкторе рядом со стилем кнопки зарегистрированы умолчания окна Resources.Add(typeof(ComboBox)) и Resources.Add(typeof(ComboBoxItem)); явный Style на боксы не понадобился (сборка и запуски прошли, тёмные боксы собираются через window-level стиль). Новые методы `CreateDarkComboBoxStyle` (строки 596-681) и `CreateDarkComboBoxItemStyle` (685-724): полный шаблон Aero2-образца из XAML-строки через XamlReader.Parse (одинарные кавычки атрибутов, чтобы не экранировать в C#). Обязательные части с точными x:Name: templateRoot (Grid, колонки * и 32), toggleButton (обе колонки, шаблон: Border CornerRadius 3 + стрелка Path 8x4 цветом Foreground справа; IsMouseOver и IsChecked → BorderBrush #FF5FB3F8, фон не меняется), contentPresenter (SelectionBoxItem/SelectionBoxItemTemplate, Margin={TemplateBinding Padding}), PART_Popup (IsOpen two-way к IsDropDownOpen, Placement Bottom, AllowsTransparency), dropDownBorder (фон #FF1B1C22, рамка #FF3A3D47, радиус 4, Margin сверху 2, MaxHeight={TemplateBinding MaxDropDownHeight}, MinWidth={Binding ActualWidth, ElementName=templateRoot}, внутри ScrollViewer с ItemsPresenter). Отключённый бокс — Opacity 0.5. ComboBoxItem: фон #FF1B1C22, текст #FFE6E6EA, триггеры IsMouseOver → #FF2F323C, IsSelected → #FF42A5F5 + текст #FF101114, IsEnabled false → Opacity 0.5; отступ контента 10,6 (ContentPresenter без своего Padding — Margin на нём).

### Прогнал
- dotnet build DanceLab.sln — успешно, 0 предупреждений, 0 ошибок, exit 0.
- dotnet test tests/DanceLab.View.Tests — пройдено 21 / не пройдено 0 / пропущено 0 (всего 21), exit 0.
- tools/player_trial/bin/x64/Debug/net48/player_trial.exe --no-sensor --shot data/preview/trial-shot-e32-toolbar.png --shot-after 4 — exit 0, PNG создан.
- ... --shot data/preview/trial-shot-e32-end.png --shot-after 9 — exit 0, PNG создан. Оба запуска без --no-build: XAML-строки приняты XamlReader.Parse, окно построено.

### Не подтвердил / проблемы
- Внешний вид выпадающих списков и карточек не оценивал — по брифу скриншоты и открытие списков мышью остаются ПМ (данные: data/preview/trial-shot-e32-toolbar.png, trial-shot-e32-end.png).
- Скриншоты сняты без открытия списка: тёмный popup и подсветку выбранных пунктов визуально подтвердит только ПМ. Структурно Popup/dropDownBorder/ComboBoxItem входят в шаблон и окно собралось (exit 0).
