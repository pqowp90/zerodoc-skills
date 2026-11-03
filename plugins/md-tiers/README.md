# md-tiers

Keeps the markdown files Claude reads and writes from drifting. Claude tends to edit CLAUDE.md, skills and memory in the middle of unrelated work, write notes from guesses, and pile up logs that later sessions read as fact. Once md-tiers is on in a project, Claude manages that project's md files in three tiers, **without stopping to ask you**: it does the work, keeps backups, and says in the report what it changed.

| Tier | Files (all inside the project) | Who edits |
|---|---|---|
| 🔒 Instructions | `CLAUDE.md`, `.claude/skills·agents·commands·rules/`, settings, hooks, this project's memory | Only when you ask. Every edit is backed up first, and the report must name each changed file and why. |
| 💡 Know-how | `.claude/knowhow/<slug>.md` | Claude, freely. Low priority: instructions win. A tip used 3 times shows up as "⬆ 승격 후보" at the end of a report; say "승격해" to move it into instructions. |
| ✏️ Work notes | `.claude/commit-notes/<task>.md`, `.claude/verify-todo/<task>.md` | Claude, once at the end of a task, only that task's file (safe with several chats on one tree). |

Everything belongs to the project; nothing is shared between projects. Only things about you yourself (the language Claude answers in, and the like) stay in your account settings.

Say **"md 정리해"** to run md-cleanup: it reads the project's CLAUDE.md, skills, memory and notes, checks each claim against the code, fixes stale state, guesses, quoted moods, duplicates, tricks that belong in know-how and project content that leaked into `~/.claude`, and reports a table. Things that need your call (contradictions) are listed, not changed. A project that was never cleaned, or not for 30 days, gets a one-line reminder at the end of a report.

플러그인을 켠 프로젝트부터 Claude가 md 파일을 세 등급으로 관리합니다. 중간에 묻지 않고, 하고 나서 보고합니다. 지침(🔒)은 고치기 전에 백업이 남고 보고에 바뀐 파일과 이유가 꼭 들어가며, 노하우(💡)는 자유롭게 쌓이다가 3번 쓰이면 보고 끝에 승격 후보로 뜨고, 작업 노트(✏️)는 작업마다 파일 하나로 남습니다. 모두 그 프로젝트에 붙고, "md 정리해"로 오염 정리를 합니다.

## What's inside

- **Hooks** (`hooks/guard.js`, `hooks/session.js`), none of which ever asks you anything:
  - SessionStart: prints the tier rules, this project's know-how titles, and the cleanup reminder when due.
  - PreToolUse: copies a 🔒 file to `.claude/md-tiers/backup/` before Edit/Write changes it (newest 100 kept).
  - PostToolUse: remembers which 🔒 files changed in this message (shell writes are matched by pattern).
  - Stop: if a 🔒 file changed and the report doesn't name it, sends Claude back once to add what changed, why, and where the backup is.
- **Skills**
  - `md-cleanup`: the cleanup described above.
  - `commit-notes`: per-task commit notes, and "커밋해" commits from them after checking the diff.
  - `verify-todo`: things to check by hand. "todo 테스트해" verifies them and deletes the ones that pass.
  - `knowhow`: format and rules for tips, showing/tidying them, promotion.

Know-how, work notes, backups and `.claude/md-tiers.json` (last cleanup date) are added to `.git/info/exclude` when first created, so nothing shows up in `git status`.

Requirements: Node.js on PATH (the hooks are small Node scripts). Works on any OS; written and tested on Windows.

Install per project (recommended; `local` = this project, just you):

```
claude plugin install md-tiers@zerodoc-skills --scope local
```
