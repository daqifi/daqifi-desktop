---
name: daqifi-loop
description: Start the autonomous daqifi-desktop work loop — a recurring cron that shepherds the user's open PRs through Qodo/CI and continuously works the backlog, opening one validated PR after another (up to a concurrency cap), picking up the next ticket after each, and doing self-directed improvements when the defined backlog is dry. NEVER merges. Use when the user wants to (re)start autonomous daqifi-desktop work while away, e.g. "start the loop", "resume the autonomous loop", "keep working on desktop while I'm out", "/daqifi-loop".
---

# DAQiFi autonomous work loop (desktop)

Sets up and kicks off a recurring cron loop that does productive, non-destructive work on this repo while the user is away, handing each result back for their review. **It never merges** — the user merges when they return (and can authorize a merge run explicitly).

This skill is the generic DAQiFi loop with a single **REPO PROFILE** block (below) that carries everything repo-specific. To retarget another .NET DAQiFi repo (e.g. daqifi-core), copy this file and change only that block. The body self-locates the repo (`git rev-parse`) and auto-discovers the board, so the same file works on macOS or Windows without edits.

## REPO PROFILE — the only repo-specific facts
- **Repo slug (gh):** `daqifi/daqifi-desktop`
- **Default branch:** `main`
- **Repo kind:** .NET WPF desktop app (Windows). A thin wrapper being migrated over Daqifi.Core.
- **Build/test gate:** `dotnet build` + FULL `dotnet test` green. Derive the target framework(s) from the csproj (do NOT assume net9+net10 — desktop is typically a single `net*-windows` TFM); run every TFM the solution targets.
- **Board discovery (do NOT hardcode a port):** each fire, discover the connected board dynamically via the app's/Core's serial discovery (a headless discovery entry point if the repo exposes one, else Daqifi.Core's `SerialDeviceFinder` / the Core example CLI `--discover`). Capture whatever serial/COM port it reports. If discovery finds nothing → skip bench steps this fire (the loop still runs).
- **Bench validation:** desktop is a **GUI (WPF) app with no clean headless drive** — do NOT try to script the UI. Primary validation is the FULL test suite. Use a live board only where a genuinely headless path exists (a console/smoke harness in the repo, or Core-level connect via the example CLI); otherwise rely on tests and say so. Never treat "couldn't bench the GUI" as a failure.
- **Destructive-op denylist (never do these unattended):** no firmware/WiFi-module flash, no `--sd-format`/`--sd-delete`, no device reboot/power-cycle, no destructive SD ops; skip live `SD:GET` while heap is low. On Windows, HID/firmware paths DO work (unlike macOS) — that makes them tempting and MORE dangerous to trigger; stay read/stream-only.
- **PATH note:** this repo runs on Windows — do NOT prepend `/opt/homebrew/bin` (that's a macOS-only step). Just ensure `dotnet`, `git`, and `gh` resolve on PATH.

## Optional args
- **Interval** (e.g. `20m`, `1h`, `10m`) — cadence. Default `10m` when the backlog looks active; suggest longer once things saturate. Convert to cron: `Nm`→`*/N * * * *` (N≤59), `1h`→`13 * * * *`, `Nh`→`13 */N * * *`.
- **A scope note** (e.g. "bug-fixes only", "no new features", "also do X", "up to 5 open PRs", "include the god-class decompose") — fold verbatim into the loop prompt. A cap phrase overrides the default concurrency cap of 3.

The loop keeps moving through the backlog — it shepherds open PRs, keeps starting the next ticket after each, and when no defined ticket is eligible it does self-directed improvements (researches → files a tracked issue → works it). It only idles at true saturation, and it still never merges.

## Steps

1. **Survey state** and report a short (≤6 line) summary to the user.
   - Repo root: `git rev-parse --show-toplevel`. If the `gh` remote isn't `daqifi/daqifi-desktop` (the REPO PROFILE slug), tell the user this skill instance targets desktop and stop.
   - Board: discover dynamically per the REPO PROFILE (do not hardcode). Note the discovered port, or "no board" if none — the loop still runs, just skips bench steps.
   - My open PRs + status: `gh pr list --repo daqifi/daqifi-desktop --state open --author @me --json number,title`; for each, unresolved qodo threads + CI.
   - Backlog: `gh issue list --repo daqifi/daqifi-desktop --state open` (candidate work to implement).

2. **Create the recurring loop** with `CronCreate` (`recurring: true`, the cron expression from the interval). Pass **exactly** the prompt in the block below, substituting `<REPO_ROOT>` (from step 1) and appending any scope note. (`CronCreate`/`CronDelete` are deferred tools — load them via `ToolSearch` first.)

   > **DISPATCHER — autonomous daqifi-desktop work loop** (user away; repo = `<REPO_ROOT>`; board = auto-discovered per fire). To keep this long-lived session's context small over many hours, do NOT do the work inline. Each fire: spawn exactly ONE subagent with the **Agent** tool (`subagent_type: general-purpose`, `run_in_background: false`) and pass it the TASK below verbatim as its prompt. Wait for it, then relay ONLY its short summary to the user and end the turn. Do NOT run git/gh/dotnet/bench commands yourself — that heavy I/O (builds, full test runs, diffs, Qodo comment bodies) belongs in the subagent's own context, which is discarded when it returns, so only a few lines per fire reach this session. If the subagent errors or returns nothing, say so in one line and end — the next fire re-triages.
   >
   > ══════════ TASK (the subagent's prompt) ══════════
   > You are ONE fire of the autonomous daqifi-desktop work loop (user away). Repo: `<REPO_ROOT>` (gh slug `daqifi/daqifi-desktop`, default branch `main`). Ensure `dotnet`/`git`/`gh` are on PATH (this is a Windows repo — do NOT prepend `/opt/homebrew/bin`). Your job is CONTINUOUS PROGRESS: shepherd open PRs AND keep implementing work, one unit after another. Do ONE focused unit of work this fire, in the priority order below; the NEXT fire picks up the next unit. Read `<REPO_ROOT>/SESSION_LOG.md` first for continuity, and re-derive live state from `gh` (don't trust stale notes). Board is NOT hardcoded — discover it this fire via Core/serial discovery; if none is found, skip bench steps.
   >
   > 1) PENDING QODO on my open PRs: `git fetch`; `gh pr list --repo daqifi/daqifi-desktop --state open --author @me`. For each, check for NEW unresolved `qodo-code-review` review threads. Valid finding → fix on the PR branch (build + affected tests + FULL `dotnet test` green on every TFM the solution targets), commit, push, re-comment `/agentic_review`. False positive → reply explaining why + resolve the thread. NEVER merge.
   > 2) READY PRs: a PR just went Qodo-clean (0 unresolved) + CI green and isn't noted → add a one-line "ready for review" note.
   > 3) CI REGRESSION on a previously-green PR: inspect the failing test. Unrelated flake → `gh run rerun --failed`, don't modify the PR. Real regression from my change → fix it.
   > 4) NEXT DEFINED TICKET — whenever this fire has no actionable item in 1–3 AND you're under the concurrency cap. An open PR merely awaiting the user's review does NOT block this: start the next ticket in parallel. Pick the next UNCLAIMED open issue (no open PR, no pushed branch), preferring bugs, then small validatable features. SKIP only: breaking-API tickets (unless the user opted in), pure-investigation tickets with no code deliverable, and tickets that can only be validated via destructive/disruptive board ops or GUI-only manual steps. If a candidate is infeasible/risky on inspection, note why in `SESSION_LOG.md` and move to the NEXT candidate — walk the backlog until you find one you can do or the eligible list is exhausted. Branch from `origin/main`, implement with tests matching repo conventions, build, FULL suite green on all target TFMs, validate on the board only where a headless path exists (see bench rules — do NOT script the WPF UI), open a PR (base main, "closes #N", note "not merging — for review"), comment `/agentic_review`. DO NOT MERGE.
   > 5) SELF-DIRECTED IMPROVEMENT — the default when 1–4 yield nothing AND you're under the cap. Do NOT idle or manufacture a marginal bench-only PR. RESEARCH the codebase for ONE concrete, high-confidence improvement (a latent bug, correctness/robustness fix, dedup/cleanup, a genuine test-coverage gap, or a small non-breaking feature) that you can defend as correct, non-breaking, and test-validatable. If you find one you're GENUINELY confident in: FILE a GitHub issue for it FIRST (`gh issue create` — problem + proposed fix + why it's safe), THEN work it as a normal ticket exactly per priority 4 (branch, tests, full suite green, open PR "closes #<new issue>", `/agentic_review`, NOT merging). Track every self-directed unit as its own issue so nothing lands untracked. Hold the same bar as any ticket — correctness over volume; do NOT invent low-value churn. Only if research surfaces nothing you're confident in → ONE light non-destructive validation pass, OR append "nothing confident to propose this fire" to `SESSION_LOG.md` and end.
   >
   > CONCURRENCY CAP: keep at most **3** loop-opened PRs awaiting the user's review at once (defined-ticket AND self-directed both count). Under the cap → priorities 4/5 keep opening new work. At the cap → don't start new tickets; just shepherd (1–3) and log. As the user merges/closes PRs, resume. (If the appended scope note names a different cap, use that.)
   >
   > Rules: never destructive board ops (no firmware/WiFi flash, `--sd-format`/`--sd-delete`, reboot/power-cycle); skip live `SD:GET` while heap is low; never script the WPF UI. Append this fire's outcome to `<REPO_ROOT>/SESSION_LOG.md`. Restore the working tree to the branch it started on before finishing. Correctness over volume. When done, RETURN a ≤4-line summary — the unit of work done, PR/issue #s touched, and test+bench result — and NOTHING else; your full transcript stays in your own context and only this summary reaches the loop session.
   > ══════════ END TASK ══════════

3. **Confirm** to the user: the returned cron **job id**, the cadence, that it works **continuously** (keeps opening new tickets/self-directed work after each, up to the cap of ~3 unreviewed PRs — adjustable), that it's **session-only** (dies when this session closes and auto-expires after 7 days), that it **never merges**, and that to stop it they run `CronDelete <job id>`.

4. **Run the first iteration now** — don't wait for the first cron fire. Follow the dispatcher pattern: spawn ONE `general-purpose` subagent (`run_in_background: false`) with the TASK block as its prompt, wait for it, relay its ≤4-line summary, then end the turn.

## Notes
- **Context hygiene via subagent delegation.** The cron fires into one long-lived session, so its context would otherwise accumulate every fire's builds, full test runs, diffs, and Qodo comment bodies until it fills. Each fire therefore spawns a single fresh `general-purpose` subagent to do the actual work; the subagent burns its own (discarded) context and returns only a ≤4-line summary, so the loop session grows a few lines per fire. This works because the loop is nearly stateless per fire — it re-derives PR/CI/ticket state from `gh` and uses `SESSION_LOG.md` on disk as durable memory. The subagent runs on the same machine, so it reaches the local board and checkout. If the session still fills over a long run, `/clear` (or a new session) and re-invoke this skill — `SESSION_LOG.md` + the open PRs preserve continuity.
- **Session-only + local.** The loop lives in a Claude Code session on THIS machine; keep it open. It cannot be moved to another machine — re-invoke this skill in a session there to re-establish it.
- **Desktop bench caveat.** WPF has no clean headless drive, so live-board validation is best-effort — the FULL test suite is the real gate. Don't block a fire on being unable to exercise the GUI.
- Merging is out of scope for the loop. When the user says to merge, do it separately (squash; verify merged `main` builds + full-suite-green afterward, especially if `--admin` bypass skips the combined CI check).
- Re-invoking this skill in a new session re-establishes the loop from scratch (crons are session-only, not persisted).
