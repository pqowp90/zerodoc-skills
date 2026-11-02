<#
.SYNOPSIS
  Screen / window capture for AI agents on Windows. No build step, no dependencies
  beyond Windows PowerShell 5.1+ (System.Drawing / WinForms ship with Windows).

.EXAMPLES
  capture.ps1 -List                         # monitors + visible top-level windows
  capture.ps1                               # primary monitor
  capture.ps1 -Monitor 1                    # monitor index from -List
  capture.ps1 -AllMonitors                  # whole virtual desktop
  capture.ps1 -Window notepad               # process name (exact) or title substring
  capture.ps1 -Window "My App" -WaitSeconds 10   # wait for the window to appear
  capture.ps1 -ProcessId 1234
  capture.ps1 -Region 0,0,800,600           # x,y,w,h in physical desktop pixels
  capture.ps1 -Window code -MaxSize 0       # keep native resolution (pixel inspection)

  Last stdout line on success:  CAPTURED: <path> | <w>x<h> | <method> | <source>
  Exit codes: 0 ok, 1 bad args / target not found, 2 capture failed.
#>
[CmdletBinding(DefaultParameterSetName = 'Monitor')]
param(
    [Parameter(ParameterSetName = 'List')]    [switch]$List,
    [Parameter(ParameterSetName = 'Monitor')] [int]$Monitor = -1,
    [Parameter(ParameterSetName = 'All')]     [switch]$AllMonitors,
    [Parameter(ParameterSetName = 'Window')]  [string]$Window,
    [Parameter(ParameterSetName = 'Pid')]     [int]$ProcessId,
    [Parameter(ParameterSetName = 'Hwnd')]    [string]$Hwnd,
    [Parameter(ParameterSetName = 'Region')]  [string]$Region,   # "x,y,w,h"

    [string]$Out,
    [int]$MaxSize = 1024,        # longest edge in px; 0 = no downscale
    [int]$DelayMs = 0,
    [int]$WaitSeconds = 0,       # window modes: poll until the window exists
    [switch]$Activate,           # window modes: restore + bring to front first
    [switch]$ScreenCopy          # window modes: skip PrintWindow, copy pixels from screen
)

