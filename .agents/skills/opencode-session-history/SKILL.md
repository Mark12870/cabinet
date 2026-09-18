---
name: opencode-session-history
description: "How to read what an OpenCode session did in cabinet — its SQLite DB, read with python3 since sqlite3 is absent"
metadata:
  node_type: memory
  type: reference
  originSessionId: 1895e67a-f1bb-4fce-9b9a-fc122ad5e15b
  modified: 2026-09-10T19:04:45.545Z
---

The user also works on cabinet with OpenCode and sometimes asks to "continue the OpenCode work". Its history is in
`~/.local/share/opencode/opencode.db` (SQLite). The host has no `sqlite3`, so open it read-only with python3:
`sqlite3.connect('file:opencode.db?mode=ro', uri=True)`.

- `session`: `id, title, directory, parent_id, time_created` in ms; subagent runs have `parent_id` set to the main session.
- `message` and `part` have only `id`, ids, times and `data` (JSON); `part` carries `session_id` itself, so no join is
  needed. A part's kind is `json_extract(data, '$.type')` — `text`, `tool`, `reasoning`; there is no `type` column. A
  user prompt is a `text` part whose message's `data` has `role: user`. A tool part's `data.state` holds `input`/`output`.
- Edited files show up as `tool` parts with `tool` = `edit`/`write`/`apply_patch`.
