# md-tiers

Keeps the markdown files Claude reads and writes from drifting. Claude tends to edit CLAUDE.md, skills and memory in the middle of unrelated work, write notes from guesses, and pile up logs that later sessions read as fact. Once md-tiers is on in a project, Claude manages that project's md files in three tiers:

| Tier | Files (all inside the project) | Who edits |
|---|---|---|
| 🔒 Instructions | `CLAUDE.md`, `.claude/skills·agents·commands·rules/`, settings, hooks, this project's memory | Only when you ask. A hook shows an approval prompt before any edit, even in auto mode. |
| 💡 Know-how | `.claude/knowhow/<slug>.md` | Claude, freely. Low priority: instructions win. A tip used 3 times is offered for promotion to instructions. |
| ✏️ Work notes | `.claude/commit-notes/<task>.md`, `.claude/verify-todo/<task>.md` | Claude, once at the end of a task, only that task's file (safe with several chats on one tree). |

Everything belongs to the project; nothing is shared between projects. Only things about you yourself (the language Claude answers in, and the like) stay in your account settings.

The first session after installing asks to run **md-cleanup**: it reads the project's CLAUDE.md, skills, memory and notes, checks each claim against the code, and proposes a table of fixes (stale state, guesses, quoted moods, duplicates, tricks that belong in know-how, project content that leaked into `~/.claude`). Nothing changes before you say go. It asks again when the last cleanup is over 30 days old.

플러그인을 켠 프로젝트부터 Claude가 md 파일을 세 등급으로 관리합니다. 지침(🔒)은 고칠 때 승인 창이 뜨고, 노하우(💡)는 자유롭게 쌓이다가 3번 쓰이면 지침으로 올릴지 묻고, 작업 노트(✏️)는 작업마다 파일 하나로 남습니다. 모두 그 프로젝트에 붙고, 처음 켜면 오염 정리(md-cleanup)를 먼저 제안합니다.

## What's inside

- **Hooks**
  - SessionStart: prints the tier rules, this project's know-how titles, and a cleanup reminder when due.
  - PreToolUse: asks before Edit/Write or a shell write to a 🔒 file, or before `claude plugin disable/uninstall`. Reads are never blocked. Shell commands are matched by pattern, so a write hidden inside a script file is not caught.
- **Skills**
  - `md-cleanup`: the contamination check described above.
  - `commit-notes`: per-task commit notes, and "커밋해" commits from them after checking the diff.
  - `verify-todo`: things to check by hand. "todo 테스트해" verifies them and deletes the ones that pass.
  - `knowhow`: format and rules for tips, showing/tidying them, promotion.

Know-how, work notes and `.claude/md-tiers.json` (last cleanup date) are added to `.git/info/exclude` when first created, so nothing shows up in `git status`.

Requirements: Node.js on PATH (the hooks are small Node scripts). Works on any OS; written and tested on Windows.

Install per project (recommended; `local` = this project, just you):

```
claude plugin install md-tiers@zerodoc-skills --scope local
```
