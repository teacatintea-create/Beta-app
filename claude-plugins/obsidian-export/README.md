# obsidian-export — диалоги Claude в Obsidian

Плагин для Claude Code. Каждая сессия сама сохраняется в хранилище Obsidian
отдельной Markdown-заметкой и обновляется после каждого ответа Claude.

- **Автоэкспорт:** хуки `Stop` и `SessionEnd`. Одна сессия — одна заметка,
  дублей не бывает.
- **Скилл `obsidian-export`:** ручной экспорт, где Claude сам пишет название,
  краткое описание и теги. Плюс настройка, выгрузка старых сессий и импорт
  истории claude.ai.
- **Формат для Obsidian:** YAML-свойства (properties), теги, свёрнутые
  callout-блоки для вызовов инструментов, ссылка `obsidian://`.
- **Ваши заметки не пропадут:** текст между маркерами `%% obsidian-export:notes-… %%`
  сохраняется при повторном экспорте. Файл можно переименовать или перенести:
  плагин найдёт его по `session_id`.
- Нужен только Python 3.8+, сторонние пакеты не используются.

## Установка

```
/plugin marketplace add teacatintea-create/Beta-app
/plugin install obsidian-export@teacatintea-plugins
```

Для локальной разработки: `claude --plugin-dir ./claude-plugins/obsidian-export`.

## Настройка

Если Obsidian установлен на этом же компьютере, плагин возьмёт открытое
хранилище из настроек приложения (`obsidian.json`). Чтобы указать хранилище
явно, скажите Claude «настрой экспорт в Obsidian в ~/Documents/MyVault» или
выполните:

```
python3 <plugin>/skills/obsidian-export/scripts/obsidian_export.py setup --vault ~/Documents/MyVault --folder Claude
```

Настройки хранятся в `~/.claude/obsidian-export/config.json`. Полный список
параметров (шаблон имени файла, вызовы инструментов, размышления, исключения
проектов, язык подписей) есть в [SKILL.md](skills/obsidian-export/SKILL.md#3-настройка).

Переменные окружения:

| Переменная | Что делает |
|---|---|
| `OBSIDIAN_VAULT_PATH` | путь к хранилищу (важнее конфига) |
| `OBSIDIAN_EXPORT_FOLDER` | папка внутри хранилища |
| `OBSIDIAN_EXPORT_DISABLE=1` | выключить автоэкспорт |
| `OBSIDIAN_EXPORT_CONFIG` | другой путь к config.json |

## Использование

Ничего делать не нужно: после первого ответа Claude заметка появится в
`<хранилище>/Claude/2026-10-26 Название диалога.md`.

Через скилл (`/obsidian-export:obsidian-export` или просто словами):

- «экспортируй этот диалог в обсидиан с кратким описанием»: название, summary
  и теги, файл переименуется под название;
- «выгрузи все старые сессии этого проекта в Obsidian»: команда `backfill`;
- «импортируй мою историю claude.ai из ~/Downloads/data.zip»: команда
  `import-claude-ai` для архива из claude.ai → Settings → Privacy → Export data.

## Пример заметки

```markdown
---
title: "Виджет доски задач для Android"
summary: "Сделали виджет на RemoteViews со списком задач и прогрессом."
session_id: "c93edc33-…"
source: claude-code
project: "Beta-app"
git_branch: "main"
model: "claude-…"
created: 2026-10-26T14:05:12
updated: 2026-10-26T14:41:03
user_messages: 6
tags:
  - claude
  - android
  - claude/project/beta-app
---

# Виджет доски задач для Android

> [!summary] Кратко
> Сделали виджет на RemoteViews со списком задач и прогрессом.

%% obsidian-export:notes-start — заметки между этими маркерами сохраняются при обновлении %%
Мои мысли, [[ссылки]] на другие заметки…
%% obsidian-export:notes-end %%

## 👤 Пользователь · 14:05

Добавь виджет на рабочий стол…

## 🤖 Claude · 14:06

> [!example]- 🔧 Read · `app/src/main/AndroidManifest.xml`

Готово: виджет показывает…
```

## Ограничения

- Автоэкспорт работает там, где плагины Claude Code запускают хуки: в CLI,
  десктопном приложении и расширениях для IDE. Чаты из веб-версии и мобильного
  приложения claude.ai переносятся через `import-claude-ai`.
- В облачных сессиях (claude.ai/code) экспорт пишет в файловую систему
  контейнера, а не на ваш компьютер.
- На Windows хуку нужны Git Bash (его использует Claude Code) и `python3` или
  `python` в `PATH`.
- Ошибки хука не показываются в чате, они пишутся в
  `~/.claude/obsidian-export/export.log`. Проверить: команда `status`.
