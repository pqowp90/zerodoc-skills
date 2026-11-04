# md-tiers

Keeps the markdown files Claude reads and writes from drifting. Claude tends to edit CLAUDE.md, skills and memory in the middle of unrelated work, write notes from guesses, and pile up logs that later sessions read as fact. Once md-tiers is on in a project, Claude manages that project's md files in three tiers, **without stopping to ask you**. There is no lock: most drift came from having no free place to write, so a labeled free tier (know-how) is what keeps tips and passing state out of the instructions.

| Tier | Files (all inside the project) | Who edits |
|---|---|---|
| 🔒 Instructions | `CLAUDE.md`, `.claude/skills·agents·commands·rules/`, settings, hooks, this project's memory | When you ask, when the work changes the code an instruction describes, or to write down how to use the project's own scripts (call, arguments, output), so later sessions use that and never re-read a script body. Each change is named in the report with its reason. |
| 💡 Know-how | `.claude/knowhow/<slug>.md` | Claude, freely. Low priority: instructions win. A tip used 3 times shows up as "⬆ 승격 후보" at the end of a report; say "승격해" to move it into instructions. |
| ✏️ Work notes | `.claude/commit-notes/<task>.md`, `.claude/verify-todo/<task>.md` | Claude, once at the end of a task, only that task's file (safe with several chats on one tree). |

Everything belongs to the project; nothing is shared between projects. Only things about you yourself (the language Claude answers in, and the like) stay in your account settings.

Say **"md 정리해"** to run md-cleanup: it reads the project's CLAUDE.md, skills, memory and notes, checks each claim against the code, fixes stale state, guesses, quoted moods, duplicates, tricks that belong in know-how and project content that leaked into `~/.claude`, and reports a table. Things that need your call (contradictions) are listed, not changed. A project that was never cleaned, or not for 30 days, gets a one-line reminder at the end of a report.

플러그인을 켠 프로젝트부터 Claude가 md 파일을 세 등급으로 관리합니다. 중간에 묻지 않고, 하고 나서 보고합니다. 지침(🔒)은 요청받았거나 코드가 바뀌어 맞출 때만 고치고 보고에 적으며(프로젝트 스크립트의 사용법은 지침에 적어 두고, 다음부터는 스크립트 본문 대신 그걸 읽습니다), 노하우(💡)는 자유롭게 쌓이다가 3번 쓰이면 보고 끝에 승격 후보로 뜨고, 작업 노트(✏️)는 작업마다 파일 하나로 남습니다. 모두 그 프로젝트에 붙고, "md 정리해"로 오염 정리를 합니다.

## What's inside

- **Hook** (`hooks/session.js`, SessionStart only): prints the tier rules, this project's know-how titles, and the cleanup reminder when due. Nothing runs on each tool call.
- **Skills**
  - `md-cleanup`: the cleanup described above.
  - `commit-notes`: per-task commit notes, and "커밋해" commits from them after checking the diff.
  - `verify-todo`: things to check by hand. "todo 테스트해" verifies them and deletes the ones that pass.
  - `knowhow`: format and rules for tips, showing/tidying them, promotion.

Know-how, work notes, cleanup backups and `.claude/md-tiers.json` (last cleanup date) are added to `.git/info/exclude` when first created, so nothing shows up in `git status`.

Requirements: Node.js on PATH (the hook is a small Node script). Works on any OS; written and tested on Windows.

Install per project (recommended; `local` = this project, just you):

```
claude plugin install md-tiers@zerodoc-skills --scope local
```
