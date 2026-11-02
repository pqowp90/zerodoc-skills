<#
.SYNOPSIS
  Mouse / keyboard input into one Windows app window, for AI agents. Pairs with
  screen-capture: same window lookup, same coordinates (relative to the window's
  visible frame, i.e. pixels of a capture of that window). No build step.

.EXAMPLES
  input.ps1 -Window notepad -Do "click 200,150; type 안녕하세요; key enter"
  input.ps1 -ProcessId 1234 -Do "click 512,300" -ImageSize 1024x692   # coords from a downscaled capture
  input.ps1 -Window MyApp -Do "dclick 40,80; key ctrl+a; key ctrl+c"
  input.ps1 -Window MyApp -Do "scroll 600,400 -3; drag 100,100 300,100; wait 500"

  Steps (separated by ';'):
    click X,Y | rclick X,Y | dclick X,Y | move X,Y | drag X1,Y1 X2,Y2 | scroll X,Y N (N>0 up, <0 down)
    type TEXT (Unicode, Korean ok; '\n' = Enter, '\t' = Tab) | key COMBO (ctrl+shift+e, enter, f5, alt+tab...)
    wait MS
  Last stdout line on success:  INPUT: <n> steps | window 0x.. <process> '<title>'
  Exit codes: 0 ok, 1 bad args / no window, 3 aborted for safety (window lost focus / covered).
