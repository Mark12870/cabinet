---
name: opencode-session-history
description: "How to read what an OpenCode session did in cabinet — its SQLite DB, read with python3 since sqlite3 is absent"
metadata:
  node_type: memory
  type: reference
  originSessionId: 1895e67a-f1bb-4fce-9b9a-fc122ad5e15b
  modified: 2026-10-17T10:30:00.000Z
---

The user also works on cabinet with OpenCode and sometimes asks to "continue the OpenCode work". Its history is in
`~/.local/share/opencode/opencode.db` (SQLite). The host has no `sqlite3`, so open it read-only with python3:
`sqlite3.connect('file:opencode.db?mode=ro', uri=True)`.

- `session_v2`: `id, title, directory, parent_id, time_created, time_updated` in ms; subagent runs have `parent_id` set
  to the main session. Order by `time_updated` to find the latest.
- `session_message`: `session_id, type, seq, data` (JSON); order by `seq`. `type` is `user` (prompt in `data.text`),
  `assistant` (`data.content[]` items of type `text` or `tool`; a tool's `state` holds `input`), `compaction`,
  `system`, `idle` and others. A run that stopped early shows `data.error` on its last `assistant`.
- Edited files show up as `tool` items named `apply_patch`, with the patch in `state.input.patchText`.
- `todo`: `session_id, content, status, position`.
