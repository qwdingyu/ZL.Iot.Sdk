---
name: test-driven-bug-finding
description: Methodical test-driven approach to discover real bugs in codebase, distinguishing actual logic bugs from test infrastructure issues
source: auto-skill
extracted_at: '2026-06-09T10:15:00.000Z'
---

# Test-Driven Bug Finding

Methodical approach to testing codebases with the goal of discovering real production bugs, not just checking if tests pass.

## Core Principle

**Test to find bugs, not to pass tests.** Every test run must have a clear target — what class of bug are we looking for? Don't run tests blindly.

## Procedure

### 1. Run tests with detailed output
- Use `--logger "console;verbosity=detailed"` to see per-test results
- Capture full output to a file for analysis (not just summary)
- Check framework alignment (e.g., net8.0 test project vs net10.0 SDK — use `--framework` flag to align)

### 2. Categorize failures immediately
- **Test infrastructure issues** (MySQL naming too long, missing Docker, wrong framework) → skip, note separately
- **Assertion failures** → likely real bugs, investigate source
- **No tests discovered** → test SDK/discovery mismatch, not a code bug

### 3. Read source code where tests fail
For each failure:
- Read the FULL source file of the failing test
- Read the FULL source file of the production code under test
- Follow the call chain — don't stop at the failing line
- Trace data flow: input → processing → expected output

### 4. Classify bugs by severity
| Level | Criteria | Example |
|-------|----------|---------|
| P0 | Silent data loss/corruption | Watermark advanced past failed rows, causing permanent skip |
| P0 | Race condition in concurrent access | SQLite multi-writer conflict via shared connection |
| P1 | Configuration silently ignored | `RetryBackoffSeconds` never bound from config |
| P2 | Dead code / overly aggressive exclusion | `_`-prefix tables excluded, but some are business tables |

### 5. Verify each bug independently
For each suspected bug:
- Check the exact code path that causes it
- Verify it's reproducible with a specific scenario
- Confirm it's NOT a test setup issue
- Check if existing tests would have caught it (if not, that's a separate finding)

### 6. Fix with minimal changes
- One bug per file when possible
- Preserve existing behavior for unrelated code
- Update comments/docstrings to match new logic
- Re-run ALL tests (not just the one that was failing)

## Common Bug Patterns to Look For

1. **Watermark/offset bugs** — advancing position past unprocessed data
2. **Idempotency gaps** — same input processed differently across runs
3. **Concurrency assumptions** — shared connections in single-writer systems (SQLite)
4. **Silent defaults** — config values that never get bound, always using defaults
5. **Overly broad exclusion** — filtering out more than intended (e.g., `LIKE '_%'` instead of `LIKE '_Sync%'`)
6. **Fallback corruption** — retry/backup logic that corrupts data instead of preserving it

## Anti-Patterns (Don't Do)

- Don't just run `dotnet test` and report pass/fail counts
- Don't modify test code to make failing tests pass (unless the test itself is wrong)
- Don't stop investigating after the first test fails
- Don't assume "tests pass = code is correct" — many bugs are edge cases tests don't cover

## Verification Checklist After Fixes

- [ ] All previously-passing tests still pass (no regression)
- [ ] Build produces no errors (warnings are OK but note them)
- [ ] No new compiler warnings introduced by fixes
- [ ] Test infrastructure failures (MySQL, Docker, etc.) are separate from code fixes
- [ ] Each fix has a clear rationale — not "cleanup for cleanup's sake"
