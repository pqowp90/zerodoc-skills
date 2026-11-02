// screen-ui core: reads a window's UI Automation tree and acts on its elements through UIA
// patterns (Invoke, Value, Toggle, SelectionItem, ExpandCollapse, Scroll). Nothing here moves
// the mouse or takes focus, except the explicit `focus` step.
// Compiled by ui.ps1 with Add-Type (Windows PowerShell 5.1 => C# 5: no $"", no ?., no out var).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace ScreenUi {
  public class Win {
    public IntPtr Hwnd; public int Pid; public string Process; public string Title;
    public int X, Y, W, H; public bool Minimized;
  }

  public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
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

    // Same rules as screen-capture / screen-input, so the same -Window picks the same window.
    public static List<Win> ListWindows() {
      var list = new List<Win>();
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
        list.Add(new Win { Hwnd = h, Pid = (int)pid, Process = pname, Title = sb.ToString(),
          X = r.L, Y = r.T, W = r.R - r.L, H = r.B - r.T, Minimized = min });
        return true;
      }, IntPtr.Zero);
      return list;
    }

    // SW_SHOWNOACTIVATE: back to its last size and place without taking the foreground.
    public static void RestoreNoActivate(IntPtr h) { ShowWindow(h, 4); }
  }

  public class UiError : Exception {
    public int Code;
    public UiError(int code, string msg) : base(msg) { Code = code; }
  }

  public class Node {
    public AutomationElement El; public Node Parent; public List<Node> Kids = new List<Node>();
    public string Rid = "", Role = "", Name = "", Aid = "", Cls = "", Help = "";
    public string Label = "";   // Name, or for an unnamed ref the text inside it
    public bool Derived;        // Label came from the text inside
    public string Value, Toggle, Expand, Range;
    public Rect Rect = Rect.Empty;
    public bool Enabled = true, Offscreen, Password, Focused, Selected;
    public bool HasInvoke, HasToggle, HasValue, ValueRO, HasSel, HasExpand, HasScroll, HasScrollItem, CanScroll;
    public double VScroll = -1;
    public bool RefAble;
    public string Ref;
  }

  class Entry { public string Ref, Rid, Role, Name, Aid, Rect; }

  class Step { public string Verb, Target, Arg, Raw; public int Ms; }

  public class Session {
    public readonly StringBuilder Out = new StringBuilder();
    public readonly StringBuilder Err = new StringBuilder();
    readonly Win W;
    readonly int TimeoutMs, StepDelayMs;
    readonly string MapPath;
    Dictionary<string, Entry> refs = new Dictionary<string, Entry>();
    Dictionary<string, string> ridToRef = new Dictionary<string, string>();
    int next = 1;
    Node Root;
    List<Node> All = new List<Node>();
    bool settled;

    public Session(Win w, int timeoutMs, int stepDelayMs) {
      W = w; TimeoutMs = timeoutMs; StepDelayMs = stepDelayMs;
      string dir = Path.Combine(Path.GetTempPath(), "claude-screen-ui");
      Directory.CreateDirectory(dir);
      MapPath = Path.Combine(dir, "refs-" + w.Hwnd.ToInt64().ToString("X") + ".txt");
      LoadMap();
    }

    public string Desc { get { return string.Format("window 0x{0:X} {1} '{2}'", W.Hwnd.ToInt64(), W.Process, W.Title); } }

    // ---------------------------------------------------------------- entry points

    bool showAll;

    public int Run(string doText, bool snap, string find, bool text, bool all) {
      showAll = all;
      List<Step> steps = null;
      if (!string.IsNullOrEmpty(doText)) {
        try { steps = Parse(doText); }
        catch (UiError e) { Err.Append("ERROR: -Do: ").Append(e.Message); return 1; }
        if (steps.Count == 0) { Err.Append("ERROR: -Do has no steps"); return 1; }
      }
      try {
        if (steps == null) { Snapshot(find, text); return 0; }
        int done = 0;
        try {
          foreach (var st in steps) {
            string r = Exec(st);
            Out.Append(done + 1).Append(". ").Append(r).Append('\n');
            done++;
            if (StepDelayMs > 0 && done < steps.Count) Thread.Sleep(StepDelayMs);
          }
        } catch (UiError e) {
          Err.Append("ERROR: step ").Append(done + 1).Append('/').Append(steps.Count)
             .Append(" (").Append(steps[done].Raw).Append("): ").Append(e.Message)
             .Append(done > 0 ? "\nSteps 1.." + done + " were done." : "\nNothing was done.");
          return e.Code;
        }
        Out.Append("UI: ").Append(done).Append(" steps | ").Append(Desc).Append('\n');
        if (snap) { Thread.Sleep(250); settled = false; Snapshot(find, text); }
        return 0;
      } catch (UiError e) {
        Err.Append("ERROR: ").Append(e.Message); return e.Code;
      } catch (ElementNotAvailableException) {
        Err.Append("ERROR: the window or element went away while reading it. Run again."); return 2;
      }
    }

    // ---------------------------------------------------------------- reading the tree

    static readonly AutomationProperty[] Props = {
      AutomationElement.NameProperty, AutomationElement.ControlTypeProperty, AutomationElement.AutomationIdProperty,
      AutomationElement.ClassNameProperty, AutomationElement.RuntimeIdProperty, AutomationElement.BoundingRectangleProperty,
      AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty, AutomationElement.IsPasswordProperty,
      AutomationElement.HasKeyboardFocusProperty, AutomationElement.HelpTextProperty,
      AutomationElement.IsInvokePatternAvailableProperty, AutomationElement.IsTogglePatternAvailableProperty,
      AutomationElement.IsValuePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
      AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsScrollPatternAvailableProperty,
      AutomationElement.IsScrollItemPatternAvailableProperty, AutomationElement.IsRangeValuePatternAvailableProperty,
      ValuePattern.ValueProperty, ValuePattern.IsReadOnlyProperty, TogglePattern.ToggleStateProperty,
      SelectionItemPattern.IsSelectedProperty, ExpandCollapsePattern.ExpandCollapseStateProperty,
      ScrollPattern.VerticallyScrollableProperty, ScrollPattern.HorizontallyScrollableProperty,
      ScrollPattern.VerticalScrollPercentProperty, RangeValuePattern.ValueProperty,
    };

    static object C(AutomationElement e, AutomationProperty p) {
      try { object v = e.GetCachedPropertyValue(p, true); return v == AutomationElement.NotSupported ? null : v; }
      catch { return null; }
    }
    static bool B(AutomationElement e, AutomationProperty p) { object v = C(e, p); return v is bool && (bool)v; }
    static string S(AutomationElement e, AutomationProperty p) { object v = C(e, p); return v == null ? "" : v.ToString(); }

    // One cross-process call for the whole subtree (control view), then plain object walks.
    void ReadTree() {
      if (!Native.IsWindow(W.Hwnd)) throw new UiError(1, "the window is gone (" + Desc + ")");
      if (Native.IsIconic(W.Hwnd))
        throw new UiError(1, "the window is minimized; UI Automation reports everything offscreen. Pass -Restore (restores it without taking focus).");
      var cr = new CacheRequest();
      foreach (var p in Props) cr.Add(p);
      cr.TreeScope = TreeScope.Subtree;
      cr.TreeFilter = Automation.ControlViewCondition;
      AutomationElement root;
      using (cr.Activate()) { root = AutomationElement.FromHandle(W.Hwnd); }
      All = new List<Node>();
      Root = Build(root, null);
      foreach (var n in All) {
        n.Label = n.Name;
        if (n.Name == "" && n.RefAble && (n.HasInvoke || n.HasSel || n.HasToggle || n.HasExpand)) {
          var t = Under(n).Where(x => x.Name != "" && x.Role == "text").Select(x => x.Name).Take(4).ToList();
          if (t.Count > 0) { n.Label = Clip(string.Join(" · ", t), 80); n.Derived = true; }
        }
      }
      AssignRefs();
    }

    Node Build(AutomationElement e, Node parent) {
      var n = new Node { El = e, Parent = parent };
      n.Name = Clean(S(e, AutomationElement.NameProperty));
      var ct = C(e, AutomationElement.ControlTypeProperty) as ControlType;
      n.Role = RoleOf(ct);
      n.Aid = S(e, AutomationElement.AutomationIdProperty);
      n.Cls = S(e, AutomationElement.ClassNameProperty);
      var rid = C(e, AutomationElement.RuntimeIdProperty) as int[];
      n.Rid = rid == null ? "" : string.Join(".", rid);
      object r = C(e, AutomationElement.BoundingRectangleProperty);
      if (r is Rect) n.Rect = (Rect)r;
      object en = C(e, AutomationElement.IsEnabledProperty);
      n.Enabled = !(en is bool) || (bool)en;
      n.Offscreen = B(e, AutomationElement.IsOffscreenProperty);
      n.Password = B(e, AutomationElement.IsPasswordProperty);
      n.Focused = B(e, AutomationElement.HasKeyboardFocusProperty);
      n.Help = Clean(S(e, AutomationElement.HelpTextProperty));
      n.HasInvoke = B(e, AutomationElement.IsInvokePatternAvailableProperty);
      n.HasToggle = B(e, AutomationElement.IsTogglePatternAvailableProperty);
      n.HasValue = B(e, AutomationElement.IsValuePatternAvailableProperty);
      n.HasSel = B(e, AutomationElement.IsSelectionItemPatternAvailableProperty);
      n.HasExpand = B(e, AutomationElement.IsExpandCollapsePatternAvailableProperty);
      n.HasScroll = B(e, AutomationElement.IsScrollPatternAvailableProperty);
      n.HasScrollItem = B(e, AutomationElement.IsScrollItemPatternAvailableProperty);
      if (n.HasValue) {
        n.Value = S(e, ValuePattern.ValueProperty);
        n.ValueRO = B(e, ValuePattern.IsReadOnlyProperty);
      }
      if (n.HasToggle) {
        object t = C(e, TogglePattern.ToggleStateProperty);
        if (t is ToggleState) n.Toggle = (ToggleState)t == ToggleState.On ? "on" : (ToggleState)t == ToggleState.Off ? "off" : "mixed";
      }
      if (n.HasSel) n.Selected = B(e, SelectionItemPattern.IsSelectedProperty);
      if (n.HasExpand) {
        object x = C(e, ExpandCollapsePattern.ExpandCollapseStateProperty);
        if (x is ExpandCollapseState) {
          var xs = (ExpandCollapseState)x;
          n.Expand = xs == ExpandCollapseState.Expanded ? "expanded" : xs == ExpandCollapseState.Collapsed ? "collapsed"
                   : xs == ExpandCollapseState.PartiallyExpanded ? "partly expanded" : null;
        }
      }
      if (n.HasScroll) {
        n.CanScroll = B(e, ScrollPattern.VerticallyScrollableProperty) || B(e, ScrollPattern.HorizontallyScrollableProperty);
        object v = C(e, ScrollPattern.VerticalScrollPercentProperty);
        if (v is double) n.VScroll = (double)v;
      }
      if (B(e, AutomationElement.IsRangeValuePatternAvailableProperty)) {
        object v = C(e, RangeValuePattern.ValueProperty);
        if (v is double) n.Range = "value " + ((double)v).ToString("0.##");
      }
      n.RefAble = parent != null && IsRefAble(n);
      All.Add(n);
      AutomationElementCollection kids = null;
      try { kids = e.CachedChildren; } catch (InvalidOperationException) { }
      if (kids != null) foreach (AutomationElement k in kids) n.Kids.Add(Build(k, n));
      return n;
    }

    static readonly HashSet<string> Interactive = new HashSet<string> {
      "button", "checkbox", "combobox", "textfield", "link", "menuitem", "radio", "slider", "spinner", "tab",
    };
    static readonly HashSet<string> NoRef = new HashSet<string> { "scrollbar", "thumb", "titlebar", "separator", "tooltip" };

    // An element gets a ref when its role is interactive or it advertises an action pattern.
    // ScrollItem/focus alone don't count: web runtimes put them on nearly every node.
    static bool IsRefAble(Node n) {
      if (NoRef.Contains(n.Role)) return false;
      for (var p = n.Parent; p != null; p = p.Parent) if (p.Role == "scrollbar") return false;
      return Interactive.Contains(n.Role) || n.HasInvoke || n.HasToggle || n.HasSel || n.HasExpand
          || (n.HasValue && !n.ValueRO) || n.CanScroll;
    }

    static string RoleOf(ControlType ct) {
      if (ct == null) return "unknown";
      if (ct == ControlType.Button || ct == ControlType.SplitButton) return "button";
      if (ct == ControlType.CheckBox) return "checkbox";
      if (ct == ControlType.ComboBox) return "combobox";
      if (ct == ControlType.Edit) return "textfield";
      if (ct == ControlType.Hyperlink) return "link";
      if (ct == ControlType.ListItem) return "listitem";
      if (ct == ControlType.MenuItem) return "menuitem";
      if (ct == ControlType.RadioButton) return "radio";
      if (ct == ControlType.Spinner) return "spinner";
      if (ct == ControlType.TabItem) return "tab";
      if (ct == ControlType.Tab) return "tablist";
      if (ct == ControlType.DataItem) return "cell";
      if (ct == ControlType.DataGrid) return "grid";
      if (ct == ControlType.ToolBar) return "toolbar";
      if (ct == ControlType.MenuBar) return "menubar";
      if (ct == ControlType.StatusBar) return "statusbar";
      if (ct == ControlType.TitleBar) return "titlebar";
      if (ct == ControlType.ScrollBar) return "scrollbar";
      if (ct == ControlType.ProgressBar) return "progressbar";
      if (ct == ControlType.HeaderItem) return "headeritem";
      if (ct == ControlType.ToolTip) return "tooltip";
      string s = ct.ProgrammaticName;
      int dot = s.LastIndexOf('.');
      return (dot >= 0 ? s.Substring(dot + 1) : s).ToLowerInvariant();
    }

    // Chromium/WebView2 builds its tree after the first UIA client connects, so the first read is
    // a thin shell. Re-read until the node count stops changing (or the budget runs out).
    void ReadSettled() {
      ReadTree();
      if (settled) return;
      var sw = Stopwatch.StartNew();
      int prev = All.Count, same = 0;
      while (sw.ElapsedMilliseconds < TimeoutMs) {
        Thread.Sleep(prev < 60 ? 400 : 250);
        ReadTree();
        if (All.Count == prev) { if (++same >= 1) break; } else { same = 0; prev = All.Count; }
      }
      settled = true;
    }

    // ---------------------------------------------------------------- refs

    // A ref always means the same element (UIA RuntimeId): reused across snapshots, never recycled.
    // A ref whose element is gone is reported stale instead of silently pointing at something else.
    void AssignRefs() {
      foreach (var n in All) {
        if (!n.RefAble || n.Rid == "") continue;
        string r;
        if (!ridToRef.TryGetValue(n.Rid, out r)) {
          r = "e" + next++;
          ridToRef[n.Rid] = r;
        }
        n.Ref = r;
        refs[r] = new Entry { Ref = r, Rid = n.Rid, Role = n.Role, Name = n.Label, Aid = n.Aid, Rect = RectKey(n.Rect) };
      }
      SaveMap();
    }

    static string RectKey(Rect r) {
      return r.IsEmpty ? "" : string.Format("{0},{1},{2},{3}", (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }

    void LoadMap() {
      try {
        if (!File.Exists(MapPath)) return;
        var lines = File.ReadAllLines(MapPath, Encoding.UTF8);
        if (lines.Length == 0) return;
        var head = lines[0].Split('\t');
        if (head.Length < 3 || head[0] != "v1" || head[1] != W.Pid.ToString()) return; // hwnd reused by another process
        next = int.Parse(head[2]);
        for (int i = 1; i < lines.Length; i++) {
          var f = lines[i].Split('\t');
          if (f.Length < 6) continue;
          var e = new Entry { Ref = f[0], Rid = f[1], Role = f[2], Aid = f[3], Rect = f[4], Name = f[5] };
          refs[e.Ref] = e; ridToRef[e.Rid] = e.Ref;
        }
      } catch { refs.Clear(); ridToRef.Clear(); next = 1; }
    }

    void SaveMap() {
      // Keep only refs whose element is present now (plus nothing else): stale refs fail loudly.
      var live = new HashSet<string>(All.Where(n => n.Ref != null).Select(n => n.Ref));
      var sb = new StringBuilder();
      sb.Append("v1\t").Append(W.Pid).Append('\t').Append(next).Append('\n');
      foreach (var e in refs.Values) {
        if (!live.Contains(e.Ref)) continue;
        sb.Append(e.Ref).Append('\t').Append(e.Rid).Append('\t').Append(e.Role).Append('\t')
          .Append(Flat(e.Aid)).Append('\t').Append(e.Rect).Append('\t').Append(Flat(e.Name)).Append('\n');
      }
      // Forget dead refs so they can't resolve, but keep `next` so numbers are never reused.
      var dead = refs.Keys.Where(k => !live.Contains(k)).ToList();
      foreach (var k in dead) { ridToRef.Remove(refs[k].Rid); refs.Remove(k); }
      string tmp = MapPath + "." + Process.GetCurrentProcess().Id;
      File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
      try { if (File.Exists(MapPath)) File.Delete(MapPath); File.Move(tmp, MapPath); } catch { try { File.Delete(tmp); } catch { } }
    }

    // ---------------------------------------------------------------- snapshot

    static readonly HashSet<string> Containers = new HashSet<string> {
      "group", "pane", "list", "tablist", "tree", "menu", "menubar", "toolbar", "table", "grid",
      "window", "document", "statusbar", "header", "custom", "dataitem",
    };

    void Snapshot(string find, bool text) {
      ReadSettled();
      Func<Node, bool> match = n => string.IsNullOrEmpty(find)
        || Has(n.Label, find) || Has(n.Value, find) || Has(n.Aid, find) || n.Role.Equals(find, StringComparison.OrdinalIgnoreCase);
      bool all = showAll || !string.IsNullOrEmpty(find);
      hidden = all ? 0 : All.Count(n => n.Ref != null && n.Offscreen);
      Func<Node, bool> keep = n => (all || !n.Offscreen) && match(n);
      var lines = Print(Root, 0, keep, text);
      int nrefs = All.Count(n => n.Ref != null);
      Out.Append("SNAPSHOT | ").Append(Desc).Append(" | ").Append(nrefs).Append(" refs, ").Append(All.Count).Append(" nodes");
      if (!string.IsNullOrEmpty(find)) Out.Append(" | find '").Append(find).Append("'");
      Out.Append('\n');
      foreach (var l in lines) Out.Append(l).Append('\n');
      if (hidden > 0) Out.Append("(+").Append(hidden).Append(" offscreen refs not shown: scroll, or pass -All)\n");
      if (lines.Count == 0) Out.Append(string.IsNullOrEmpty(find) ? "(nothing actionable)\n" : "(no match)\n");
      if (All.Count < 40)
        Out.Append("NOTE: the tree is small; a web/Chromium app may still be building it. Run again or raise -TimeoutMs.\n");
    }

    int hidden;

    List<string> Print(Node n, int indent, Func<Node, bool> keep, bool text) {
      var lines = new List<string>();
      bool self = n != Root && ((n.Ref != null && keep(n)) || (text && IsTextLine(n) && keep(n)));
      bool header = n != Root && !self && n.Ref == null && n.Name != "" && Containers.Contains(n.Role);
      int ci = (self || header) ? indent + 1 : indent;
      var kids = new List<string>();
      foreach (var k in n.Kids) kids.AddRange(Print(k, ci, keep, text));
      if (self) lines.Add(Ind(indent) + Line(n, true));
      else if (header && kids.Count > 0) lines.Add(Ind(indent) + n.Role + " \"" + Clip(n.Name, 60) + "\":");
      if (header && kids.Count == 0) return lines;
      lines.AddRange(kids);
      return lines;
    }

    // Static text worth showing with -Text: named, and not just repeating an ancestor's label.
    static bool IsTextLine(Node n) {
      if (n.Ref != null || n.Name == "" || !(n.Role == "text" || n.Role == "image" || n.Role == "progressbar")) return false;
      int hops = 0;
      for (var p = n.Parent; p != null && hops < 3; p = p.Parent, hops++)
        if (p.Name != "" && p.Name.IndexOf(n.Name, StringComparison.Ordinal) >= 0) return false;
      return true;
    }

    string Line(Node n, bool withAid) {
      var sb = new StringBuilder();
      if (n.Ref != null) sb.Append(n.Ref).Append(' ');
      sb.Append(n.Role);
      if (n.Name != "") sb.Append(" \"").Append(Clip(n.Name, 80)).Append('"');
      else if (n.Derived) sb.Append(" (").Append(n.Label).Append(')');
      if (!string.IsNullOrEmpty(n.Value) && n.Value != n.Name)
        sb.Append(" = \"").Append(n.Password ? "***" : Clip(Clean(n.Value), 80)).Append('"');
      var st = new List<string>();
      if (withAid && n.Aid != "" && n.Aid.Length <= 40 && n.Aid.IndexOf(' ') < 0 && !Regex.IsMatch(n.Aid, @"^\d+$")) st.Add("#" + n.Aid);
      if (!n.Enabled) st.Add("disabled");
      if (n.Toggle != null) st.Add(n.Toggle);
      if (n.Selected) st.Add("selected");
      if (n.Expand != null) st.Add(n.Expand);
      if (n.CanScroll) st.Add(n.VScroll >= 0 ? "scroll " + (int)Math.Round(n.VScroll) + "%" : "scrollable");
      if (n.Range != null) st.Add(n.Range);
      if (n.HasValue && n.ValueRO && Interactive.Contains(n.Role) && n.Role == "textfield") st.Add("readonly");
      if (n.Focused) st.Add("focused");
      if (n.Offscreen) st.Add("offscreen");
      if (st.Count > 0) sb.Append(" [").Append(string.Join(", ", st)).Append(']');
      return sb.ToString();
    }

    // ---------------------------------------------------------------- steps

    static readonly HashSet<string> Verbs = new HashSet<string> {
      "click", "set", "toggle", "check", "uncheck", "select", "expand", "collapse",
      "scroll", "scrollto", "focus", "get", "wait", "waitfor", "waitgone",
    };

    static List<Step> Parse(string text) {
      var steps = new List<Step>();
      // ';' separates steps; '\;' is a literal semicolon inside a value.
      foreach (var raw0 in Regex.Split(text, @"(?<!\\);")) {
        string raw = raw0.Replace("\\;", ";").Trim();
        if (raw == "") continue;
        var m = Regex.Match(raw, @"^(\S+)\s*(.*)$", RegexOptions.Singleline);
        var st = new Step { Verb = m.Groups[1].Value.ToLowerInvariant(), Raw = raw };
        string rest = m.Groups[2].Value.Trim();
        if (!Verbs.Contains(st.Verb)) throw new UiError(1, "unknown step '" + st.Verb + "'");
        switch (st.Verb) {
          case "wait": {
            int ms; if (!int.TryParse(rest, out ms)) throw new UiError(1, "wait needs milliseconds");
            st.Ms = ms; break;
          }
          case "waitfor": case "waitgone": {
            var mm = Regex.Match(rest, @"^(.*?)\s+(\d+)$", RegexOptions.Singleline);
            st.Arg = Unquote(mm.Success ? mm.Groups[1].Value : rest);
            st.Ms = mm.Success ? int.Parse(mm.Groups[2].Value) : 10000;
            if (st.Arg == "") throw new UiError(1, st.Verb + " needs text");
            break;
          }
          case "set": case "select": {
            string tail; st.Target = TakeTarget(rest, out tail);
            st.Arg = tail == "" ? null : Unquote(tail).Replace("\\n", "\n").Replace("\\t", "\t");
            if (st.Verb == "set" && st.Arg == null) st.Arg = "";
            break;
          }
          case "toggle": {
            var mm = Regex.Match(rest, @"^(.*?)\s+(on|off)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            st.Target = Unquote(mm.Success ? mm.Groups[1].Value : rest);
            st.Arg = mm.Success ? mm.Groups[2].Value.ToLowerInvariant() : null;
            break;
          }
          case "check": st.Verb = "toggle"; st.Arg = "on"; st.Target = Unquote(rest); break;
          case "uncheck": st.Verb = "toggle"; st.Arg = "off"; st.Target = Unquote(rest); break;
          case "scroll": {
            var mm = Regex.Match(rest, @"^(.*?)\s+(up|down|left|right|top|bottom|pageup|pagedown)(?:\s+(\d+))?$",
                                 RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!mm.Success) throw new UiError(1, "scroll needs: scroll TARGET up|down|left|right|pageup|pagedown|top|bottom [N]");
            st.Target = Unquote(mm.Groups[1].Value);
            st.Arg = mm.Groups[2].Value.ToLowerInvariant();
            st.Ms = mm.Groups[3].Success ? int.Parse(mm.Groups[3].Value) : 3;
            break;
          }
          default: st.Target = Unquote(rest); break;
        }
        if (st.Verb != "wait" && st.Verb != "waitfor" && st.Verb != "waitgone" && string.IsNullOrEmpty(st.Target))
          throw new UiError(1, st.Verb + " needs a target (eN, #automationId, or a name)");
        steps.Add(st);
      }
      return steps;
    }

    // First token of `rest` as a target: a quoted name, eN, #id, or role:"name"; the remainder is returned.
    static string TakeTarget(string rest, out string tail) {
      var m = Regex.Match(rest, @"^((?:[a-z]+:)?(?:""[^""]*""|'[^']*'|\S+))\s*(.*)$", RegexOptions.Singleline);
      if (!m.Success) { tail = ""; return Unquote(rest); }
      tail = m.Groups[2].Value;
      return Unquote(m.Groups[1].Value);
    }

    static string Unquote(string s) {
      s = s.Trim();
      var m = Regex.Match(s, @"^([a-z]+:)?([""'])(.*)\2$", RegexOptions.Singleline);
      return m.Success ? m.Groups[1].Value + m.Groups[3].Value : s;
    }

    string Exec(Step st) {
      switch (st.Verb) {
        case "wait": Thread.Sleep(st.Ms); return "waited " + st.Ms + " ms";
        case "waitfor": return WaitFor(st.Arg, st.Ms, true);
        case "waitgone": return WaitFor(st.Arg, st.Ms, false);
      }
      string note;
      Node n = Resolve(st.Target, out note);
      string label = Line(n, false) + note;
      switch (st.Verb) {
        case "click": return label + " -> " + DoClick(n);
        case "set": return label + " -> " + DoSet(n, st.Arg);
        case "toggle": return label + " -> " + DoToggle(n, st.Arg);
        case "select": return label + " -> " + DoSelect(n, st.Arg);
        case "expand": return label + " -> " + DoExpand(n, true);
        case "collapse": return label + " -> " + DoExpand(n, false);
        case "scroll": return label + " -> " + DoScroll(n, st.Arg, st.Ms);
        case "scrollto": return label + " -> " + DoScrollTo(n);
        case "focus":
          Timed(delegate { n.El.SetFocus(); }, "focus");
          return label + " -> focused (this can bring the window to the front)";
        case "get": return DoGet(n, note);
      }
      throw new UiError(1, "unknown step " + st.Verb);
    }

    // ---------------------------------------------------------------- resolving targets

    static readonly HashSet<string> RoleWords = new HashSet<string> {
      "button", "checkbox", "combobox", "textfield", "link", "listitem", "menuitem", "radio", "slider",
      "spinner", "tab", "treeitem", "cell", "group", "pane", "list", "document", "text", "tree", "menu", "toolbar",
    };

    Node Resolve(string target, out string note) {
      note = "";
      Node n = TryResolve(target, ref note);
      if (n == null && !settled) { ReadSettled(); n = TryResolve(target, ref note); }
      if (n == null) {
        if (Regex.IsMatch(target, @"^e\d+$")) {
          Entry e = lastRef;
          if (e == null && !WasKnown(target))
            throw new UiError(2, target + " is not a ref of this window. Take a snapshot first (refs belong to one window).");
          throw new UiError(2, target + (e != null ? " (" + e.Role + " \"" + Clip(e.Name, 40) + "\")" : "")
                               + " is no longer on screen. Take a new snapshot and use its refs.");
        }
        throw new UiError(2, "nothing matches '" + target + "'. Take a snapshot (-Find helps) and use a ref.");
      }
      return n;
    }

    bool WasKnown(string r) { int k; return int.TryParse(r.Substring(1), out k) && k < next; }

    Entry lastRef;

    Node TryResolve(string target, ref string note) {
      bool isRef = Regex.IsMatch(target, @"^e\d+$");
      Entry old;
      if (isRef && refs.TryGetValue(target, out old)) lastRef = old;
      ReadTree();
      if (isRef) {
        Node hit = All.FirstOrDefault(x => x.Ref == target);
        if (hit != null) return hit;
        // Web UIs re-render: the same control comes back as a new element. Accept it only when
        // role, name and automation id still single out exactly one element.
        var e = lastRef;
        if (e == null || e.Name == "") return null;
        var c = All.Where(x => x.RefAble && x.Role == e.Role && x.Label == e.Name && x.Aid == e.Aid).ToList();
        if (c.Count != 1) return null;
        note = " (was " + target + ": re-found by name, the old element was replaced)";
        return c[0];
      }
      if (target.StartsWith("#")) {
        string aid = target.Substring(1);
        var c = All.Where(x => x.Aid == aid).ToList();
        if (c.Count == 0) return null;
        if (c.Count > 1) c = Prefer(c);
        if (c.Count > 1) throw Ambiguous(target, c);
        return c[0];
      }
      string role = null, name = target;
      var rm = Regex.Match(target, @"^([a-z]+):(.+)$", RegexOptions.Singleline);
      if (rm.Success && RoleWords.Contains(rm.Groups[1].Value)) { role = rm.Groups[1].Value; name = rm.Groups[2].Value.Trim(); }
      Func<Node, bool> roleOk = x => role == null ? x.RefAble : x.Role == role;
      var exact = All.Where(x => roleOk(x) && x.Label.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
      var cands = exact.Count > 0 ? exact : All.Where(x => roleOk(x) && Has(x.Label, name)).ToList();
      if (cands.Count == 0) return null;
      if (cands.Count > 1) cands = Prefer(cands);
      if (cands.Count > 1) throw Ambiguous(target, cands);
      if (exact.Count == 0) note = " (matched by part of its name)";
      return cands[0];
    }

    // Among several matches, an on-screen enabled one wins if it is the only such one.
    static List<Node> Prefer(List<Node> c) {
      var vis = c.Where(x => !x.Offscreen && x.Enabled).ToList();
      if (vis.Count == 1) return vis;
      // A clickable wrapper and its own labelled child often share a name: keep the outermost.
      var outer = c.Where(x => !c.Any(o => o != x && IsAncestor(o, x))).ToList();
      return outer.Count >= 1 && outer.Count < c.Count ? outer : c;
    }

    static bool IsAncestor(Node a, Node d) { for (var p = d.Parent; p != null; p = p.Parent) if (p == a) return true; return false; }

    UiError Ambiguous(string target, List<Node> c) {
      var sb = new StringBuilder();
      sb.Append("'").Append(target).Append("' matches ").Append(c.Count).Append(" elements; use a ref:");
      foreach (var x in c.Take(10)) {
        sb.Append("\n  ").Append(Line(x, true));
        string ctx = Context(x);
        if (ctx != "") sb.Append("  (in ").Append(ctx).Append(')');
      }
      return new UiError(2, sb.ToString());
    }

    static string Context(Node n) {
      for (var p = n.Parent; p != null; p = p.Parent)
        if (p.Name != "" && p.Name != n.Name && p.Parent != null) return p.Role + " \"" + Clip(p.Name, 40) + "\"";
      return "";
    }

    // ---------------------------------------------------------------- actions

    // UIA calls go to the app and can block (e.g. a button that opens a modal dialog keeps
    // Invoke from returning). Run them on a worker and stop waiting after a few seconds.
    static string Timed(Action a, string ok) {
      Exception err = null;
      var t = new Thread(delegate () { try { a(); } catch (Exception e) { err = e; } });
      t.IsBackground = true;
      t.Start();
      if (!t.Join(4000)) return ok + " (the call has not returned after 4 s: the app may be showing a modal dialog)";
      if (err != null) {
        if (err is ElementNotEnabledException) throw new UiError(3, "the element is disabled");
        if (err is ElementNotAvailableException) throw new UiError(2, "the element went away; take a new snapshot");
        throw new UiError(3, err.GetType().Name + ": " + err.Message);
      }
      return ok;
    }

    static T Pat<T>(Node n, AutomationPattern p) where T : BasePattern {
      object o;
      try { if (n.El.TryGetCurrentPattern(p, out o)) return o as T; } catch (ElementNotAvailableException) { }
      return null;
    }

    string NoHeadless(Node n, string what, string fallback) {
      string at = CenterText(n);
      return n.Role + " \"" + Clip(n.Name, 40) + "\" has no UI Automation pattern for " + what + ". "
           + (at == "" ? "It has no on-screen position either." : "Use screen-input instead: " + fallback.Replace("@", at));
    }

    string CenterText(Node n) {
      if (n.Rect.IsEmpty || n.Rect.Width <= 0 || n.Offscreen) return "";
      var w = Native.VisibleRect(W.Hwnd);
      return string.Format("{0},{1}", (int)(n.Rect.X + n.Rect.Width / 2) - w.L, (int)(n.Rect.Y + n.Rect.Height / 2) - w.T);
    }

    string DoClick(Node n) {
      var inv = Pat<InvokePattern>(n, InvokePattern.Pattern);
      if (inv != null) return Timed(delegate { inv.Invoke(); }, "invoked");
      var sel = Pat<SelectionItemPattern>(n, SelectionItemPattern.Pattern);
      if (sel != null) return Timed(delegate { sel.Select(); }, "selected");
      var tg = Pat<TogglePattern>(n, TogglePattern.Pattern);
      if (tg != null) { string r = Timed(delegate { tg.Toggle(); }, "toggled"); return r + " -> " + ToggleText(tg); }
      var ec = Pat<ExpandCollapsePattern>(n, ExpandCollapsePattern.Pattern);
      if (ec != null) return DoExpand(n, ec.Current.ExpandCollapseState == ExpandCollapseState.Collapsed);
      throw new UiError(3, NoHeadless(n, "click", "click @"));
    }

    static string ToggleText(TogglePattern tg) {
      Thread.Sleep(60);
      try {
        var s = tg.Current.ToggleState;
        return s == ToggleState.On ? "on" : s == ToggleState.Off ? "off" : "mixed";
      } catch { return "?"; }
    }

    string DoSet(Node n, string value) {
      var vp = Pat<ValuePattern>(n, ValuePattern.Pattern);
      if (vp == null) throw new UiError(3, NoHeadless(n, "setting a value", "click @; key ctrl+a; type TEXT"));
      if (vp.Current.IsReadOnly) throw new UiError(3, "the field is read-only");
      string r = Timed(delegate { vp.SetValue(value); }, "set");
      Thread.Sleep(60);
      if (n.Password) return r + " (password field: not read back)";
      string now = "";
      try { now = vp.Current.Value; } catch { }
      return now == value ? r + " (verified)" : r + " (now reads \"" + Clip(Clean(now), 60) + "\")";
    }

    string DoToggle(Node n, string want) {
      var tg = Pat<TogglePattern>(n, TogglePattern.Pattern);
      if (tg == null) {
        if (want == "on") {
          var sel = Pat<SelectionItemPattern>(n, SelectionItemPattern.Pattern);
          if (sel != null) return Timed(delegate { sel.Select(); }, "selected");
        }
        var inv = Pat<InvokePattern>(n, InvokePattern.Pattern);
        if (inv != null) return Timed(delegate { inv.Invoke(); }, "invoked (no toggle state to check)");
        throw new UiError(3, NoHeadless(n, "toggle", "click @"));
      }
      string now = ToggleText(tg);
      if (want != null && now == want) return "already " + want;
      for (int i = 0; i < 3; i++) {
        Timed(delegate { tg.Toggle(); }, "toggled");
        now = ToggleText(tg);
        if (want == null || now == want) break;
      }
      if (want != null && now != want) throw new UiError(3, "toggled 3 times but it reads " + now + ", not " + want);
      return "now " + now;
    }

    string DoExpand(Node n, bool expand) {
      var ec = Pat<ExpandCollapsePattern>(n, ExpandCollapsePattern.Pattern);
      if (ec == null) throw new UiError(3, NoHeadless(n, expand ? "expand" : "collapse", "click @"));
      var s = ec.Current.ExpandCollapseState;
      if (s == ExpandCollapseState.LeafNode) throw new UiError(3, "it has nothing to expand (leaf)");
      if (expand && s == ExpandCollapseState.Expanded) return "already expanded";
      if (!expand && s == ExpandCollapseState.Collapsed) return "already collapsed";
      return Timed(delegate { if (expand) ec.Expand(); else ec.Collapse(); }, expand ? "expanded" : "collapsed");
    }

    string DoSelect(Node n, string option) {
      if (option == null) {
        var sel = Pat<SelectionItemPattern>(n, SelectionItemPattern.Pattern);
        if (sel != null) return Timed(delegate { sel.Select(); }, "selected");
        return DoClick(n);
      }
      string rid = n.Rid;
      Node opt = FindOption(n, option, null);
      bool opened = false;
      if (opt == null) {
        var ec = Pat<ExpandCollapsePattern>(n, ExpandCollapsePattern.Pattern);
        if (ec != null && ec.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) {
          var before = new HashSet<string>(All.Select(x => x.Rid));
          Timed(delegate { ec.Expand(); }, "expanded");
          opened = true;
          Thread.Sleep(300);
          ReadTree();
          n = All.FirstOrDefault(x => x.Rid == rid) ?? n;
          opt = FindOption(n, option, before);
        }
      }
      if (opt == null) {
        var vp = Pat<ValuePattern>(n, ValuePattern.Pattern);
        if (vp != null && !vp.Current.IsReadOnly) return DoSet(n, option);
        var names = Under(n).Where(x => x.RefAble && x.Name != "").Select(x => "\"" + Clip(x.Name, 30) + "\"").Distinct().Take(15);
        throw new UiError(2, "no option '" + option + "' in it. Options seen: " + string.Join(", ", names));
      }
      string label = "\"" + Clip(opt.Name, 40) + "\"";
      var s2 = Pat<SelectionItemPattern>(opt, SelectionItemPattern.Pattern);
      if (s2 != null) Timed(delegate { s2.Select(); }, "selected");
      else {
        var inv = Pat<InvokePattern>(opt, InvokePattern.Pattern);
        if (inv == null) throw new UiError(3, NoHeadless(opt, "select", "click @"));
        Timed(delegate { inv.Invoke(); }, "invoked");
      }
      if (opened) {
        Thread.Sleep(100);
        var ec2 = Pat<ExpandCollapsePattern>(n, ExpandCollapsePattern.Pattern);
        try { if (ec2 != null && ec2.Current.ExpandCollapseState == ExpandCollapseState.Expanded) ec2.Collapse(); } catch { }
      }
      return "selected " + label;
    }

    // An option inside the control, or (once a dropdown has opened) a newly shown list/menu item.
    Node FindOption(Node n, string name, HashSet<string> before) {
      Func<Node, bool> pick = x => x.RefAble && (x.HasSel || x.HasInvoke) && x != n;
      var inside = Under(n).Where(pick).ToList();
      var hit = Match(inside, name);
      if (hit != null || before == null) return hit;
      var fresh = All.Where(x => pick(x) && !before.Contains(x.Rid)
                              && (x.Role == "listitem" || x.Role == "menuitem" || x.Role == "treeitem")).ToList();
      return Match(fresh, name);
    }

    static Node Match(List<Node> c, string name) {
      var exact = c.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
      if (exact.Count >= 1) return exact[0];
      var part = c.Where(x => Has(x.Name, name)).ToList();
      return part.Count == 1 ? part[0] : null;
    }

    static IEnumerable<Node> Under(Node n) {
      foreach (var k in n.Kids) { yield return k; foreach (var d in Under(k)) yield return d; }
    }

    string DoScroll(Node n, string dir, int count) {
      Node s = n;
      while (s != null && !(s.HasScroll && s.CanScroll)) s = s.Parent;
      if (s == null) throw new UiError(3, NoHeadless(n, "scroll", "scroll @ " + (dir == "up" || dir == "pageup" || dir == "top" ? "3" : "-3")));
      var sp = Pat<ScrollPattern>(s, ScrollPattern.Pattern);
      if (sp == null) throw new UiError(3, "the scroll area went away; take a new snapshot");
      Action a;
      switch (dir) {
        case "top": a = delegate { sp.SetScrollPercent(ScrollPattern.NoScroll, 0); }; break;
        case "bottom": a = delegate { sp.SetScrollPercent(ScrollPattern.NoScroll, 100); }; break;
        default: {
          bool horiz = dir == "left" || dir == "right";
          bool back = dir == "up" || dir == "left" || dir == "pageup";
          bool page = dir == "pageup" || dir == "pagedown";
          var amt = back ? (page ? ScrollAmount.LargeDecrement : ScrollAmount.SmallDecrement)
                         : (page ? ScrollAmount.LargeIncrement : ScrollAmount.SmallIncrement);
          int times = Math.Max(1, count);
          a = delegate {
            for (int i = 0; i < times; i++) {
              if (horiz) sp.ScrollHorizontal(amt); else sp.ScrollVertical(amt);
            }
          };
          break;
        }
      }
      Timed(a, "scrolled");
      Thread.Sleep(60);
      string where = "";
      try {
        double v = sp.Current.VerticalScrollPercent, h = sp.Current.HorizontalScrollPercent;
        where = dir == "left" || dir == "right" ? (h >= 0 ? " -> " + (int)h + "% across" : "")
                                                : (v >= 0 ? " -> " + (int)v + "% down" : "");
      } catch { }
      return "scrolled " + dir + (s != n ? " (" + (s.Ref ?? s.Role) + ")" : "") + where;
    }

    string DoScrollTo(Node n) {
      var si = Pat<ScrollItemPattern>(n, ScrollItemPattern.Pattern);
      if (si == null) throw new UiError(3, NoHeadless(n, "scroll into view", "scroll @ -3"));
      return Timed(delegate { si.ScrollIntoView(); }, "scrolled into view");
    }

    string DoGet(Node n, string note) {
      var sb = new StringBuilder();
      sb.Append(Line(n, true)).Append(note).Append('\n');
      var pats = new List<string>();
      if (n.HasInvoke) pats.Add("invoke");
      if (n.HasValue) pats.Add(n.ValueRO ? "value(readonly)" : "value");
      if (n.HasToggle) pats.Add("toggle");
      if (n.HasSel) pats.Add("select");
      if (n.HasExpand) pats.Add("expand");
      if (n.HasScroll) pats.Add("scroll");
      if (n.HasScrollItem) pats.Add("scrollto");
      sb.Append("   actions: ").Append(pats.Count > 0 ? string.Join(", ", pats) : "(none)").Append('\n');
      string at = CenterText(n);
      if (at != "") sb.Append("   center: ").Append(at).Append(" (window coords, for screen-input)  size ")
                      .Append((int)n.Rect.Width).Append('x').Append((int)n.Rect.Height).Append('\n');
      if (n.Cls != "") sb.Append("   class: ").Append(n.Cls).Append('\n');
      if (n.Help != "") sb.Append("   help: ").Append(Clip(n.Help, 200)).Append('\n');
      if (!string.IsNullOrEmpty(n.Value) && !n.Password && n.Value.Length > 80)
        sb.Append("   value: ").Append(Clip(n.Value, 2000)).Append('\n');
      var text = Under(n).Where(x => x.Name != "" && (x.Role == "text" || x.Ref != null))
                         .Select(x => x.Name).Where(s => s != n.Name).Distinct().ToList();
      if (text.Count > 0) sb.Append("   text: ").Append(Clip(string.Join(" / ", text), 2000)).Append('\n');
      string ctx = Context(n);
      if (ctx != "") sb.Append("   in: ").Append(ctx).Append('\n');
      return sb.ToString().TrimEnd('\n');
    }

    string WaitFor(string text, int ms, bool appear) {
      var sw = Stopwatch.StartNew();
      while (true) {
        ReadTree();
        var hit = All.FirstOrDefault(x => Has(x.Name, text) || Has(x.Value, text));
        if (appear && hit != null) return "found after " + sw.ElapsedMilliseconds + " ms: " + Line(hit, true);
        if (!appear && hit == null) return "gone after " + sw.ElapsedMilliseconds + " ms";
        if (sw.ElapsedMilliseconds >= ms)
          throw new UiError(2, (appear ? "'" + text + "' did not appear" : "'" + text + "' is still there") + " after " + ms + " ms");
        Thread.Sleep(250);
      }
    }

    // ---------------------------------------------------------------- text helpers

    static bool Has(string hay, string needle) {
      return !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    static string Clean(string s) { return s == null ? "" : Regex.Replace(s, @"\s+", " ").Trim(); }
    static string Flat(string s) { return s == null ? "" : Regex.Replace(s, @"[\t\r\n]+", " "); }
    static string Clip(string s, int n) { s = Clean(s); return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
    static string Ind(int n) { return new string(' ', n * 2); }
  }
}
