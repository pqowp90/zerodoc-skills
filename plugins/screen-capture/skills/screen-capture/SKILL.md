---
name: screen-capture
description: Capture a screenshot of the Windows desktop, a specific monitor, a region, or a single app window (by process name, title, PID or HWND) into a PNG, then view it with Read. Use whenever you need to SEE what is on screen — verifying a GUI/desktop app you built or launched (WPF, WinForms, Unity, Electron, games), checking a UI change visually, reading an error dialog, or when the user says "캡처", "스크린샷", "화면 봐줘", "screenshot", "look at the screen". Windows only; no build step.
---

# screen-capture

One self-contained script: `${CLAUDE_SKILL_DIR}/capture.ps1`
(Windows PowerShell 5.1+, uses only System.Drawing/WinForms + user32/dwmapi P/Invoke).

Always invoke through a fresh process so DPI awareness is set correctly:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_SKILL_DIR}/capture.ps1" <args>
```
(From the Bash tool use `"${CLAUDE_SKILL_DIR}/capture.ps1"`.)

## Workflow

1. Don't know the target? Run `-List` → monitors (index, bounds) and visible top-level windows (HWND, PID, process, size, title).
2. Capture. The last stdout line is machine-readable:
   `CAPTURED: <abs path> | <w>x<h> [(native WxH)] | <PrintWindow|ScreenCopy> | <source>`
3. `Read` the path to actually look at the image. Do not claim what the UI looks like without doing this.

## Targets (pick one)

| Args | Captures |
|---|---|
| *(none)* | primary monitor |
| `-Monitor 1` | monitor index from `-List` |
| `-AllMonitors` | whole virtual desktop |
| `-Window notepad` | window whose **process name** equals it (`.exe` optional); otherwise **title** contains it. Several matches → largest non-minimized, candidates printed to stderr |
| `-ProcessId 1234` / `-Hwnd 0x6C0C26` | exact window |
| `-Region "x,y,w,h"` | screen rectangle, physical desktop pixels (monitors can have negative coords) |

## Options

- `-Out path.png` — default `%TEMP%\claude-captures\cap_<timestamp>.png`. `.jpg`/`.bmp` also accepted.
- `-MaxSize 1024` (default) — downscales the longest edge to save vision tokens (≈ w×h/750 tokens: 1024x576 ≈ 790 vs native 1920x1080 ≈ 2,760). Tested: normal 100%-scale Windows UI text, incl. Korean, stays readable at 1024 on a 1920 screen.
  - Text too small / unsure what it says? Do NOT recapture the whole screen bigger. Capture just that window or `-Region` — a small area at native size costs fewer tokens and is sharper.
  - Use `-MaxSize 0` for native pixels when checking alignment, 1px borders, colours or text crispness.
- `-WaitSeconds N` — poll until the window appears (use right after launching an app).
- `-DelayMs N` — wait before grabbing (animations, splash screens).
- `-Activate` — restore if minimized and bring to front. Off by default so the user's focus isn't stolen; required for minimized windows.
- `-ScreenCopy` — skip PrintWindow and copy what is visibly on screen.

## How window capture works / limits

- Default is `PrintWindow(PW_RENDERFULLCONTENT)`: works for occluded windows and most GPU-rendered apps (verified with WPF, Win11 Notepad, Unity DX11 editor, Chrome). The result is cropped to the DWM visible frame (no invisible resize border).
- If PrintWindow returns an all-black image, the script falls back to ScreenCopy automatically and says so on stderr. ScreenCopy shows whatever is on top in that area — add `-Activate` if something overlaps.
- Exclusive-fullscreen games, DRM/protected video and windows with `WDA_EXCLUDEFROMCAPTURE` may come out black. That is the OS refusing, not a script bug — report it as such.
- Cannot capture a locked workstation, the secure desktop (UAC prompt), or from a non-interactive session (service / SSH).

## Exit codes

`0` ok · `1` bad args / no match / minimized without `-Activate` · `2` capture produced nothing. Errors go to stderr as `ERROR: ...` — surface them, don't retry blindly.

## Typical recipes

```powershell
# launch an app you just built and look at it
Start-Process .\bin\Release\MyApp.exe
powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_SKILL_DIR}/capture.ps1" -Window MyApp -WaitSeconds 15 -DelayMs 800

# pixel-exact check of one window, saved into the project
... capture.ps1 -Window MyApp -MaxSize 0 -Out .\screenshots\main.png
```

Delete `%TEMP%\claude-captures` contents freely; nothing else depends on them.
