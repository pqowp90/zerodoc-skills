---
name: ui-check
description: Check and fix a GUI problem by actually running the app and looking at it, instead of guessing from code. Use whenever the user points out something wrong on screen in an app you are building ("화면이 이상해", "깨져 보여", "버튼이 안 눌려", "안 보여", "레이아웃", "UI 확인해", "looks wrong", "check the UI"), and before reporting any UI change as done. Builds the app, runs a separate test copy, captures it with screen-capture, operates it with screen-ui (screen-input only as a last resort), fixes, and captures again. Windows only.
---

# ui-check

Never fix a UI problem from the code alone, and never report a UI change as done with
"couldn't check because I didn't build". The loop is: **build → run → see → fix → build → see again → report.**

## 1. Build and run a test copy

- Build with the project's normal debug build (see the project's CLAUDE.md / README).
- Run it as a **separate test instance**, next to the copy the user is working in. If the project
  has a way to run side by side (its own port, config dir or env var, e.g. `ZD_DEV_PORT`), use it.
  Never close, kill or rebuild over the user's running copy: if the build fails because a file is
  locked, build to another target dir instead.
- Wait until the window is up (poll for it with screen-capture or screen-ui `-Window <name>`),
  don't sleep blindly.

## 2. See it

- Capture the window with **screen-capture** and Read the PNG. Look at the actual problem the user
  described before forming a theory.
- To read the state as text (labels, values, which item is selected), take a **screen-ui** snapshot.

## 3. Reach the broken state

Operate the app to reproduce what the user saw, in this order:

1. **screen-ui** first: clicks, typing into fields, toggles, tabs, lists, scrolling. It does not
   move the mouse or take focus, so the user can keep working.
2. **screen-input** only for what screen-ui can't do: drag, canvas, terminals, raw key combos,
   real hit-testing/IME. It takes the user's mouse and focus while it runs, so say so in one line
   before using it.

Capture again once you are in the broken state.

## 4. Fix, then prove it

- Fix the cause, rebuild (assets embedded in the binary need a rebuild, not just a reload), rerun,
  repeat the same steps, and capture again.
- Compare before/after yourself. If it is still wrong, keep going; don't hand it back half done.
- Report with what you saw: the cause, the fix, and that the final screenshot shows it fixed.
  The final check screenshot is taken with screen-capture.
- Close the test instance you started when you are done (only that one).

If something truly can't be checked here (another device, a real phone, a different monitor setup),
say exactly which part was not checked and why.
