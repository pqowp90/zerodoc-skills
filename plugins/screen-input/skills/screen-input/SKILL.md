---
name: screen-input
description: Click, double-click, right-click, drag, scroll, type text (Korean OK) and press key combos inside one Windows app window (by process name, title, PID or HWND), using the same coordinates as a screen-capture screenshot of that window. Use to DRIVE a GUI you are testing — open a menu or panel, press a button, fill a field — then look at the result with screen-capture. Also when the user says "클릭해", "눌러 봐", "입력해 봐", "click", "type into". Windows only; no build step.
---

# screen-input

One self-contained script: `${CLAUDE_SKILL_DIR}/input.ps1` (Windows PowerShell 5.1+, user32 `SendInput`).

```
powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_SKILL_DIR}/input.ps1" -Window <name> -Do "<steps>"
```

## Workflow (with screen-capture)

1. Capture the window with screen-capture and `Read` it.
2. Take coordinates **from that image**. If the CAPTURED line says `1024x692 (native 1202x812)`,
   pass `-ImageSize 1024x692` so they are scaled to real pixels. Native captures need no flag.
3. Run the steps, then capture again and `Read` it to see what happened. Never report a click
   as working without looking at the result.

## Target (pick one) — same matching as screen-capture

`-Window notepad` (process name, else title substring) · `-ProcessId 1234` · `-Hwnd 0x6C0C26`

## Steps (`-Do`, separated by `;`)

| Step | Does |
|---|---|
| `click X,Y` / `rclick X,Y` / `dclick X,Y` | left / right / double click |
| `move X,Y` | hover (tooltips, hover menus) |
| `drag X1,Y1 X2,Y2` | press, move in 10 steps, release |
| `scroll X,Y N` | wheel at a point, N notches (positive = up) |
| `type TEXT` | Unicode text, Korean/emoji fine (no IME needed); `\n` = Enter, `\t` = Tab |
| `key COMBO` | `enter`, `esc`, `tab`, `f5`, `ctrl+a`, `ctrl+shift+e`, `alt+f4`, `left`… |
| `wait MS` | pause (let a panel open / an animation finish) |

X,Y are relative to the window's visible frame (top-left of a capture of that window = 0,0).

## Options

- `-ImageSize WxH` — size of the downscaled capture the coordinates come from.
- `-StepDelayMs 80` — pause after each step.
- `-KeepCursor` — leave the mouse where it ended (default: put it back where the user had it).

## Safety (built in — don't work around it)

- The window is brought to the front first; if Windows refuses, nothing is sent (exit 3).
- Before every step it checks the window is still in front, and before every click that the
  point is inside the window and not covered by another window. Otherwise it stops (exit 3)
  and says which steps were already sent.
- This is real input on the user's desktop: it takes focus and moves the mouse for a moment.
  Tell the user before a long sequence, and never type secrets or click destructive buttons
  (delete, purchase, send) unless asked.

## When a browser/WebView-specific tool is better

For pages in a browser or WebView2 with a debugging port, DevTools (CDP) can click by element
and read DOM state without taking focus. Use screen-input for everything else (native apps,
WPF/WinForms, Unity, dialogs) and to test the real input path (focus, hit-testing, IME).

## Exit codes

`0` ok · `1` bad args / no window · `3` stopped for safety. Last stdout line on success:
`INPUT: <n> steps | window 0x.. <process> '<title>'`.
