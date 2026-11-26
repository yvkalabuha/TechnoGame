---
tags: [review, ui, core, done]
---
# ADR-0025 — ревьюер (2026-11-21): changes requested

- high, §2 п. 6–7: клиент не читает `session.View`; поток симуляции (`SimHost.Publish`, `SimHost.cs` ~196–216) кладёт вид в `HostSnapshot`, тот идёт в поток Godot через `volatile _snap`; `ActorPanel`, `TechTree`, `AudioDirector`, `SignalField` читают `s.View`. Аренду берёт поток симуляции при сборке `HostSnapshot`, освобождает клиент при замене снимка; убрать `SimHost` из п. 7.
- high, §2 п. 8: проверка поколения до чтения не ловит перезапись во время чтения (рваное чтение без исключения). Аренда обязательна для не-владельца или seqlock.
- high, Проверка п. 2–4: нет теста через `SimHost` (пауза, N+ публикаций, чтение старого `HostSnapshot`).
- med, §2 п. 5: путь F7a (`SliceSession.cs` ~244–247, `prev with {…}` с теми же `NodeList`) и R2 `NodeBlockCache.Begin` — набор в нескольких видах; считать действительность в захватах набора.
- med, `NodeList.Equals` (ранний true по ссылке блока) даст ложный PASS при кольце.
- low: 651 × 200 × 40 B = 4,97 МиБ, не 6,42 (остальное — маски, `KnownSince`, объекты); 6,94 против 6,6 МиБ; `SignalField` в списке потребителей; `SliceObserver._initial` в разборе N = 3; альтернатива «один потребитель с подтверждением последнего снимка».
- Подтверждено по коду: `ProcessView`, `NodeView` без ссылочных полей; `NodeList` — readonly struct; R2; `Volatile`-публикация; `TestApiVersion` 14. Детерминизм не затронут.
