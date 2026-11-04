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
  '[md-tiers] 이 프로젝트에서 Claude가 읽고 쓰는 md 파일은 세 등급이고, 모두 이 프로젝트에 붙는다(사용자 자신에 대한 것, 예: 답하는 언어만 전역). md 관리 때문에 사용자에게 묻거나 작업을 멈추지 않는다: 하고, 보고에 적는다.',
  '- 🔒 지침 (CLAUDE.md, .claude/skills·agents·commands·rules, 설정, 훅, 이 프로젝트 메모리): 사용자가 요청했을 때, 지침이 설명하는 코드·구조를 이번 작업에서 바꿔 지침을 맞춰야 할 때, 또는 아래 📜 스크립트 사용법을 적을 때만 고친다. 팁·요령·그때 상태·추측은 지침에 쓰지 않고 노하우로 보낸다. 고쳤으면 보고에 파일마다 무엇을 왜 바꿨는지 한 줄씩 적는다. 그 밖에 지침이 틀려 보이면 보고 끝에 바꿀 내용을 한 줄 제안만 한다. 메모리는 사용자가 기억하라고 한 오래 갈 사실만: 그때의 상태·추측·기분은 쓰지 않는다.',
  '- 📜 스크립트 사용법 (🔒 지침에 적는다): 이 프로젝트의 스크립트·도구(scripts/ 등)는 지침에 적힌 사용법만 보고 쓴다: 부르는 법, 인자, 출력(마지막 줄 형식 등), 언제 쓰는지. 본문은 사용법이 틀렸거나 고칠 때만 읽는다. 스크립트를 만들거나 인자·출력을 바꾸면 같은 작업에서 지침의 사용법도 고치고, 지침에 없는 스크립트를 읽어 해석했으면 그 결과를 지침에 적는다(요청 없이 해도 된다). "파일을 보라"로 넘기지 않는다.',
  `- 💡 노하우 (.claude/knowhow/): 이 프로젝트의 중요도 낮은 팁, 지침과 부딪치면 지침이 이긴다. 작업이 아래 제목과 맞으면 그 파일을 읽는다. 써서 도움이 됐으면 uses +1·last 오늘. 막혔다가 실제로 풀린 방법은 작업 끝에 팁 하나로 남기고(knowhow 스킬) 보고에 한 줄로 알린다. uses ${PROMOTE} 이상(⬆)은 보고 끝에 "⬆ 승격 후보: <제목>" 한 줄로 알린다(사용자가 올리라고 하면 올린다).`,
  '- ✏️ 작업 노트 (.claude/commit-notes/<작업>.md, .claude/verify-todo/<작업>.md): 작업이 끝나고 빌드·테스트가 통과한 뒤 한 번, 자기 작업 파일만 쓰고 보고에 그대로 보여 준다. 기능·수정을 커밋하지 않고 끝내면 커밋 노트(commit-notes 스킬), 사용자에게 손으로 확인을 부탁하면 확인 항목(verify-todo 스킬).',
];
if (!lastCleanup) {
  lines.push('- ⚠ 이 프로젝트는 아직 md 정리를 한 적이 없다. 사용자가 시킨 일을 먼저 하고, 보고 끝에 "md 정리를 안 한 프로젝트예요. \'md 정리해\'라고 하시면 해요" 한 줄만 붙인다(묻지 않는다).');
} else if (daysSince > CLEANUP_DAYS) {
  lines.push(`- ⚠ 마지막 md 정리가 ${Math.floor(daysSince)}일 전(${lastCleanup})이다. 보고 끝에 "md 정리한 지 오래됐어요. 'md 정리해'라고 하시면 해요" 한 줄만 붙인다(묻지 않는다).`);
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