#>
[CmdletBinding(DefaultParameterSetName = 'Window')]
param(
    [Parameter(ParameterSetName = 'Window', Mandatory)] [string]$Window,
    [Parameter(ParameterSetName = 'Pid', Mandatory)]    [int]$ProcessId,
    [Parameter(ParameterSetName = 'Hwnd', Mandatory)]   [string]$Hwnd,
    [Parameter(Mandatory)] [string]$Do,
    [string]$ImageSize,          # "WxH" of the (downscaled) capture the coordinates come from
    [int]$StepDelayMs = 80,      # pause between steps so the app can react
    [switch]$KeepCursor          # leave the mouse where the last step put it (default: put it back)
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$OutputEncoding = [Console]::OutputEncoding

if (-not ('ClaudeInput.Native' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClaudeInput {
  public class WinInfo {
    public IntPtr Hwnd; public int Pid; public string Process; public string Title;
    public int X, Y, W, H; public bool Minimized;
  }

  public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int cb);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int cb);

    public static void InitDpi() {
      try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
      SetProcessDPIAware();
    }

    public static RECT VisibleRect(IntPtr h) {
      RECT r;
      if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
      return r;
    }

    static bool IsCloaked(IntPtr h) { int v; return DwmGetWindowAttribute(h, 14, out v, 4) == 0 && v != 0; }

    // Same rules as screen-capture's -List, so the same -Window/-ProcessId picks the same window.
    public static List<WinInfo> ListWindows() {
      var list = new List<WinInfo>();
      var names = new Dictionary<uint, string>();
      EnumWindows(delegate (IntPtr h, IntPtr l) {
        if (!IsWindowVisible(h) || IsCloaked(h)) return true;
        if (GetWindow(h, 4) != IntPtr.Zero) return true;
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        if (sb.Length == 0) return true;
        uint pid; GetWindowThreadProcessId(h, out pid);
        string pname;
        if (!names.TryGetValue(pid, out pname)) {
          try { pname = Process.GetProcessById((int)pid).ProcessName; } catch { pname = "<exited>"; }
          names[pid] = pname;
        }
        RECT r = VisibleRect(h);
        bool min = IsIconic(h);
        if (!min && (r.R - r.L < 20 || r.B - r.T < 20)) return true;
        list.Add(new WinInfo { Hwnd = h, Pid = (int)pid, Process = pname, Title = sb.ToString(),
          X = r.L, Y = r.T, W = r.R - r.L, H = r.B - r.T, Minimized = min });
        return true;
      }, IntPtr.Zero);
      return list;
    }

    // Foreground lock: Windows only lets the foreground thread hand focus over. Borrow its
    // input queue for a moment, then check that it actually worked.
    public static bool Activate(IntPtr h) {
      if (IsIconic(h)) ShowWindow(h, 9);
      for (int i = 0; i < 3 && GetForegroundWindow() != h; i++) {
        IntPtr fg = GetForegroundWindow();
        uint pid;
        uint fgThread = GetWindowThreadProcessId(fg, out pid), me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        if (attached) AttachThreadInput(me, fgThread, false);
        if (GetForegroundWindow() != h) { Key(0x12, false); Key(0x12, true); SetForegroundWindow(h); } // ALT tap unlocks it
        Thread.Sleep(120);
      }
      return GetForegroundWindow() == h;
    }

    public static bool IsForeground(IntPtr h) { return GetForegroundWindow() == h; }

    // The top-level window under a screen point (what a click there would actually hit).
    public static IntPtr RootAt(int x, int y) {
      var p = new POINT { X = x, Y = y };
      return GetAncestor(WindowFromPoint(p), 2 /*GA_ROOT*/);
    }

    public static POINT Cursor() { POINT p; GetCursorPos(out p); return p; }
    public static void PutCursor(int x, int y) { SetCursorPos(x, y); }

    static void Send(INPUT[] a) { SendInput((uint)a.Length, a, Marshal.SizeOf(typeof(INPUT))); }

    // Absolute moves are in 0..65535 over the virtual desktop.
    public static void MoveTo(int x, int y) {
      int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
      var m = new MOUSEINPUT {
        dx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1)),
        dy = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1)),
        flags = 0x0001 | 0x8000 | 0x4000 /*MOVE|ABSOLUTE|VIRTUALDESK*/ };
      Send(new[] { new INPUT { type = 0, u = new UNION { mi = m } } });
    }

    public static void Button(bool right, bool up) {
      uint f = right ? (up ? 0x0010u : 0x0008u) : (up ? 0x0004u : 0x0002u);
      Send(new[] { new INPUT { type = 0, u = new UNION { mi = new MOUSEINPUT { flags = f } } } });
    }

    public static void Wheel(int notches) {
      var m = new MOUSEINPUT { flags = 0x0800, data = (uint)(notches * 120) };
      Send(new[] { new INPUT { type = 0, u = new UNION { mi = m } } });
    }

    static readonly HashSet<ushort> Extended = new HashSet<ushort> { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C };
    public static void Key(ushort vk, bool up) {
      uint f = (up ? 0x0002u : 0u) | (Extended.Contains(vk) ? 0x0001u : 0u);
      Send(new[] { new INPUT { type = 1, u = new UNION { ki = new KEYBDINPUT { vk = vk, flags = f } } } });
    }

    // Each UTF-16 unit as a Unicode keystroke: no keyboard layout / IME involved, Korean works.
    public static void Unicode(string s) {
      foreach (char c in s) {
        Send(new[] {
          new INPUT { type = 1, u = new UNION { ki = new KEYBDINPUT { scan = c, flags = 0x0004 } } },
          new INPUT { type = 1, u = new UNION { ki = new KEYBDINPUT { scan = c, flags = 0x0004 | 0x0002 } } } });
        Thread.Sleep(4);
      }
    }

    public static ushort VkForChar(char c) { short r = VkKeyScan(c); return r == -1 ? (ushort)0 : (ushort)(r & 0xFF); }
  }
}
'@
}

function Fail([int]$code, [string]$msg) {
    [Console]::Error.WriteLine("ERROR: $msg")
    exit $code
}

[ClaudeInput.Native]::InitDpi()

# ---- target window ----
$wins = [ClaudeInput.Native]::ListWindows()
$c = @(switch ($PSCmdlet.ParameterSetName) {
    'Window' {
        $q = $Window -replace '\.exe$', ''
        $byProc = @($wins | Where-Object { $_.Process -ieq $q })
        if ($byProc.Count) { $byProc } else { @($wins | Where-Object { $_.Title -like "*$Window*" }) }
    }
    'Pid'  { @($wins | Where-Object { $_.Pid -eq $ProcessId }) }
    'Hwnd' {
        $h = [Int64]$(if ($Hwnd -match '^0x') { [Convert]::ToInt64($Hwnd.Substring(2), 16) } else { $Hwnd })
        @($wins | Where-Object { $_.Hwnd.ToInt64() -eq $h })
    }
})
if ($c.Count -gt 1) {
    [Console]::Error.WriteLine("NOTE: $($c.Count) windows matched; using the largest non-minimized one (pass -Hwnd to choose).")
}
$t = $c | Sort-Object @{ e = { -not $_.Minimized }; Descending = $true }, @{ e = { $_.W * $_.H }; Descending = $true } | Select-Object -First 1
if (-not $t) { Fail 1 "No visible window matched. Run screen-capture's -List to see candidates." }
$h = $t.Hwnd
$desc = "window 0x{0:X} {1} '{2}'" -f $h.ToInt64(), $t.Process, $t.Title

