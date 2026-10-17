# build/F0a: tools

## t6: Postman CLI and Bicep CLI — 2026-10-17

| Tool | Installed by | Command | Version |
|---|---|---|---|
| Bicep CLI | winget `Microsoft.Bicep` | `bicep` | `Bicep CLI version 0.48.1 (cec7951cf6)` |
| Postman CLI | Postman's Windows installer (`dl-cli.pstmn.io/install/win64.ps1`), run by David | `postman` | `1.75.0` |

**Install notes**
- winget had to be pinned to `--source winget`. The `msstore` source failed with `0x8a15005e` (server certificate mismatch), and an unpinned install stopped at exit 94.
- The winget Postman package (`Postman.Postman.CLI`) was tried first and then uninstalled. It was stale at 1.16.0, and it installs `postman-cli`, not the `postman` the specs use. Postman's own installer gives the current version as `postman`.
- **Postman's installer was read in full before running.**
  - It verifies the binary's Authenticode signature (valid, signer "Postman, Inc.").
  - It installs to `%LOCALAPPDATA%\Microsoft\WindowsApps`.
  - It sends an install ping to Postman's Sentry.
  - **By default, it appends a marked "Postman CLI is installed on this device" block to `~/.claude/CLAUDE.md`** (and to `~/.codex/AGENTS.md`). Since t4 showed the user-level CLAUDE.md is loaded into every subagent, that step was disabled: `CLAUDE_CONFIG_DIR` and `CODEX_HOME` were pointed at a nonexistent folder for the installer's process only. `~/.claude/CLAUDE.md` had the same hash before and after, with no Postman block.

**Bicep.** A two-line template built to ARM JSON with exit 0. A broken one (`param x string = `) exited 1 with `BCP009`. `az` was never run.

**Postman CLI: a local collection runs without login.**
- Precondition: no CLI credentials (`~/.postman` held only `logs/`).
- A v2.1 collection file plus an environment file, with one request (`GET {{baseUrl}}/health/ready`) and two `pm.test` assertions, ran against a throwaway Node server on `127.0.0.1:5099`.
- Result, on both 1.16.0 and 1.75.0: 1 request, 2/2 assertions passed, **exit 0**. The CLI prints "No authorization data found. Please use the `postman login` command", and on 1.16.0 also "Postman api key is required for publishing run details". Both are warnings only.
- Control: the same run with `--env-var baseUrl=…/nope` got a 404, failed both assertions, and **exited 1**.

**Consequences**
- `gate.postman` is viable: the orchestrator can run the node's folder unattended, with no login and no API key, and use the exit code as the receipt (orchestration step 14 / testing Q6). Postman is **not** David-only.
- The spec commands (`postman collection run …`) run as written.
