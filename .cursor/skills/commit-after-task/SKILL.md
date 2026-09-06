---
name: commit-after-task
description: >-
  Creates a Conventional Commit with a meaningful English message after every
  completed OpenSignature task. Use when a task is finished, when TASKS.md is
  marked DONE, after tests and acceptance criteria pass, or when the user asks
  to commit completed work.
---

# Commit After Task

After every completed task, create a git commit. Do not leave completed work uncommitted.

A task is complete only when implementation is done, tests pass, acceptance criteria pass, and relevant docs (including `docs/TASKS.md` when applicable) are updated.

## When to commit

Commit immediately after:

- a `docs/TASKS.md` item is marked `DONE`
- a coherent implementation, fix, test, or documentation task finishes
- the user asks to finish a task and the work is actually complete

Do not commit:

- incomplete or failing work
- unrelated files sitting in the working tree
- secrets, private keys, PFX files, certificates with private keys, credentials, or `.env` files
- when there are no changes

Push to remote only when the user explicitly asks.

## Commit workflow

Run these in parallel first:

```text
git status
git diff
git log -15 --oneline
```

Then:

1. Confirm tests for the completed task passed.
2. Stage **only** files that belong to this task.
3. Draft a Conventional Commit message that matches this repository's history.
4. Commit.
5. Run `git status` and confirm the commit succeeded and unrelated files remain unstaged.

Never use `git add -A` or `git add .` if unrelated changes exist.

On PowerShell, pass the message with here-strings:

```powershell
git commit -m @"
feat(signing): add PAdES baseline B signer

Persist the digest-then-sign path so hardware providers never export private keys.
"@
```

## Message format

Use Conventional Commits in English:

```text
type(scope): short summary of why

Optional body: why the change exists, not a file list.
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `perf`, `ci`.

Common scopes: `api`, `signing`, `worker`, `queue`, `storage`, `web`, `security`, `docs`, `test`, `cursor`.

Rules:

- Focus on **why**, not a dump of **what** files changed.
- Keep the subject to one sentence, imperative mood, no trailing period.
- Match existing history (see `git log`).
- Product name in subjects/bodies is **OpenSignature** when referring to the product.

## Good examples

```text
feat(api): add asynchronous signature request endpoint
feat(signing): add PAdES baseline B signer
feat(queue): add RabbitMQ signing worker
fix(worker): prevent duplicate job processing
test(signing): add PAdES interoperability tests
docs(api): document signature endpoints
chore(cursor): require a Conventional Commit after every completed task
```

## Reject these messages

```text
update
changes
fix
work
final
stuff
test
WIP
misc
```

## Safety

- Never update git config.
- Never skip hooks (`--no-verify`, `--no-gpg-sign`) unless the user explicitly asks.
- Never amend unless the user asked, HEAD is this conversation's commit, and it has not been pushed.
- If a hook rejects the commit, fix the issue and create a **new** commit. Do not amend.
- Never force-push `main`/`master`.
- Never run interactive git (`-i`).
