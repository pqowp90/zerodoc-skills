---
name: knowhow
description: This project's low-priority tips in .claude/knowhow/ (one tip per file, titles listed at session start). Use when writing a tip after solving something that was stuck, when a listed tip matches the task, when a tip reaches the promotion mark (⬆), or when the user asks about tips ("노하우 보여줘", "노하우 정리해", "노하우 승격", "knowhow").
---

# knowhow

💡 Know-how is the middle tier of the md-tiers rules: tips Claude may write and change freely, below 🔒 instructions (CLAUDE.md, skills, settings, hooks, this project's memory). When a tip and an instruction disagree, the instruction wins.
Tips belong to **this project only**: `<project>/.claude/knowhow/<slug>.md`. Another project never reads them. The session-start hook lists their titles; read a file when its title matches the task.
Answer in the user's language.

**Keep it out of git:** when creating the folder, add `.claude/knowhow/` to `.git/info/exclude` unless `.gitignore` or the exclude file already covers it. `uses` changes often, so it would only make noisy diffs.

## File format (one tip)

```markdown
---
title: 증상이나 상황으로 쓴 한 줄 제목 (검색되게)
uses: 1                  # times it really helped, counting the first
last: 2026-11-03         # last day it helped
---

증상: 무엇이 어떻게 안 됐나
해결: 실제로 된 방법 (명령·코드 그대로)
```

## Writing

- Only a way that **actually worked** after being stuck, written at the end of the task. No guesses, no state of the moment (versions, branches, "not done yet"), no moods.
- One tip per file. A similar tip exists → update that file instead of adding one.
- A tip about the machine or tools rather than the project (e.g. a shell quirk) still goes in this project. If it turns up in other projects too, that is a sign to promote it, not to share it.
- Tell the user in one line of the report which tip was added or changed.

## Using

- A tip helped → `uses` +1 and `last` = today.
- A tip was wrong or is outdated → fix it or delete it.

## Promoting (⬆, uses ≥ 3)

Don't ask. End the report with one line, `⬆ 승격 후보: <title> → <where it would go>` (this project's CLAUDE.md, a skill, or memory). When the user says to promote ("승격해", "올려"), write it there (a 🔒 edit: the guard hook backs it up, and the report names the file), then delete the know-how file.

## Showing / tidying ("노하우 보여줘", "노하우 정리해")

- Show: title, uses, last for every file, most used first.
- Tidy: merge duplicates, delete tips that are wrong. Tips unused for a long time are listed in the report, not deleted.
