# GameTimeLine — TimeMapGameplay

Инструмент геймдизайнера для Unity: вся прогрессия игры на одном таймлайне в стиле видеоредактора
и «срез игры» — что знает, умеет и носит игрок в любой момент времени.

- **Треки:** фабула, сюжет (акты), прокачка, локации, апгрейды, кривая уровня — на оси времени игры
  (часы, сессии или квесты). Свои типы треков добавляются без правки пакета.
- **Срез игры** над таймлайном: акт, герой, карта мира, фабула, снаряжение; пересчёт при движении плейхеда.
- **Проверка зависимостей:** акт требует открытую локацию, навык — уровень, квест — раскрытый факт фабулы.
  Авто-режим или по кнопке.
- **Сейвы и Play Mode с момента t** через сменные адаптеры формата сейва — подходит к любой системе сохранений.
- **Анализ:** сравнение двух срезов (A/B), сейвы по контрольным точкам для QA, телеметрия плейтестов
  «план против факта», обмен с Google Sheets / Excel (CSV), версии ассета со сравнением.

**Требования:** Unity 6000.3 или новее.

## Установка

*Window → Package Manager → + → Add package from git URL*:

```
https://github.com/Beetlejuice8921/GameTimeLine.git?path=/Packages/com.timemap.gameplay
```

Затем *Window → TimeMapGameplay → Progression Timeline* и *Tools → TimeMapGameplay → Create Demo Timeline*.
В Package Manager у пакета есть два сэмпла: **RPG Campaign** и **Metroidvania**.

## Документация

- [Пакет: обзор и быстрый старт](Packages/com.timemap.gameplay/README.md)
- [Руководство дизайнера](Packages/com.timemap.gameplay/Documentation~/index.md) — окно, мышь, клавиши, проверка, анализ
- [Интеграция с проектом](Packages/com.timemap.gameplay/Documentation~/Integration.md) — сейвы, Play Mode с t, свои треки, правила, карточки
- [Изменения](Packages/com.timemap.gameplay/CHANGELOG.md)
- [Техническое задание](docs/SPEC.md) и [интерактивный концепт интерфейса](https://beetlejuice8921.github.io/GameTimeLine/docs/concept/progression-timeline.html) (открывается в браузере)

## Структура репозитория

```
Packages/com.timemap.gameplay/   UPM-пакет: Runtime, Editor, Tests, Samples~, Documentation~
ProjectSettings/, Packages/      проект-песочница для разработки (открывается в Unity 6000.3+)
docs/                            ТЗ и концепт интерфейса
```

Тесты: *Window → General → Test Runner* (EditMode и PlayMode) в проекте-песочнице.

## Лицензия

[MIT](LICENSE.md) — бесплатно для личного и коммерческого использования.

Автор идеи — Alexandr Rumyancev.
