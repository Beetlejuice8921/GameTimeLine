# Интеграция TimeMapGameplay с проектом

Все точки расширения находятся автоматически (TypeCache / рефлексия): достаточно объявить класс с публичным конструктором без параметров.

| Что | Интерфейс / база | Сборка проекта |
|---|---|---|
| Формат сейва | `ISaveBuilder` | Editor или Runtime |
| Загрузка сейва при «Play Mode с t» | `ITimelineBootstrap` | Runtime |
| Правило проверки | `IValidationRule` | Editor |
| Тип требования | `Requirement` (`[Serializable]`, `[DisplayName]`) | Runtime |
| Превью ассета | `IPreviewSource` | Editor |
| Карточка среза | `ISlicePanel` + `[SlicePanel("Id", order, Column = 1, Replaces = "Hero")]` | Editor |
| Отрисовка трека | `TrackDrawer` / `ItemTrackDrawer` + `[CustomTrackDrawer(typeof(MyTrack))]` | Editor |
| Тип трека | `ClipTrack` / `MarkerTrack` / `CurveTrack` + `[TrackMenu("Имя")]` | Runtime |

## Сейвы

Активный формат выбирается в **Project Settings → TimeMapGameplay** (там же папка и шаблон имени файла).
Без интеграции работает встроенный `JsonSaveBuilder`: игра читает состояние через `TimelineLaunch.TryReadSnapshot`.

```csharp
// Editor или Runtime: ProgressionState → байты сейва вашей игры.
public sealed class MyGameSaveBuilder : ISaveBuilder
{
    public string Id => "mygame.save";
    public string DisplayName => "MyGame save";
    public string FileExtension => "sav";

    public byte[] Build(ProgressionState state, SaveBuildContext context)
    {
        var save = new MyGameSave { level = Mathf.FloorToInt(state.Level ?? 1) };
        var skills = state.FirstOf<SkillTrack, MarkerTrackState>();
        foreach (var skill in skills.Reached)
            save.skillIds.Add(context.AssetKey(skill.Binding)); // GUID привязанного ассета
        return MySaveSystem.Serialize(save);
    }
}

// Runtime: загрузка при старте Play Mode с t (до загрузки первой сцены).
public sealed class MyGameBootstrap : ITimelineBootstrap
{
    public bool CanLoad(PlayRequest request) => request.saveBuilderId == "mygame.save";
    public void Load(PlayRequest request) => MySaveSystem.LoadFromFile(request.savePath);
}
```

Без `ITimelineBootstrap` игра может сама проверить `TimelineLaunch.IsActive` / `TimelineLaunch.Request`.

## Проверка

- Режим (авто / ручной) хранится в EditorPrefs для каждого пользователя. По умолчанию авто, если элементов не больше 500.
- «Проверить» пишет полный отчёт в Console; авто-режим пишет только новые нарушения.

## Анализ из кода

Runtime-сборка содержит чистые функции, которые можно использовать в своих инструментах и CI:

- `ProgressionDiff.Compare(stateA, stateB)` — разница двух срезов;
- `TimelineDiff.Compare(baseline, current)` — изменения между версиями ассета;
- `Checkpoints.Collect(timeline)` — контрольные точки (акты + `CheckpointTrack`);
- `TelemetryImport.ParseCsv/ParseJson`, `TelemetryIndex`, `TelemetryStats` — телеметрия;
- `Csv.Parse/Write`.

В редакторе: `SaveService.CreateCheckpointSaves`, `TimelineCsv.Export/Import`, `TimelineJson`, `TimelineVersions`.
