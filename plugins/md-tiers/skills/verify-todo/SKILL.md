---
name: verify-todo
description: Manage the "to verify" list in .claude/verify-todo/ (one file per task). Use when the user asks to test, check or verify the todo items ("todo 테스트해", "todo에 있는 거 확인해", "확인 목록 돌려", "verify todo"), or to add/show/clear items ("todo에 넣어", "todo 보여줘"), and at the end of a task whose report asks the user to check something by hand ("확인해보세요"). Verifies each item and deletes the ones that pass.
---

# verify-todo

The list lives in `.claude/verify-todo/` at the project root, **one file per task** (`<task-slug>.md`, same slug as its commit note).
It is a ✏️ work note (see the md-tiers rules shown at session start): written once at the end of a task, only the task's own file.
Several chats may share one working tree, so a chat never edits another task's file; only this skill, run when the user asks, touches all of them.
Answer in the user's language.

**Keep it out of git:** when creating the folder, add `.claude/verify-todo/` to `.git/info/exclude` unless `.gitignore` or the exclude file already covers it (local, nothing to commit).
**Old single file:** if `.claude/verify-todo.md` exists, first move its items into per-task files in the folder (skip items already there), then delete it.

## File format

```markdown
# 작업 이름

- [ ] 2026-11-03 · 짧은 제목
  - 확인: 무엇을 하면(재현 단계) 무엇이 보여야 하는지(기대 결과)
  - 관련: src/app.js, 커밋 abc1234
```

- One item = one checkable behavior. Split "A와 B 확인" into two items.
- `확인:` must be concrete enough that someone with no context can do it.
- A failed check adds `  - 실패 (날짜): 무엇이 어떻게 달랐는지` under the item.
- Items Claude cannot check itself are marked `- [사람]` instead of `- [ ]`.
- The file is a to-do list, not a log. When part of an item is verified, rewrite `확인:` to only what is left (a short "(데스크톱은 확인됨)" is enough); don't append "확인됨 …" paragraphs.
- An empty file is deleted.

## Adding ("todo에 넣어", or at the end of a task)

Add items once, at the end of the task, in the task's own file. Before adding, skip anything already listed in any file (same behavior). Show the added items to the user in the report.

## Running ("todo 테스트해")

1. Read every file in `.claude/verify-todo/` (after migrating the old single file, above). If there are none, say so and stop.
2. For each `- [ ]` item, pick the cheapest check that really proves the behavior. How to build, test and run this project is in its CLAUDE.md:
   - Logic → an existing or new automated test.
   - A GUI → run a separate test copy and look at it (the `ui-check` skill, if installed). Show the user the screenshot path.
   - A web page → open it with a browser skill.
   Build once up front, not per item. Stop any app instance you started when done.
3. Judge each item:
   - **통과** → delete the item from the file.
   - **실패** → keep it, add the `실패` line. Do not fix it unless the user asked for fixes too.
   - **직접 확인 불가** (another device, a real login, sound, etc.) → change to `- [사람]`, keep it, and tell the user exactly what to do.
4. Never delete an item you did not actually verify. "Probably fine" or "the code looks right" is not a pass.
5. Report as a short table: 항목 / 결과 (통과·실패·사람 확인 필요) / 근거 (test name, screenshot path, what was seen). End with what is left on the list.

## Showing / clearing

- "todo 보여줘" → print every file's items, grouped by file.
- `[사람]` items the user says they confirmed ("그거 됐어") → delete them.
- Delete the whole list only when the user asks explicitly.
