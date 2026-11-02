---
name: screen-ui
description: Read and operate a Windows app window through UI Automation, without moving the mouse or taking focus. It lists the window's buttons, fields, checkboxes, tabs, lists and scroll areas with refs (e12), then clicks, sets text, toggles, selects, expands or scrolls them by ref, #automationId or name. It works on windows behind other windows, and on WebView2/Electron/Chromium, WinUI, WPF, WinForms and Win32 apps. Use it FIRST to drive a GUI you are testing ("클릭해", "눌러 봐", "입력해 봐", "버튼 눌러", "click", "fill in"), and to read a window's state as text. Fall back to screen-input only for what has no UI Automation pattern (canvas, games, terminals, drag, raw keys). Windows only; no build step.
---

# screen-ui

`${CLAUDE_SKILL_DIR}/ui.ps1` (Windows PowerShell 5.1+, .NET UI Automation). It compiles `ui.cs` once
per version into `%TEMP%\claude-screen-ui\`, and later calls take about a second.

```
powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_SKILL_DIR}/ui.ps1" -Window <name> [-Do "<steps>"]
```

Target, same matching as screen-capture: `-Window deskdoc` (process name, else title substring) ·
`-ProcessId 1234` · `-Hwnd 0x6C0C26`.

## Loop: snapshot → act → check

1. **Snapshot** (no `-Do`): every actionable element, indented under its named containers.
   ```
   SNAPSHOT | window 0x250C10 zd-app 'DeskDoc (테스트)' | 44 refs, 433 nodes
       document "DeskDoc":
         e208 button "+ Claude" [#new-claude]
         e223 group (에이전트 데스크 가상 마우스 · 방금)
         e239 textfield "메시지 입력" = "hi" [#compose-text, focused]
         e241 checkbox "자동 승인" [on]
   ```
   `#…` is the automation id (the HTML `id` in web apps). `(…)` is the text inside an unnamed
   clickable element. The states are `disabled`, `on`/`off`, `selected`, `expanded`/`collapsed`,
   `scroll N%`, `focused` and `offscreen`.
   - `-Find TEXT`: only elements whose name, value, id or role contains TEXT. Use it on big windows.
   - `-Text`: also list static text, to read what the window says.
   - `-All`: also list offscreen elements. They are hidden by default, and a count is printed.
2. **Act** with `-Do "step; step"`. Add `-Snap` to get a fresh snapshot in the same call.
3. **Check**: each step prints its outcome (`invoked`, `set (verified)`, `now on`, `scrolled …`).
   A UIA "invoked" means the app received it, not that it did the right thing. Confirm with a
   snapshot, a `get`, or a screen-capture before reporting a feature as working.

## Steps (`-Do`, separated by `;`; `\;` is a literal semicolon)

| Step | Does (pattern used) |
|---|---|
| `click T` | Invoke. If there is none: Select, then Toggle, then Expand/Collapse |
| `set T TEXT` | Value.SetValue (replaces the whole value, read back to verify; `\n` = newline) |
| `toggle T [on\|off]` · `check T` · `uncheck T` | Toggle until it reads the wanted state |
| `select T [OPTION]` | Select the item. With OPTION, open the dropdown/list T and pick that option |
| `expand T` · `collapse T` | ExpandCollapse (menus, tree nodes, dropdowns) |
| `scroll T up\|down\|left\|right\|pageup\|pagedown\|top\|bottom [N]` | Scroll T, or its nearest scrollable ancestor |
| `scrollto T` | ScrollItem.ScrollIntoView |
| `get T` | Details: value, states, actions, help text, the text inside, and center coords for screen-input |
| `focus T` | SetFocus. **This can bring the window to the front**, so use it only when needed |
| `wait MS` · `waitfor TEXT [MS]` · `waitgone TEXT [MS]` | Pause, or poll until some element's name/value contains TEXT, or until none does. The default is 10 s |

**T (target)**
- `e12`: a ref from a snapshot of this window.
- `#compose-text`: an automation id.
- `보내기`: a name (exact match first, then partial).
- `button:보내기`: a name limited to one role.
- Quote a name that has spaces when an argument follows it: `set "검색어 입력" hello`.

A name that matches several elements fails and lists their refs. A ref is never reused for a
different element. If its element was re-rendered, it is found again by role and name, and if
that is not unique the step fails as stale. Take a new snapshot then.

## When to use screen-input instead

- The step fails with "has no UI Automation pattern for …". The error gives coordinates, and
  `get` shows `center: X,Y`. Those are window coords that screen-input's `click X,Y` takes as they are.
- Canvas content (games, xterm.js terminals, charts), drag-and-drop, right-click menus,
  double-click, and raw keys or shortcuts (`ctrl+s`, Enter to send).
- **Web apps:** Invoke runs the element's click handler. A control that only listens to
  `mousedown`/`pointerdown` won't react, so use screen-input for those.

## Notes

- Web, Chromium and WebView2 windows build their tree after the first read. The snapshot waits
  until the tree stops growing (`-TimeoutMs`, default 6000). If you see "the tree is small", run it again.
- A minimized window can't be read. `-Restore` restores it without taking focus.
- An elevated (admin) window can't be driven from a non-elevated shell.
- A call that opens a modal dialog may not return. The step stops waiting after 4 s and says so.
- Password fields: values are never printed or read back.
- Exit codes: `0` ok · `1` bad args / no window · `2` target not found, stale or ambiguous ·
  `3` action failed or not supported. On failure, the steps before the failing one were done.
- Never type secrets, and never click destructive buttons (delete, purchase, send) unless asked.