# ---- parse steps up front: a typo must not leave half the steps done ----
$scale = 1.0
if ($ImageSize) {
    if ($ImageSize -notmatch '^\s*(\d+)\s*x\s*(\d+)\s*$') { Fail 1 "-ImageSize needs WxH (got '$ImageSize')" }
    $r0 = [ClaudeInput.Native]::VisibleRect($h)
    $scale = ($r0.R - $r0.L) / [double]$Matches[1]
}
function Pt([string]$s) {
    if ($s -notmatch '^\s*(-?\d+)\s*,\s*(-?\d+)\s*$') { throw "bad point '$s' (want X,Y)" }
    [pscustomobject]@{ X = [int]$Matches[1]; Y = [int]$Matches[2] }
}
$vkNames = @{
    ctrl = 0x11; control = 0x11; shift = 0x10; alt = 0x12; win = 0x5B; enter = 0x0D; return = 0x0D; tab = 0x09
    esc = 0x1B; escape = 0x1B; space = 0x20; backspace = 0x08; bs = 0x08; delete = 0x2E; del = 0x2E; insert = 0x2D
    home = 0x24; end = 0x23; pageup = 0x21; pgup = 0x21; pagedown = 0x22; pgdn = 0x22
    left = 0x25; up = 0x26; right = 0x27; down = 0x28; apps = 0x5D; menu = 0x5D; hangul = 0x15; hanja = 0x19
}
function Vk([string]$name) {
    $n = $name.Trim().ToLower()
    if ($vkNames.ContainsKey($n)) { return [uint16]$vkNames[$n] }
    if ($n -match '^f(\d{1,2})$' -and [int]$Matches[1] -ge 1 -and [int]$Matches[1] -le 24) { return [uint16](0x6F + [int]$Matches[1]) }
    if ($n.Length -eq 1) { $v = [ClaudeInput.Native]::VkForChar($n[0]); if ($v) { return $v } }
    throw "unknown key '$name'"
}
$steps = @()
try {
    foreach ($raw in ($Do -split ';')) {
        $s = $raw.Trim()
        if (-not $s) { continue }
        $verb, $rest = $s -split '\s+', 2
        $verb = $verb.ToLower()
        switch ($verb) {
            { $_ -in 'click', 'rclick', 'dclick', 'move' } { $steps += [pscustomobject]@{ Verb = $verb; P = (Pt $rest) } }
            'drag'   { $a, $b = $rest -split '\s+', 2; $steps += [pscustomobject]@{ Verb = 'drag'; P = (Pt $a); Q = (Pt $b) } }
            'scroll' { $a, $n = $rest -split '\s+', 2; $steps += [pscustomobject]@{ Verb = 'scroll'; P = (Pt $a); N = [int]$n } }
            'type'   { $steps += [pscustomobject]@{ Verb = 'type'; Text = ($rest -replace '\\n', "`n" -replace '\\t', "`t") } }
            'key'    { $steps += [pscustomobject]@{ Verb = 'key'; Keys = @($rest -split '\+' | ForEach-Object { Vk $_ }); Combo = $rest } }
            'wait'   { $steps += [pscustomobject]@{ Verb = 'wait'; Ms = [int]$rest } }
            default  { throw "unknown step '$verb'" }
        }
    }
} catch { Fail 1 "-Do: $($_.Exception.Message)" }
if (-not $steps.Count) { Fail 1 '-Do has no steps' }

# ---- run ----
if (-not [ClaudeInput.Native]::Activate($h)) {
    Fail 3 "Could not bring $desc to the front (another app holds focus). Nothing was sent."
}
$home0 = [ClaudeInput.Native]::Cursor()

