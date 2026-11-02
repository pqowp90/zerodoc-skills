# screen-ui

Lets Claude read and operate a Windows app window through UI Automation (the accessibility tree)
instead of the mouse. A snapshot lists the actionable elements with refs (`e12`). Steps then act
on a ref, an automation id (`#send-btn`) or a name: click (Invoke), set text (Value), toggle,
select, expand/collapse, scroll and wait for text. The mouse never moves and focus stays where the
user has it. It works on windows behind other windows, and on WebView2/Electron, WinUI, WPF,
WinForms and Win32 apps.

Use [screen-input](../screen-input) for what UI Automation can't reach: canvas content, drag,
right/double click and raw key combos. `get` prints an element's center in screen-input's
window coordinates.

The approach (ref snapshots, action through patterns rather than synthetic input, waiting for
Chromium trees to settle) follows [vercel-labs/agent-desktop](https://github.com/vercel-labs/agent-desktop)
(Apache-2.0). This is a small independent rewrite of the core idea: no code was copied.

Requirements: Windows with PowerShell 5.1 or later. No build step: `ui.cs` is compiled on first
use and cached in `%TEMP%\claude-screen-ui\`.

```
claude plugin install screen-ui@zerodoc-skills
```