$ErrorActionPreference = 'Stop'
# Window titles are often non-ASCII; emit UTF-8 so callers (Bash, PowerShell, pipes) decode them correctly.
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$OutputEncoding = [Console]::OutputEncoding

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not ('ClaudeCap.Native' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeCap {
  public class WinInfo {
    public IntPtr Hwnd; public int Pid; public string Process; public string Title;
    public int X, Y, W, H; public bool Minimized;
  }

  public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int cb);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int cb);

    public static string InitDpi() {
      try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return "PerMonitorV2"; }
      catch (EntryPointNotFoundException) { /* pre-1703 Windows: fall through */ }
      return SetProcessDPIAware() ? "SystemAware" : "Unchanged";
    }

    // Visible bounds (no invisible resize borders) when DWM can tell us.
    public static RECT VisibleRect(IntPtr h) {
      RECT r;
      if (DwmGetWindowAttribute(h, 9 /*EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(RECT))) != 0)
        GetWindowRect(h, out r);
      return r;
    }

    static bool IsCloaked(IntPtr h) {
      int v;
      return DwmGetWindowAttribute(h, 14 /*CLOAKED*/, out v, 4) == 0 && v != 0;
    }

    public static List<WinInfo> ListWindows() {
      var list = new List<WinInfo>();
      var names = new Dictionary<uint, string>();
      EnumWindows(delegate (IntPtr h, IntPtr l) {
        if (!IsWindowVisible(h) || IsCloaked(h)) return true;
        if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;  // skip owned popups/tooltips
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        if (sb.Length == 0) return true;
        uint pid; GetWindowThreadProcessId(h, out pid);
        string pname;
        if (!names.TryGetValue(pid, out pname)) {
          try { pname = Process.GetProcessById((int)pid).ProcessName; }
          catch (ArgumentException) { pname = "<exited>"; }
          catch (InvalidOperationException) { pname = "<exited>"; }
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

    public static bool Exists(IntPtr h) { return IsWindow(h); }
    public static bool Minimized(IntPtr h) { return IsIconic(h); }

    public static void Activate(IntPtr h) {
      if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
      SetForegroundWindow(h);
    }

    // PrintWindow renders relative to GetWindowRect; crop to the DWM visible rect.
    public static Bitmap PrintWindowCapture(IntPtr h) {
      RECT wr; GetWindowRect(h, out wr);
      RECT vr = VisibleRect(h);
      int fw = wr.R - wr.L, fh = wr.B - wr.T;
      if (fw <= 0 || fh <= 0) return null;
      using (var full = new Bitmap(fw, fh, PixelFormat.Format32bppArgb)) {
        bool ok;
        using (var g = Graphics.FromImage(full)) {
          IntPtr hdc = g.GetHdc();
          ok = PrintWindow(h, hdc, 2 /*PW_RENDERFULLCONTENT*/);
          g.ReleaseHdc(hdc);
        }
        if (!ok) return null;
        var crop = Rectangle.Intersect(new Rectangle(0, 0, fw, fh),
          new Rectangle(vr.L - wr.L, vr.T - wr.T, vr.R - vr.L, vr.B - vr.T));
        if (crop.Width <= 0 || crop.Height <= 0) crop = new Rectangle(0, 0, fw, fh);
        return full.Clone(crop, PixelFormat.Format32bppArgb);
      }
    }

    public static Bitmap ScreenCapture(int x, int y, int w, int h) {
      var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
      return bmp;
    }

    // True when ~all sampled pixels are black/transparent (typical PrintWindow failure on GPU surfaces).
    public static bool LooksBlank(Bitmap b) {
      int total = 0, dark = 0;
      int sx = Math.Max(1, b.Width / 40), sy = Math.Max(1, b.Height / 40);
      for (int y = 0; y < b.Height; y += sy)
        for (int x = 0; x < b.Width; x += sx) {
          Color c = b.GetPixel(x, y); total++;
          if (c.A == 0 || (c.R < 4 && c.G < 4 && c.B < 4)) dark++;
        }
      return total > 0 && dark >= total * 0.995;
    }

    public static Bitmap Downscale(Bitmap src, int maxEdge) {
      int edge = Math.Max(src.Width, src.Height);
      if (maxEdge <= 0 || edge <= maxEdge) return null;
      double s = (double)maxEdge / edge;
      int w = Math.Max(1, (int)Math.Round(src.Width * s)), h = Math.Max(1, (int)Math.Round(src.Height * s));
      var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(dst)) {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
      }
      return dst;
    }
  }
}
'@
}

function Fail([int]$code, [string]$msg) {
    [Console]::Error.WriteLine("ERROR: $msg")
    exit $code
}

$dpiMode = [ClaudeCap.Native]::InitDpi()

function Get-Monitors {
    $i = 0
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
        [pscustomobject]@{
            Index = $i++; Primary = $s.Primary; Device = $s.DeviceName
            X = $s.Bounds.X; Y = $s.Bounds.Y; W = $s.Bounds.Width; H = $s.Bounds.Height
        }
    }
}

if ($List) {
    Write-Output "DPI awareness: $dpiMode"
    Write-Output "`n== Monitors =="
    Get-Monitors | Format-Table -AutoSize | Out-String -Width 300 | Write-Output
    Write-Output "== Windows (visible, titled, top-level) =="
    [ClaudeCap.Native]::ListWindows() | ForEach-Object {
        [pscustomobject]@{
            Hwnd = ('0x{0:X}' -f $_.Hwnd.ToInt64()); Pid = $_.Pid; Process = $_.Process
            Size = $(if ($_.Minimized) { 'minimized' } else { "$($_.W)x$($_.H)" })
            Pos = "$($_.X),$($_.Y)"; Title = $_.Title
        }
    } | Format-Table -AutoSize | Out-String -Width 300 | Write-Output
    exit 0
}

# ---- resolve target window (window modes) ----
function Find-TargetWindow {
    $wins = [ClaudeCap.Native]::ListWindows()
    $c = switch ($PSCmdlet.ParameterSetName) {
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
    }
    $c = @($c)
    if ($c.Count -gt 1) {
        [Console]::Error.WriteLine("NOTE: $($c.Count) windows matched; picking the largest non-minimized one. Candidates:")
        $c | ForEach-Object { [Console]::Error.WriteLine(("  0x{0:X}  {1}  [{2}]  {3}x{4}" -f $_.Hwnd.ToInt64(), $_.Process, $_.Title, $_.W, $_.H)) }
    }
    $c | Sort-Object @{ e = { -not $_.Minimized }; Descending = $true }, @{ e = { $_.W * $_.H }; Descending = $true } |
        Select-Object -First 1
}

