<#
.SYNOPSIS
  Read and operate one Windows app window through UI Automation, for AI agents: buttons, fields,
  checkboxes, tabs, lists, dropdowns, scroll areas. No mouse movement, no focus change, works on
  windows behind other windows. Same window lookup as screen-capture / screen-input. No build step.

.EXAMPLES
  ui.ps1 -Window notepad                                  # snapshot: every actionable element with a ref
  ui.ps1 -Window MyApp -Find 저장                          # only elements whose name/value/id contains 저장
  ui.ps1 -Window MyApp -Do "click e12"                    # act on a ref from the snapshot
  ui.ps1 -Window MyApp -Do "set e5 안녕하세요; click 보내기" -Snap   # name targets work too; -Snap re-snapshots
  ui.ps1 -Window MyApp -Do "toggle e7 on; select e9 '옵션 B'; waitfor 완료 5000"

  Steps (separated by ';'; '\;' is a literal semicolon):
    click T | set T TEXT | toggle T [on|off] | check T | uncheck T | select T [OPTION]
    expand T | collapse T | scroll T up|down|left|right|pageup|pagedown|top|bottom [N] | scrollto T
    focus T | get T | wait MS | waitfor TEXT [MS] | waitgone TEXT [MS]
  T (target) = eN (ref from a snapshot) | #automationId | name | role:name   (quote names with spaces
  when an argument follows: set "검색어" hello)
  Exit codes: 0 ok, 1 bad args / no window, 2 target not found / stale / ambiguous, 3 action failed.
#>
[CmdletBinding(DefaultParameterSetName = 'Window')]
param(
    [Parameter(ParameterSetName = 'Window', Mandatory)] [string]$Window,
    [Parameter(ParameterSetName = 'Pid', Mandatory)]    [int]$ProcessId,
    [Parameter(ParameterSetName = 'Hwnd', Mandatory)]   [string]$Hwnd,
    [string]$Do,                 # steps; without it, take a snapshot
    [string]$Find,               # snapshot filter: name / value / automation id / role
    [switch]$Text,               # snapshot also lists static text (read what the window says)
    [switch]$Snap,               # after -Do, take a snapshot
    [switch]$All,                # snapshot also lists offscreen elements (scrolled out of view)
    [switch]$Restore,            # if the window is minimized, restore it (without taking focus)
    [int]$TimeoutMs = 6000,      # budget for a web/Chromium tree to finish building
    [int]$StepDelayMs = 150      # pause between steps so the app can react
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$OutputEncoding = [Console]::OutputEncoding

function Fail([int]$code, [string]$msg) {
    [Console]::Error.WriteLine("ERROR: $msg")
    exit $code
}

# ---- load the C# core, compiled once per version into %TEMP% (Add-Type is slow; the dll is not) ----
if (-not ('ScreenUi.Session' -as [type])) {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
    $src = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ui.cs'))
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($src))[0..5] | ForEach-Object { $_.ToString('x2') })
    $dir = Join-Path ([IO.Path]::GetTempPath()) 'claude-screen-ui'
    [void][IO.Directory]::CreateDirectory($dir)
    $dll = Join-Path $dir "ScreenUi-$hash.dll"
    if (-not (Test-Path $dll)) {
        $tmp = "$dll.$PID.tmp"
        $refs = @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase', 'System.Core', 'System.Xaml') | ForEach-Object {
            ([System.Reflection.Assembly]::LoadWithPartialName($_)).Location
        }
        try {
            Add-Type -TypeDefinition $src -ReferencedAssemblies $refs -OutputAssembly $tmp -OutputType Library
            Move-Item -LiteralPath $tmp -Destination $dll -Force -ErrorAction SilentlyContinue
        } catch { Fail 1 "compiling ui.cs failed: $($_.Exception.Message)" }
        if (-not (Test-Path $dll)) { $dll = $tmp }
    }
    if (-not ('ScreenUi.Session' -as [type])) { Add-Type -Path $dll }
}

[ScreenUi.Native]::InitDpi()

# ---- target window (same rules as screen-input) ----
$wins = [ScreenUi.Native]::ListWindows()
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
if ($t.Minimized -and $Restore) {
    [ScreenUi.Native]::RestoreNoActivate($t.Hwnd)
    Start-Sleep -Milliseconds 400
}

$s = New-Object ScreenUi.Session($t, $TimeoutMs, $StepDelayMs)
$code = $s.Run($Do, [bool]$Snap, $Find, [bool]$Text, [bool]$All)
if ($s.Out.Length) { [Console]::Out.Write($s.Out.ToString()) }
if ($s.Err.Length) { [Console]::Error.WriteLine($s.Err.ToString()) }
exit $code
