// SessionStart hook: tells Claude the three tiers of markdown files for this project,
// lists this project's know-how titles, and asks for a cleanup when the project is new to
// md-tiers or the last cleanup is old.
// Everything is per project: know-how lives in <project>/.claude/knowhow/<slug>.md.
const fs = require('fs');
const path = require('path');
const os = require('os');

let input = {};
try { input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}'); } catch {}
const project = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();

const KNOWHOW = path.join(project, '.claude', 'knowhow');
const STATE = path.join(project, '.claude', 'md-tiers.json'); // { "cleanup": "YYYY-MM-DD" }
const OLD_GLOBAL = path.join(os.homedir(), '.claude', 'knowhow'); // version 0 kept tips here
const PROMOTE = 3;
const MAX = 40;
const CLEANUP_DAYS = 30;

/** Minimal frontmatter reader: key: value lines between the first two "---". */
function meta(text) {
  const m = /^---\r?\n([\s\S]*?)\r?\n---/.exec(text);
  const out = {};
  if (m) for (const line of m[1].split(/\r?\n/)) {
    const i = line.indexOf(':');
    if (i > 0) out[line.slice(0, i).trim()] = line.slice(i + 1).trim();
  }
  return out;
}

function mdFiles(dir) {
  try { return fs.readdirSync(dir).filter(f => f.endsWith('.md') && f.toLowerCase() !== 'readme.md'); } catch { return []; }
}

const tips = mdFiles(KNOWHOW).map(f => {
  let m = {};
  try { m = meta(fs.readFileSync(path.join(KNOWHOW, f), 'utf8')); } catch {}
  return { f, title: m.title || f, uses: parseInt(m.uses, 10) || 0 };
});
tips.sort((a, b) => b.uses - a.uses || a.f.localeCompare(b.f));

let lastCleanup = null;
try { lastCleanup = JSON.parse(fs.readFileSync(STATE, 'utf8')).cleanup || null; } catch {}
const daysSince = lastCleanup ? (Date.now() - Date.parse(lastCleanup)) / 86400000 : Infinity;

const lines = [
  '[md-tiers] 이 프로젝트에서 Claude가 읽고 쓰는 md 파일은 세 등급이고, 모두 이 프로젝트에 붙는다(사용자 자신에 대한 것, 예: 답하는 언어만 전역).',
  '- 🔒 지침 (CLAUDE.md, .claude/skills·agents·commands·rules, 설정, 훅, 이 프로젝트 메모리): 사용자가 그 변경을 요청할 때만 고친다. 다른 작업을 하다가 같이 고치지 않는다(고치려 하면 승인 창이 뜬다). 지침이 틀려 보이면 말하고 바꿀 내용을 제안한다. 메모리는 사용자가 기억하라고 한 오래 갈 사실만: 그때의 상태·추측·기분은 쓰지 않는다.',
  `- 💡 노하우 (.claude/knowhow/): 이 프로젝트의 중요도 낮은 팁, 지침과 부딪치면 지침이 이긴다. 작업이 아래 제목과 맞으면 그 파일을 읽는다. 써서 도움이 됐으면 uses +1·last 오늘. 막혔다가 실제로 풀린 방법은 작업 끝에 팁 하나로 남기고(knowhow 스킬) 보고에 한 줄로 알린다. uses ${PROMOTE} 이상(⬆)은 작업 끝에 지침으로 올릴지 사용자에게 묻는다.`,
  '- ✏️ 작업 노트 (.claude/commit-notes/<작업>.md, .claude/verify-todo/<작업>.md): 작업이 끝나고 빌드·테스트가 통과한 뒤 한 번, 자기 작업 파일만 쓰고 보고에 그대로 보여 준다. 기능·수정을 커밋하지 않고 끝내면 커밋 노트(commit-notes 스킬), 사용자에게 손으로 확인을 부탁하면 확인 항목(verify-todo 스킬).',
];
if (!lastCleanup) {
  lines.push('- ⚠ 이 프로젝트는 아직 md 정리를 한 적이 없다. 첫 답에서 사용자에게 "md 오염 정리(md-cleanup 스킬)를 먼저 돌릴까요?"라고 한 번 묻는다. 사용자가 다른 일을 시켰으면 그 일을 먼저 하고 끝에 묻는다.');
} else if (daysSince > CLEANUP_DAYS) {
  lines.push(`- ⚠ 마지막 md 정리가 ${Math.floor(daysSince)}일 전(${lastCleanup})이다. 이번 작업이 끝나면 md 오염 정리(md-cleanup 스킬)를 할지 사용자에게 한 번 묻는다.`);
}
if (mdFiles(OLD_GLOBAL).length) {
  lines.push(`- 전역 노하우 폴더(${OLD_GLOBAL.replace(/\\/g, '/')})에 예전 팁이 남아 있다. md-cleanup이 프로젝트로 옮긴다.`);
}
lines.push(tips.length ? '노하우 목록:' : '노하우 목록: (아직 없음)');
for (const t of tips.slice(0, MAX)) {
  const flag = t.uses >= PROMOTE ? ' ⬆승격 후보' : '';
  lines.push(`- ${t.title} — ${t.f} · ${t.uses}회${flag}`);
}
if (tips.length > MAX) lines.push(`(…외 ${tips.length - MAX}개, .claude/knowhow/에서 찾기)`);
process.stdout.write(lines.join('\n') + '\n');
