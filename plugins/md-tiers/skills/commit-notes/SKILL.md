---
name: commit-notes
description: Per-task commit notes in .claude/commit-notes/, and committing from them. Use when the user asks to commit ("커밋해", "커밋 정리해", "노트대로 커밋", "commit") or to show/edit the notes ("커밋 노트 보여줘"), and at the end of a feature or fix that is left uncommitted (write its note). Each note holds the commit subject, the files the task touched and a draft body with the why; committing reads the diff once to check them.
---

# commit-notes

Notes live in `.claude/commit-notes/` at the project root, **one file per task** (`<task-slug>.md`, e.g. `chat-autocomplete.md`).
They are ✏️ work notes (see the md-tiers rules shown at session start): written once at the end of a task, only the task's own file.
Several chats may share one working tree, so a chat never edits another task's file; only this skill, run when the user asks, touches all of them.
Answer in the user's language.

**Keep it out of git:** when creating the folder, add `.claude/commit-notes/` to `.git/info/exclude` unless `.gitignore` or the exclude file already covers it (local, nothing to commit).
**Old single file:** if `.claude/commit-notes.md` exists, first move each `## ` note in it into its own file in the folder (skip notes already there), then delete it.

## Note format

```markdown
## feat(chat): 대화창에서 메뉴 바로 고르기
- 날짜: 2026-11-03
- 버전: 0.1.6 · dev · 59d00c5
- 파일: src/core.rs, web/app.js
- 내용:
  무엇이 어떻게 바뀌었는지, 왜 그렇게 했는지 (커밋 본문 초안)
```

- `버전:` = the project's version if it has one (Cargo.toml, package.json, …; the project's CLAUDE.md may name the file) · branch · short HEAD, at the time the work was done. Updating a note later keeps the original version. It flags stale notes and is not copied into the commit message.
- Heading = commit subject in the repo's style (look at `git log --oneline`), e.g. `feat|fix|refactor|chore|docs|perf(scope): 요약`. Write it in the language the repo's commits use.
- `파일:` lists every file this work touched, repo-relative, including new and deleted files. Build it from `git status` / `git diff --stat`, not from memory.
- `내용:` is the commit body: what changed for the user, and the non-obvious why. Not a file-by-file diff summary.
- Same feature continued later in the same or another chat → update that file, don't add a second one.
- Write it once, when the work is done and builds/tests pass, not mid-task. Show the note to the user in the report.

## Committing ("커밋해")

"커밋해" is the go-ahead. **Don't ask questions along the way.** Make the calls below yourself, commit, and put anything worth a second look in the final report. Commits are local and easy to amend, so a judgment call is fine.

1. Read every file in `.claude/commit-notes/` (after migrating the old single file, above) and `git status --porcelain`. No notes and no changes → say so and stop.
   Commit on the current branch whatever it is; if it's the main branch, mention it in the report.
2. Cross-check notes against real changes:
   - Note lists a file with no change → it was already committed or reverted; drop it from the note (drop the note if nothing is left).
   - Changed file in no note → attach it to the matching note or make a new note for it from its diff.
3. Read the diff once (`git diff -- <files>`, plus new files) and check it against each note. The note gives the intent and the why; the diff is the truth about what changed.
   - Note says something the diff doesn't do, or the diff does something the note doesn't mention (unrelated edits, a second feature) → fix the message or split it into its own commit.
   - Leftovers that look unintended (debug prints, commented-out code, temp files) → commit as is, don't remove them, and list them (file:line) at the top of the report so the user can fix them.
   - Don't rewrite a note that matches the diff; keep its wording.
4. Group: notes that share a file go into one commit (hunks can't be split non-interactively). Others become one commit each, oldest note first.
5. Per group: `git add -- <its files>` (never `git add -A` / `git add .`), then commit with the heading as subject, `내용` as body, and any attribution trailer the system asks for. Use a heredoc for the message.
   If a hook fails, fix the cause and make a new commit; don't skip hooks.
6. Delete the note files that were committed. Leave the rest.
7. Don't push unless asked.
8. Report a short table: 커밋 해시 / 제목 / 파일 수. Mention leftovers (first), files that had no note, messages you changed from the note, and if `.claude/verify-todo/` still has items, say how many are unverified (don't block on them).

## Showing

"커밋 노트 보여줘" → print the notes and, next to each, whether its files are still uncommitted.
