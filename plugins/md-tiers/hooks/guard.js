// Guard for instruction files (CLAUDE.md, skills, agents, commands, rules, settings, hooks, memory).
// It never asks the user: it keeps a backup before every edit and makes sure the report says what changed.
//   node guard.js turn   UserPromptSubmit: a new message starts, forget the last message's changes
//   node guard.js pre    PreToolUse: back up a locked file before Edit/Write touches it
//   node guard.js post   PostToolUse: remember which locked files (or shell commands) really changed
//   node guard.js stop   Stop: if locked files changed and the final report doesn't name them, send
//                        Claude back once to add them (what and why, plus the backup folder)
// Know-how (.claude/knowhow/) and work notes (.claude/commit-notes/, .claude/verify-todo/) are free.
const fs = require('fs');
const os = require('os');
const path = require('path');

const mode = process.argv[2] || 'pre';
let input = {};
try { input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}'); } catch { process.exit(0); }
const tool = input.tool_name || '';
const ti = input.tool_input || {};
const project = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
const BACKUP = path.join(project, '.claude', 'md-tiers', 'backup');
const KEEP = 100; // newest backups kept

const stateFile = path.join(os.tmpdir(), 'md-tiers-' + String(input.session_id || 'none').replace(/[^\w-]/g, '') + '.json');
function readState() { try { return JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch { return { changed: [] }; } }
function writeState(st) { try { fs.writeFileSync(stateFile, JSON.stringify(st)); } catch {} }

const home = os.homedir().replace(/\\/g, '/').toLowerCase();
/** Lowercase, forward slashes, "/x/..." -> "x:/...", "~" -> home. */
function norm(p) {
  let s = String(p || '').replace(/\\/g, '/').toLowerCase();
  s = s.replace(/^\/([a-z])\//, '$1:/');
  if (s.startsWith('~/')) s = home + s.slice(1);
  return s;
}

const LOCKED = [
  /(^|\/)claude(\.local)?\.md$/,
  /\/\.claude\/(skills|agents|commands|rules)\//,
  /\/\.claude\/settings(\.local)?\.json$/,
  /\/\.claude\/hooks\//,
  /\/\.claude\/projects\/[^/]+\/memory\//,
];
const isLocked = p => LOCKED.some(re => re.test(norm(p)));

/** A shell command that seems to write a locked file or turn this plugin off. */
function lockedShellWrite(cmd) {
  const c = norm(cmd).replace(/[0-9&]?>\s*(\/dev\/null|\$null|nul\b|&[12])/g, ' ');
  if (/\bclaude\s+plugins?\s+(disable|uninstall|remove)\b/.test(c)) return true;
  const mentions = /claude(\.local)?\.md|\.claude\/(skills|agents|commands|rules|hooks)\b|\.claude\/settings(\.local)?\.json|\/memory\b|^memory\b|\bmemory\//.test(c);
  const writes = />|\btee\b|\bsed\b[^|;]*\s-i|\b(rm|mv|cp|del|ren|truncate|install)\b|\bperl\s+-[a-z]*i|set-content|add-content|out-file|remove-item|move-item|copy-item|new-item|rename-item|clear-content|writefile|appendfile|\bgit\s+(checkout|restore|rm|mv|reset|stash)\b/.test(c);
  return mentions && writes;
}

const FILE_TOOLS = ['Edit', 'Write', 'MultiEdit', 'NotebookEdit'];
const target = () => ti.file_path || ti.notebook_path || '';

if (mode === 'turn') { writeState({ changed: [] }); process.exit(0); }

if (mode === 'pre') {
  const p = target();
  if (FILE_TOOLS.includes(tool) && isLocked(p) && fs.existsSync(p)) {
    try {
      if (!fs.existsSync(BACKUP)) {
        fs.mkdirSync(BACKUP, { recursive: true });
        const exclude = path.join(project, '.git', 'info', 'exclude'); // keep backups out of git status
        if (fs.existsSync(path.dirname(exclude))) {
          const cur = fs.existsSync(exclude) ? fs.readFileSync(exclude, 'utf8') : '';
          if (!cur.includes('.claude/md-tiers/')) fs.appendFileSync(exclude, (cur && !cur.endsWith('\n') ? '\n' : '') + '.claude/md-tiers/\n');
        }
      }
      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      fs.copyFileSync(p, path.join(BACKUP, `${stamp}__${path.basename(p)}`));
      const old = fs.readdirSync(BACKUP).sort();
      for (const f of old.slice(0, Math.max(0, old.length - KEEP))) fs.rmSync(path.join(BACKUP, f), { force: true });
    } catch { /* a failed backup must never block the edit */ }
  }
  process.exit(0);
}

if (mode === 'post') {
  let what = null;
  if (FILE_TOOLS.includes(tool) && isLocked(target())) what = target().replace(/\\/g, '/');
  else if ((tool === 'Bash' || tool === 'PowerShell') && lockedShellWrite(ti.command || '')) {
    const c = String(ti.command || '');
    what = '셸: ' + (c.length > 120 ? c.slice(0, 120) + '…' : c);
  }
  if (what) {
    const st = readState();
    if (!st.changed.includes(what)) st.changed.push(what);
    writeState(st);
  }
  process.exit(0);
}

if (mode === 'stop') {
  if (input.stop_hook_active) process.exit(0); // already sent back once: never loop
  const st = readState();
  if (!st.changed.length) process.exit(0);
  const report = lastReport(input.transcript_path).toLowerCase();
  const missing = st.changed.filter(w => {
    if (w.startsWith('셸: ')) return !/(claude\.md|skill|memory|메모리|스킬|지침|설정|settings)/i.test(report);
    return !report.includes(path.basename(w).toLowerCase());
  });
  if (!missing.length) process.exit(0);
  process.stdout.write(JSON.stringify({
    decision: 'block',
    reason: `[md-tiers] 이번 메시지에서 🔒 지침 파일을 고쳤는데 보고에 빠졌다: ${missing.join(', ')}. ` +
      `보고에 파일마다 무엇을 왜 바꿨는지 한 줄씩 적고, 되돌릴 백업은 ${BACKUP.replace(/\\/g, '/')}/ 에 있다고 적는다. 사과 없이 보고만 고친다.`,
  }));
  process.exit(0);
}

/** Text of Claude's messages since the user's last real message. */
function lastReport(file) {
  try {
    const TAIL = 4 * 1024 * 1024;
    const fd = fs.openSync(file, 'r');
    const size = fs.fstatSync(fd).size;
    const start = Math.max(0, size - TAIL);
    const buf = Buffer.alloc(size - start);
    fs.readSync(fd, buf, 0, buf.length, start);
    fs.closeSync(fd);
    const lines = buf.toString('utf8').split('\n');
    if (start > 0) lines.shift();
    let texts = [];
    for (const line of lines) {
      let o;
      try { o = JSON.parse(line); } catch { continue; }
      const c = o.message && o.message.content;
      if (o.type === 'user' && typeof c === 'string') texts = [];
      else if (o.type === 'assistant' && Array.isArray(c)) for (const b of c) if (b.type === 'text' && b.text) texts.push(b.text);
    }
    return texts.join('\n');
  } catch { return ''; }
}
process.exit(0);
