// PreToolUse hook: instruction files are the user's, not Claude's.
// Any edit to one (CLAUDE.md, skills, agents, commands, rules, settings, hooks, memory), or turning
// this plugin off, stops and asks the user, even in auto mode. Reads are never blocked.
// Know-how (.claude/knowhow/) and work notes (.claude/commit-notes/, .claude/verify-todo/) stay free.
// One approval per user message: once a locked edit has really run (PostToolUse), later ones in the
// same message pass. A denied edit never runs, so the next try asks again.
//   node protect.js        PreToolUse
//   node protect.js post   PostToolUse (records the approval)
//   node protect.js turn   UserPromptSubmit (starts a new message)
const fs = require('fs');
const os = require('os');
const path = require('path');

const mode = process.argv[2] || 'pre';

let input = {};
try { input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}'); } catch { process.exit(0); }
const tool = input.tool_name || '';
const ti = input.tool_input || {};

// Per-session state in the temp folder: { turn, approved } (approved = turn whose locked edit ran).
const stateFile = path.join(os.tmpdir(), 'md-tiers-' + String(input.session_id || 'none').replace(/[^\w-]/g, '') + '.json');
function readState() { try { return JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch { return {}; } }
function writeState(st) { try { fs.writeFileSync(stateFile, JSON.stringify(st)); } catch {} }

if (mode === 'turn') { writeState({ turn: Date.now() }); process.exit(0); }

const home = os.homedir().replace(/\\/g, '/').toLowerCase();

/** Lowercase, forward slashes, "/x/..." -> "x:/...", "~" -> home. */
function norm(p) {
  let s = String(p || '').replace(/\\/g, '/').toLowerCase();
  s = s.replace(/^\/([a-z])\//, '$1:/');
  if (s.startsWith('~/')) s = home + s.slice(1);
  return s;
}

// What counts as an instruction file, and its Korean label for the prompt.
const LOCKED = [
  [/(^|\/)claude(\.local)?\.md$/, 'CLAUDE.md'],
  [/\/\.claude\/(skills|agents|commands|rules)\//, '스킬·에이전트·명령·규칙 정의'],
  [/\/\.claude\/settings(\.local)?\.json$/, 'Claude Code 설정'],
  [/\/\.claude\/hooks\//, '훅'],
  [/\/\.claude\/projects\/[^/]+\/memory\//, '메모리'],
];

function lockedLabel(path) {
  const p = norm(path);
  for (const [re, label] of LOCKED) if (re.test(p)) return label;
  return null;
}

function ask(label, what) {
  const st = readState();
  if (mode === 'post') { writeState({ ...st, approved: st.turn || 0 }); process.exit(0); }
  if (st.approved !== undefined && st.approved === (st.turn || 0)) process.exit(0); // approved earlier in this message
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'ask',
      permissionDecisionReason: `[지침 파일 보호] ${label}: ${what} — 사용자 승인이 필요해요`,
    },
  }));
  process.exit(0);
}

if (['Edit', 'Write', 'MultiEdit', 'NotebookEdit'].includes(tool)) {
  const path = ti.file_path || ti.notebook_path;
  const label = lockedLabel(path);
  if (label) ask(label, norm(path));
  process.exit(0);
}

if (tool === 'Bash' || tool === 'PowerShell') {
  const cmd = String(ti.command || '');
  const short = cmd.length > 160 ? cmd.slice(0, 160) + '…' : cmd;
  const c = norm(cmd)
    .replace(/[0-9&]?>\s*(\/dev\/null|\$null|nul\b|&[12])/g, ' '); // discard redirects aren't writes
  if (/\bclaude\s+plugins?\s+(disable|uninstall|remove)\b/.test(c)) ask('플러그인 끄기', short);
  const mentions =
    /claude(\.local)?\.md|\.claude\/(skills|agents|commands|rules|hooks)\b|\.claude\/settings(\.local)?\.json|\/memory\b|^memory\b|\bmemory\//.test(c);
  const writes =
    />|\btee\b|\bsed\b[^|;]*\s-i|\b(rm|mv|cp|del|ren|truncate|install)\b|\bperl\s+-[a-z]*i|set-content|add-content|out-file|remove-item|move-item|copy-item|new-item|rename-item|clear-content|writefile|appendfile|\bgit\s+(checkout|restore|rm|mv|reset|stash)\b/.test(c);
  if (mentions && writes) ask('셸 명령', short);
}
process.exit(0);
