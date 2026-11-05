---
name: subagent-model
description: Pick the model for a subagent before calling the Agent tool, instead of letting it inherit the main model (often Opus at high effort). Use right before delegating wide code searches, finding out how something is used, tracing a flow, root-cause hunts or reviews, and when the user asks about subagent cost or models ("서브 에이전트 모델", "서브 에이전트 비용", "haiku로", "sonnet으로"). Also decides when to skip the subagent and read the files yourself.
---

# Subagent model

Don't let a subagent inherit the main model by default. Pass `model` on that Agent call, matched to the job.

| Job | `model` |
|---|---|
| Wide search for names or file locations only | `haiku` |
| Finding out how something is used, tracing a call flow, summarizing structure | `sonnet`, and ask it to return the key locations (`file:line`) |
| Root-cause hunts, reviews, reasoning about what the code doesn't say | omit it (main model) |

- A job that only needs a few files: read them yourself, no subagent. Spawning one costs more.
- Before changing code based on a subagent's analysis, open the key locations it returned and check them yourself.
- `fork` ignores `model`. To run on a smaller model, use another type such as `general-purpose`.
- Don't propose `.claude/agents` definitions or settings changes for this. Set `model` on the call only.
