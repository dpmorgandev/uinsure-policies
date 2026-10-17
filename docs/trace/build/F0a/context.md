# build/F0a: context smoke tests

## t18: S1–S9 (context.spec) — 2026-10-17

S1–S5 were scripted (28 checks). S6–S8 were run through the real skills, invoked as skills rather than
imitated. S9 is the live session-start check.

| Test | Result | Receipt |
|---|---|---|
| S1 | **Pass.** `{"source":"startup"}` → exit 0; stdout is JSON with `hookEventName` `SessionStart`; the text ends `Run /orient before any work.`; ≤ 10,000 chars | script, 4 checks |
| S2 | **Pass.** `"source":"compact"` → the instruction names `/load-context-chain` | script |
| S3 | **Pass.** `not json` on stdin, and a cwd outside any git repo: both exit 0 and print the instruction line | script, 2 checks |
| S4 | **Pass.** The S1 output passes the privacy scan | script |
| S5 | **Pass.** 16 samples, one per deny pattern across the three categories, built at runtime by concatenation and never written to the repo: each is a hit. `noreply@anthropic.com`, an `@example.com` address and `git@github.com` aren't hits. In a scratch clone with the hooks installed, staging a change to `.claude/hooks/privacy-denylist.txt` commits cleanly | script, 20 checks |
| S6 (refusal) | **Pass.** `/save-context --slug s6-planted-path`, with an absolute user path planted in Current state, refused at step 7 with `26: absolute user paths`. The draft was deleted, no `*-s6-planted-path.md` was written, and the tree stayed clean | skill run |
| S6 (clean) | **Pass.** `/save-context --slug f0a-t18-smoke` wrote `docs/context/20261017-221833-f0a-t18-smoke.md`: 15/15 keys, 7/7 sections, 56/200 lines, privacy clean, nothing staged | skill run |
| S7 (2 snapshots) | **Pass.** `/load-context-chain` walked `f0a-t18-smoke` → `f0a-agents-done` and stopped there (`checkpoint: true`, inclusive). It synthesised oldest → newest, flagged staleness (none), drift (none: head `b40dbcd` matched) and chain integrity, and ended with exactly `Resume with: …?` and held. David confirmed | skill run |
| S7 (0 snapshots) | **Not re-run in t18.** It needs a checkout with no `docs/context/`. The only evidence is the step-0 snapshot's note "S7 empty-chain half verified … (no commit)", which wasn't recorded in a trail at the time | step-0 claim only |
| S8 | **Pass.** `/orient C:/Windows/System32` refused at its root guard ("the path argument resolves outside the repository root"), and nothing was read | skill run |
| S9 | **Pass (live).** This session opened with the SessionStart `[context]` lines, and the first reply was "The session hook says to run /orient before any work, so I'm starting there". /orient then ran | session start, 2026-10-17 |

**Finding (S5):** `privacy-scan.sh` skips the committed denylist by file identity (`-ef`). pre-commit
scans staged *copies* in a temp directory, so that skip never applies there. Staging the denylist passes
only because its pattern text doesn't currently match its own patterns: escaped dots and character
classes break the matches. That's latent, not live. If a future pattern is written so that it matches
its own text, pre-commit will flag the denylist. The fix would be for pre-commit to skip
`.claude/hooks/privacy-denylist.txt` by path.