function ToScreen($p) {
    $r = [ClaudeInput.Native]::VisibleRect($h)   # re-read: the window may have moved
    [pscustomobject]@{ X = $r.L + [int][Math]::Round($p.X * $scale); Y = $r.T + [int][Math]::Round($p.Y * $scale) }
}
function Guard($sp) {
    if (-not [ClaudeInput.Native]::IsForeground($h)) { throw "SAFETY: $desc is no longer in front" }
    if ($sp) {
        $r = [ClaudeInput.Native]::VisibleRect($h)
        if ($sp.X -lt $r.L -or $sp.X -ge $r.R -or $sp.Y -lt $r.T -or $sp.Y -ge $r.B) { throw "SAFETY: point is outside the window ($($sp.X),$($sp.Y))" }
        if ([ClaudeInput.Native]::RootAt($sp.X, $sp.Y) -ne $h) { throw "SAFETY: another window covers ($($sp.X),$($sp.Y))" }
    }
}

$done = 0
try {
    foreach ($st in $steps) {
        switch ($st.Verb) {
            { $_ -in 'click', 'rclick', 'dclick', 'move' } {
                $sp = ToScreen $st.P; Guard $sp
                [ClaudeInput.Native]::MoveTo($sp.X, $sp.Y); Start-Sleep -Milliseconds 30
                if ($st.Verb -ne 'move') {
                    $right = $st.Verb -eq 'rclick'
                    $n = if ($st.Verb -eq 'dclick') { 2 } else { 1 }
                    for ($i = 0; $i -lt $n; $i++) { [ClaudeInput.Native]::Button($right, $false); [ClaudeInput.Native]::Button($right, $true); Start-Sleep -Milliseconds 40 }
                }
            }
            'drag' {
                $a = ToScreen $st.P; $b = ToScreen $st.Q; Guard $a; Guard $b
                [ClaudeInput.Native]::MoveTo($a.X, $a.Y); Start-Sleep -Milliseconds 30
                [ClaudeInput.Native]::Button($false, $false)
                for ($i = 1; $i -le 10; $i++) {
                    [ClaudeInput.Native]::MoveTo([int]($a.X + ($b.X - $a.X) * $i / 10), [int]($a.Y + ($b.Y - $a.Y) * $i / 10)); Start-Sleep -Milliseconds 15
                }
                [ClaudeInput.Native]::Button($false, $true)
            }
            'scroll' {
                $sp = ToScreen $st.P; Guard $sp
                [ClaudeInput.Native]::MoveTo($sp.X, $sp.Y); Start-Sleep -Milliseconds 30
                [ClaudeInput.Native]::Wheel($st.N)
            }
            'type' {
                Guard $null
                foreach ($part in ($st.Text -split "(`n|`t)")) {
                    if ($part -eq "`n") { [ClaudeInput.Native]::Key(0x0D, $false); [ClaudeInput.Native]::Key(0x0D, $true) }
                    elseif ($part -eq "`t") { [ClaudeInput.Native]::Key(0x09, $false); [ClaudeInput.Native]::Key(0x09, $true) }
                    elseif ($part) { [ClaudeInput.Native]::Unicode($part) }
                }
            }
            'key' {
                Guard $null
                foreach ($k in $st.Keys) { [ClaudeInput.Native]::Key($k, $false) }
                [array]::Reverse($st.Keys)
                foreach ($k in $st.Keys) { [ClaudeInput.Native]::Key($k, $true) }
            }
            'wait' { Start-Sleep -Milliseconds $st.Ms }
        }
        $done++
        Start-Sleep -Milliseconds $StepDelayMs
    }
} catch {
    if (-not $KeepCursor) { [ClaudeInput.Native]::PutCursor($home0.X, $home0.Y) }
    $code = if ($_.Exception.Message -like 'SAFETY:*') { 3 } else { 1 }
    Fail $code "step $($done + 1)/$($steps.Count) ($($steps[$done].Verb)): $($_.Exception.Message). Steps 1..$done were sent."
}
if (-not $KeepCursor) { [ClaudeInput.Native]::PutCursor($home0.X, $home0.Y) }
Write-Output "INPUT: $done steps | $desc"
exit 0
