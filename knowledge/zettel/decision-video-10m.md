---
name: decision-video-10m
description: "Решение владельца 2026-11-09: видео AIST берём в битрейте 10M, а не 2M, как написано в старом spec/85-datasets.md"
metadata:
  node_type: memory
  type: project
  originSessionId: 21a2b4dc-a66d-4bad-a785-b00e04b48d0e
  modified: 2026-11-09T16:21:43.316Z
---

Основной битрейт видео AIST: 10M (путь `https://aistdancedb.ongaaccel.jp/v1.0.0/video/10M/<имя>.mp4`). Старая спека `spec/85-datasets.md` советует 2M, это решение отменено.

**Why:** владелец: «Качество картинки важнее, плюс у меня будет большая диагональ телевизора/проектора с высоким разрешением».

**How to apply:** при переносе `spec/85-datasets.md` в `specs/datasets.md` записать 10M основным, пересчитать таблицу объёмов (все видео около 400 ГБ против 82 ГБ при 2M, клип 13 с около 16 МБ). Проверено 2026-11-09: путь 10M и 2M отвечают, `video_list.txt` у Google отдаёт 403, массовый загрузчик не запускать. См. [[general-working-rules]].
