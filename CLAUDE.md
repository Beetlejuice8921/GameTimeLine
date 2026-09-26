# TimeMapGameply — Progression Timeline

Unity Editor-инструмент для геймдизайнера: прогрессия игры на таймлайне + «срез игры» в выбранный момент. Итог — UPM-пакет.

- Полное ТЗ: `docs/SPEC.md` — читать перед работой.
- Концепт интерфейса (интерактивный HTML): `docs/concept/progression-timeline.html`.

## Правила работы

- Общение и комментарии к задачам — на русском; код, идентификаторы и XML-doc — на английском.
- Editor UI — только UI Toolkit (UXML/USS), без IMGUI, кроме случаев, где Unity API иначе не позволяет.
- Runtime-сборка не должна ссылаться на `UnityEditor`.
- Все изменения данных в редакторе — через `Undo.RecordObject`, затем `EditorUtility.SetDirty`.
- Игровые системы проекта (квесты, сейвы, предметы) подключаются только через интерфейсы-адаптеры (`ISaveBuilder`, `ISlicePanel`, `IValidationRule`, `IPreviewSource`), ядро пакета о них не знает.
- Проверка зависимостей поддерживает авто- и ручной режим (кнопка «Проверить»).
- Новые фичи сопровождать EditMode-тестами для `Evaluate(t)` и правил проверки.

## Проект

- Unity 6 only (минимум 6000.3); песочница — этот репозиторий (Unity 6000.3.8f1, `D:\Unitys\6000.3.8f1`).
- Пакет: `Packages/com.timemap.gameplay` (embedded), namespace `TimeMapGameplay` / `TimeMapGameplay.Editor`.
- Система сейвов проекта неизвестна — `ISaveBuilder` проектируется как набор сменных адаптеров (SPEC, раздел 9).
- Тесты из консоли: `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode|PlayMode -testResults <file>` — Tests/Editor (EditMode) и Tests/Runtime (PlayMode).
- Версии для проверки совместимости: `D:\Unitys\6000.3.8f1`, `6000.5.7f1`, `6000.5.0b11`.
- Если проект открыт в Unity, batchmode-тесты запускать на копии (`Assets` пустая + `Packages` + `ProjectSettings`) во временной папке.
- Не использовать InstanceID-API (`GetInstanceID`, `InstanceIDToObject`) — в 6000.5 это ошибки компиляции; только EntityId.
- Пустые поля `UnityEngine.Object`, загруженные с диска, — «псевдо-null»: проверять через `==` Unity, а не `ReferenceEquals` / `??`.
- Тестовые классы-расширения не должны попадать в автообнаружение реального окна: давать им конструктор с параметром или не вешать атрибуты.
- Enum единиц оси называется `AxisUnit` (не `TimeUnit` — конфликт с `UnityEngine.UIElements.TimeUnit`).

## Текущий этап

Этап 4 — расширения (см. `docs/SPEC.md`, раздел 8), пакет 0.2.0. Этапы 0–3 приняты пользователем 2026-09-26.
Сделано: A/B-срезы, сейвы по контрольным точкам, телеметрия «план/факт», CSV/JSON, версии ассета с «призраками».
Документация: README и CHANGELOG пакета, `Documentation~/index.md` (дизайнер), `Documentation~/Integration.md` (программисты).
Сэмплы в `Samples~` сгенерированы в batchmode (ассеты не править руками).
Состояние анализа (B, телеметрия, версия для сравнения) — во окне (`AnalysisState`), не в ассете.
