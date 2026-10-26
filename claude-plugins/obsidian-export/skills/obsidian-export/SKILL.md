---
name: obsidian-export
description: Экспорт диалогов Claude в Obsidian в формате Markdown. Используй, когда пользователь просит сохранить, экспортировать или выгрузить текущий диалог/чат/сессию в Obsidian (или в заметку .md), добавить к заметке краткое описание и теги, настроить хранилище (vault) и папку для автоэкспорта, выгрузить старые сессии проекта или импортировать историю чатов claude.ai. Triggers - "экспортируй в обсидиан", "сохрани диалог в Obsidian", "export this chat to Obsidian", "настрой экспорт в Obsidian", "obsidian vault".
argument-hint: "[setup <путь к хранилищу> | backfill | import <файл> | status]"
allowed-tools: Bash(python3 *), Bash(python *)
---

# Экспорт диалогов в Obsidian

Плагин уже экспортирует каждую сессию Claude Code автоматически: хуки `Stop` и
`SessionEnd` перезаписывают одну заметку на сессию после каждого ответа. Этот
скилл нужен для того, что хук не умеет: настройки, краткого описания и тегов,
выгрузки старых сессий и импорта из claude.ai.

Скрипт (только стандартная библиотека Python 3.8+):

```
python3 "${CLAUDE_SKILL_DIR}/scripts/obsidian_export.py" <команда>
```

На Windows без `python3` используй `python`. Если `${CLAUDE_SKILL_DIR}` не
подставился, найди скрипт: `find ~/.claude/plugins -name obsidian_export.py`.
Все команды печатают JSON — читай его и пересказывай пользователю по-человечески.

Аргументы вызова: $ARGUMENTS

## 1. Сначала проверь настройку

```
python3 "${CLAUDE_SKILL_DIR}/scripts/obsidian_export.py" status
```

- `vault_resolved` пустой → хранилище не найдено. Покажи `detected_vaults`
  (хранилища из настроек приложения Obsidian), спроси, какое использовать, и
  выполни настройку (раздел 3). Не выдумывай путь.
- `vault_source: "obsidian.json"` → хранилище выбрано автоматически (открытое в
  Obsidian). Скажи пользователю, какое именно, и предложи закрепить его через
  `setup --vault`.

## 2. Экспорт текущего диалога (основной сценарий)

Перед вызовом сам составь по всему диалогу:

- **title** — короткое понятное название заметки (3–8 слов, на языке диалога);
- **summary** — 2–4 предложения: задача, что сделано, итог/решение;
- **tags** — 2–5 тематических тегов в нижнем регистре, через запятую, без `#`
  (например `android, kotlin, widget`). Теги `claude` и `claude/project/<проект>`
  добавляются сами.

Затем:

```
python3 "${CLAUDE_SKILL_DIR}/scripts/obsidian_export.py" export \
  --session "${CLAUDE_SESSION_ID}" \
  --title "<title>" --rename \
  --tags "<tag1>, <tag2>" \
  --summary-file - <<'EOF'
<summary>
EOF
```

- Описание передавай через heredoc `--summary-file -`, чтобы кавычки и переносы
  не ломали команду.
- `--rename` переименовывает файл под новое название; убери флаг, если
  пользователь просит не трогать имя файла.
- Если `--session` не сработал (пустой ID или `transcript not found`), повтори
  без него — скрипт возьмёт самую свежую сессию текущего каталога.
- Название, описание и теги сохраняются: следующие автоэкспорты их не затрут.
- В ответе дай путь к заметке (`relative`) и ссылку `uri`
  (`obsidian://open?...`), чтобы открыть её в Obsidian одним кликом.

Если пользователь просит «просто сохранить», без описания — запусти `export`
без `--summary/--tags/--title`.

## 3. Настройка

```
python3 "${CLAUDE_SKILL_DIR}/scripts/obsidian_export.py" setup \
  --vault "<путь к хранилищу>" --folder "Claude" --language ru
```

Остальные параметры — `--set ключ=значение` (значение в JSON, можно несколько
`--set`):

| Ключ | По умолчанию | Смысл |
|---|---|---|
| `folder` | `Claude` | папка внутри хранилища |
| `filename_template` | `{date} {title}` | имя файла; поля `{date} {time} {title} {project} {session}`, `/` создаёт подпапки — например `{project}/{date} {title}` |
| `auto_export` | `true` | автоэкспорт хуками |
| `include_tool_calls` | `true` | вызовы инструментов как свёрнутые callout-блоки |
| `include_tool_results` | `true` | результаты инструментов внутри этих блоков |
| `include_thinking` | `false` | размышления модели |
| `max_tool_chars` | `1500` | обрезка длинных команд и выводов |
| `tags` | `["claude"]` | теги каждой заметки |
| `project_tags` | `true` | тег `claude/project/<папка проекта>` |
| `exclude_projects` | `[]` | glob-шаблоны имён/путей проектов без автоэкспорта |
| `min_user_messages` | `1` | не сохранять сессии короче этого |
| `language` | `ru` | язык подписей: `ru` или `en` |

Пример: `setup --set include_thinking=true --set 'exclude_projects=["secret-*"]'`.

Временно выключить автоэкспорт без изменения настроек: переменная окружения
`OBSIDIAN_EXPORT_DISABLE=1`. Путь к хранилищу можно задать и переменной
`OBSIDIAN_VAULT_PATH`.

## 4. Другие команды

- **Выгрузить старые сессии проекта:** `backfill` (текущий каталог) или
  `backfill --all` (все проекты из `~/.claude/projects`). Перед `--all` уточни у
  пользователя — это может быть сотни заметок.
- **Импорт истории claude.ai:** пользователь скачивает архив в claude.ai →
  Settings → Privacy → Export data, затем
  `import-claude-ai "<путь к .zip или conversations.json>"`. Заметки попадут в
  папку `claude_ai_folder` (по умолчанию `Claude/claude.ai`).
- **Диагностика:** `status` показывает конфиг, найденные хранилища и хвост лога
  `~/.claude/obsidian-export/export.log` — хук пишет ошибки туда, а не в чат.

## Как устроена заметка

- YAML-свойства: `title`, `summary`, `session_id`, `project`, `cwd`,
  `git_branch`, `model`, `created`, `updated`, `user_messages`, `tags`.
- Реплики — заголовки `## 👤 Пользователь · 14:05` / `## 🤖 Claude · 14:06`;
  инструменты — свёрнутые callout'ы `> [!example]-`, ошибки — `> [!failure]-`.
- Блок между маркерами `%% obsidian-export:notes-start %%` и
  `%% obsidian-export:notes-end %%` — для личных заметок пользователя, он
  переживает повторный экспорт. Всё остальное перегенерируется: правки вне
  блока будут перезаписаны — предупреди об этом, если пользователь хочет
  редактировать текст диалога.
- Заметку можно переименовать или перенести внутри хранилища — она найдётся по
  `session_id` и продолжит обновляться.
