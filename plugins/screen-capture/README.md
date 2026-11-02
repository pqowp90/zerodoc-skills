# screen-capture

Lets Claude see your Windows screen. The skill captures the whole desktop, a single monitor,
a rectangular region, or one application window (found by process name, window title, PID or HWND)
into a PNG file, which Claude then opens with its Read tool. By default the long side is scaled
down to 1024px to keep token usage low; pass `-MaxSize 0` for full resolution.

Typical uses: checking a GUI app you just built or launched, reading an error dialog,
verifying a UI change visually. Claude triggers it on requests like "screenshot", "캡처", "화면 봐줘".

Requirements: Windows with PowerShell 5.1 or later. No build step and no extra modules —
the script uses System.Drawing / WinForms and a few user32/dwmapi calls.

```
claude plugin install screen-capture@zerodoc-skills
```
