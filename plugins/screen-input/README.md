# screen-input

Lets Claude operate a Windows app window: click, double/right click, drag, scroll, type text
(Korean included, sent as Unicode so no IME is involved) and press key combos. It finds windows
exactly like [screen-capture](../screen-capture) and takes coordinates from its screenshots
(`-ImageSize` scales coordinates from a downscaled capture), so the loop is
capture → decide → input → capture.

Safety: the target window is brought to the front first and checked before every step; a click
on a point outside the window or covered by another window is refused, and the run stops there.
The mouse is put back where it was afterwards.

Requirements: Windows with PowerShell 5.1 or later. No build step.

```
claude plugin install screen-input@zerodoc-skills
```
