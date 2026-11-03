---
name: md-cleanup
description: Clean up contamination in the markdown files Claude reads for this project (CLAUDE.md, skills, memory, know-how, work notes) and sort each piece into the right md-tiers tier. Use when the session-start hook says the project has never been cleaned or the last cleanup is old, or when the user asks ("md 정리해", "오염 정리", "지침 정리", "메모리 정리", "md cleanup").
---

# md-cleanup

Instructions drift: a later session reads every line of CLAUDE.md, skills and memory as fact, so stale, guessed or duplicated lines quietly steer work. This skill finds those lines, puts each piece in the right tier, and leaves the project with a clean start.
Everything stays **per project**. The only things that belong outside it are about the user themselves (the language to answer in, and similar), and those are left alone.
Answer in the user's language.

## 1. Read everything (read only)

- 🔒 This project: `CLAUDE.md`, `CLAUDE.local.md` and nested `CLAUDE.md` files; `.claude/skills|agents|commands|rules/`; `.claude/settings*.json`; this project's memory folder `~/.claude/projects/<project path with \ / : replaced by ->/memory/` (`MEMORY.md` and every file).
- 🔒 Outside the project, only to find project-specific content that leaked there: `~/.claude/CLAUDE.md`, `~/.claude/skills|agents|commands/`, `~/.claude/settings.json`.
- 💡 `.claude/knowhow/`, plus the old global `~/.claude/knowhow/` if it exists.
- ✏️ `.claude/commit-notes/`, `.claude/verify-todo/`, old single files `.claude/commit-notes.md` and `.claude/verify-todo.md`.

## 2. Check facts before judging

For every path, command, file name, flag or version a line mentions, check it still exists or holds (`ls`, grep the code, `git log`). A line is stale only if the check says so. Never call something wrong from memory.

## 3. Find contamination

| Kind | Example | Fix |
|---|---|---|
| State of the moment | "아직 릴리스 없음", "오늘 clone이 뒤처짐", a version or branch as a fact | delete, or rewrite as the durable rule behind it |
| Guess or unverified claim | "probably", a cause nobody confirmed | delete, or verify and keep |
| Mood or quotes of the user | angry quotes, "사용자가 화냄" | keep only the rule |
| Stale | a path, command or name that no longer exists | fix to the current one or delete |
| Duplicate | the same rule in memory and CLAUDE.md, or a skill's steps copied into memory | keep one, in the right tier |
| Contradiction | two instructions that disagree | leave both, list under "결정 필요" |
| Wrong tier, down | a practical trick in 🔒 memory/CLAUDE.md (how to run X, a workaround) | move to 💡 `.claude/knowhow/` |
| Wrong tier, up | a 💡 tip with `uses` ≥ 3 | offer promotion to 🔒 |
| Wrong place | project-specific content in `~/.claude/...` | move into this project |
| Log in a to-do | "확인됨 …" paragraphs in verify-todo, notes for files already committed | trim to what is left, delete finished notes |
| Old layout | single-file notes, tips in `~/.claude/knowhow/` | split into per-task / per-project files |

User-level preferences (language, tone) in `~/.claude/` are not contamination: leave them. A copy of one inside project memory is a duplicate.

## 4. Apply, then report (don't ask)

"md 정리해" is the go-ahead. Don't stop to ask; every 🔒 edit is backed up by the guard hook (`.claude/md-tiers/backup/`), so a change the user dislikes is one copy away.
1. Apply the fixes. Moving a tip: new file in `.claude/knowhow/` with the know-how format (`title`, `uses`, `last`), then remove it from the old place. Keep `MEMORY.md` in sync when a memory file changes or goes away. Before removing a file that has no backup (outside the project, untracked), copy it into the backup folder first.
2. Don't apply what needs the user's judgment (a contradiction, deleting something that might still matter): leave it and list it under "결정 필요".
3. Write `.claude/md-tiers.json` as `{ "cleanup": "<today>" }` and add `.claude/md-tiers.json`, `.claude/knowhow/` and `.claude/md-tiers/` to `.git/info/exclude` unless already ignored. This stops the session-start reminder for 30 days.
4. Report one table: 파일 / 문제 / 바꾼 내용 (짧게), grouped by file; what stays as is in one line; then "결정 필요" items, and the backup folder path.