$isWindowMode = $PSCmdlet.ParameterSetName -in 'Window', 'Pid', 'Hwnd'
$target = $null
if ($isWindowMode) {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    do {
        $target = Find-TargetWindow
        if ($target) { break }
        if ($WaitSeconds -gt 0) { Start-Sleep -Milliseconds 300 }
    } while ((Get-Date) -lt $deadline)
    if (-not $target) {
        Fail 1 "No visible window matched ($($PSCmdlet.ParameterSetName)). Run with -List to see candidates."
    }
    if ($Activate) {
        [ClaudeCap.Native]::Activate($target.Hwnd)
        Start-Sleep -Milliseconds 250
    } elseif ($target.Minimized) {
        Fail 1 ("Window 0x{0:X} '{1}' is minimized; re-run with -Activate to restore it." -f $target.Hwnd.ToInt64(), $target.Title)
    }
}

if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }

# ---- capture ----
$bmp = $null; $method = ''; $source = ''
switch ($PSCmdlet.ParameterSetName) {
    { $_ -in 'Window', 'Pid', 'Hwnd' } {
        $h = $target.Hwnd
        $source = ("window 0x{0:X} {1} '{2}'" -f $h.ToInt64(), $target.Process, $target.Title)
        if (-not $ScreenCopy) {
            $bmp = [ClaudeCap.Native]::PrintWindowCapture($h)
            if ($bmp -and [ClaudeCap.Native]::LooksBlank($bmp)) {
                [Console]::Error.WriteLine("NOTE: PrintWindow returned a blank image; falling back to screen copy.")
                $bmp.Dispose(); $bmp = $null
            }
            if ($bmp) { $method = 'PrintWindow' }
        }
        if (-not $bmp) {
            $r = [ClaudeCap.Native]::VisibleRect($h)
            $bmp = [ClaudeCap.Native]::ScreenCapture($r.L, $r.T, $r.R - $r.L, $r.B - $r.T)
            $method = 'ScreenCopy'
            if (-not $Activate) {
                [Console]::Error.WriteLine("NOTE: ScreenCopy shows whatever is on top of that area; add -Activate if other windows overlap it.")
            }
        }
    }
    'Region' {
        $rv = @($Region -split '[,\s]+' | Where-Object { $_ -ne '' } | ForEach-Object { [int]$_ })
        if ($rv.Count -ne 4 -or $rv[2] -le 0 -or $rv[3] -le 0) { Fail 1 "-Region needs `"x,y,w,h`" with w,h > 0 (got '$Region')" }
        $bmp = [ClaudeCap.Native]::ScreenCapture($rv[0], $rv[1], $rv[2], $rv[3])
        $method = 'ScreenCopy'; $source = "region $($rv -join ',')"
    }
    'All' {
        $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = [ClaudeCap.Native]::ScreenCapture($v.X, $v.Y, $v.Width, $v.Height)
        $method = 'ScreenCopy'; $source = "virtual desktop $($v.Width)x$($v.Height) at $($v.X),$($v.Y)"
    }
    default {
        $mons = @(Get-Monitors)
        $m = if ($Monitor -ge 0) { $mons | Where-Object Index -eq $Monitor } else { $mons | Where-Object Primary | Select-Object -First 1 }
        if (-not $m) { Fail 1 "Monitor index $Monitor not found (have 0..$($mons.Count - 1))." }
        $bmp = [ClaudeCap.Native]::ScreenCapture($m.X, $m.Y, $m.W, $m.H)
        $method = 'ScreenCopy'; $source = "monitor $($m.Index) $($m.Device)$(if ($m.Primary) { ' (primary)' })"
    }
}
if (-not $bmp) { Fail 2 "Capture produced no image." }

$native = "$($bmp.Width)x$($bmp.Height)"
$small = [ClaudeCap.Native]::Downscale($bmp, $MaxSize)
if ($small) { $bmp.Dispose(); $bmp = $small }

if (-not $Out) {
    $dir = Join-Path $env:TEMP 'claude-captures'
    $Out = Join-Path $dir ("cap_{0:yyyyMMdd_HHmmss_fff}.png" -f (Get-Date))
}
$Out = [System.IO.Path]::GetFullPath($Out)
$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$fmt = switch ([System.IO.Path]::GetExtension($Out).ToLower()) {
    '.jpg'  { [System.Drawing.Imaging.ImageFormat]::Jpeg }
    '.jpeg' { [System.Drawing.Imaging.ImageFormat]::Jpeg }
    '.bmp'  { [System.Drawing.Imaging.ImageFormat]::Bmp }
    default { [System.Drawing.Imaging.ImageFormat]::Png }
}
$bmp.Save($Out, $fmt)
$final = "$($bmp.Width)x$($bmp.Height)"
$bmp.Dispose()

$scaleNote = if ($final -ne $native) { "$final (native $native)" } else { $final }
Write-Output "CAPTURED: $Out | $scaleNote | $method | $source"
exit 0
