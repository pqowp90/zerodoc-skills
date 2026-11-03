# zerodoc-skills

Claude Code plugin marketplace with the default skills for [zerodocDesk](https://github.com/pqowp90).
Each skill is its own plugin, so you can turn them on per account or per project.

zerodocDesk 기본 스킬 모음입니다. 스킬마다 플러그인 하나라서 계정 공통 / 프로젝트별로 따로 켜고 끌 수 있어요.

## Install / 설치

In Claude Code:

```
/plugin marketplace add pqowp90/zerodoc-skills
/plugin install screen-capture@zerodoc-skills
```

or from a terminal:

```
claude plugin marketplace add pqowp90/zerodoc-skills
claude plugin install screen-capture@zerodoc-skills --scope user     # 계정 공통
claude plugin install screen-capture@zerodoc-skills --scope local    # 이 프로젝트, 나만
```

## Skills

| Plugin | What it does | Platform |
|---|---|---|
| `screen-capture` | Captures the whole desktop, one monitor, a region, or a single app window (by process name, title, PID or HWND) into a PNG that Claude then reads. Long side is scaled to 1024px by default to save tokens. | Windows (PowerShell 5.1+) |
| `screen-input` | Real mouse and keyboard input into one app window at screen-capture's coordinates: drag, right/double click, hover, raw key combos, typing into terminals or canvas (Korean OK). It takes the mouse and focus while it runs, so use `screen-ui` first for buttons and fields. Checks the window is in front and not covered before every step. | Windows (PowerShell 5.1+) |
| `ui-check` | The loop for fixing a GUI problem by looking instead of guessing: build, run a separate test copy, screenshot, reproduce with `screen-ui` (`screen-input` last), fix, screenshot again before reporting. Installing it also installs `screen-capture`, `screen-ui` and `screen-input`. | Windows |
| `screen-ui` | Reads a window's UI Automation tree as a list of actionable elements with refs, then clicks, sets text, toggles, selects, expands and scrolls them by ref, id or name. It never moves the mouse or takes focus, and works on windows behind others (WebView2/Electron, WinUI, WPF, WinForms, Win32). | Windows (PowerShell 5.1+) |
| `md-tiers` | Claude manages the project's markdown files in three tiers: never stops to ask: 🔒 instructions (CLAUDE.md, skills, settings, hooks, memory) change only when asked or when the code they describe changes; 💡 know-how in `.claude/knowhow/` shows up as a promotion candidate once used 3 times; ✏️ per-task work notes (commit notes, things to verify by hand). All per project; "md 정리해" runs an `md-cleanup` of stale, guessed or duplicated lines. Install per project (`--scope local`). | Any (Node.js) |

## Adding a skill / 스킬 추가

1. `plugins/<name>/.claude-plugin/plugin.json`
2. `plugins/<name>/skills/<name>/SKILL.md` (+ scripts; reference them as `${CLAUDE_SKILL_DIR}/...`)
3. Add an entry to `.claude-plugin/marketplace.json`
4. `claude plugin validate --strict .` and `claude plugin validate --strict plugins/<name>`

## License

MIT
