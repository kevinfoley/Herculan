#!/usr/bin/env python3
"""Delete merged local `claude/*` branches and the worktrees that have them checked out.

A `claude/*` branch counts as merged when both hold:

- its tip is reachable from a target branch: every local and remote-tracking branch outside
  `claude/` by default, or the `--into` branches;
- it has commits of its own: its reflog records a commit, or its tip lies off the first-parent
  history of every branch outside `claude/` (so it arrived through the second parent of a merge).

The second test keeps a branch that was created and never committed to, such as a fresh session's
worktree that sits at its base branch's tip or behind it. A branch without reflog history whose tip
lies on another branch's first-parent history could be either that or work fast-forwarded into the target;
it is reported as unverified and kept unless `--include-unverified` is given. A squash or rebase
merge leaves the tip unreachable from any target, so such a branch is kept as unmerged.

A worktree is never forced: one with modified or untracked files, a locked one, the main checkout
and the worktree this script runs from are skipped, and so is their branch. Remote branches are not
touched.

Dry run by default; pass `--apply` to delete.
"""

import argparse
import os
import subprocess
import sys

PREFIX = "refs/heads/claude/"
COMMIT_REFLOG_PREFIXES = ("commit", "cherry-pick", "revert", "rebase")


def git(*args, cwd=None, check=True):
    result = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8")
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result


def norm_path(path):
    return os.path.normcase(os.path.abspath(path))


def list_refs(*patterns):
    out = git("for-each-ref", "--format=%(refname) %(objectname)", *patterns).stdout
    return dict(line.split(" ", 1) for line in out.splitlines())


def target_refs(into):
    if into:
        targets = {}
        for name in into:
            full = git("rev-parse", "--symbolic-full-name", name).stdout.strip()
            if not full:
                raise RuntimeError(f"--into {name} is not a branch")
            targets[full] = git("rev-parse", full).stdout.strip()
        return targets
    refs = list_refs("refs/heads", "refs/remotes")
    return {
        ref: sha for ref, sha in refs.items()
        if not ref.startswith(PREFIX)
        and not ref.endswith("/HEAD")
        and "/claude/" not in ref.removeprefix("refs/remotes/")
    }


def first_parent_commits(targets):
    out = git("rev-list", "--first-parent", *sorted(set(targets.values()))).stdout
    return set(out.split())


def is_ancestor(commit, of):
    return git("merge-base", "--is-ancestor", commit, of, check=False).returncode == 0


def reflog_has_commits(ref):
    out = git("reflog", "show", "--format=%gs", ref, check=False).stdout
    return any(line.startswith(COMMIT_REFLOG_PREFIXES) for line in out.splitlines())


def worktrees():
    """Map branch ref -> worktree record; the first record is the main checkout."""
    records, current = [], {}
    for line in git("worktree", "list", "--porcelain").stdout.splitlines():
        if not line:
            if current:
                records.append(current)
            current = {}
            continue
        key, _, value = line.partition(" ")
        current[key] = value
    if current:
        records.append(current)
    for i, record in enumerate(records):
        record["main"] = i == 0
    return {r["branch"]: r for r in records if "branch" in r}


def classify(ref, sha, targets, first_parent):
    """Return (merged, reason)."""
    containing = [t for t, t_sha in targets.items() if is_ancestor(sha, t_sha)]
    if not containing:
        return False, "not merged into any target"
    into = ", ".join(t.removeprefix("refs/heads/").removeprefix("refs/remotes/") for t in containing)
    if reflog_has_commits(ref):
        return True, f"merged into {into}"
    if sha not in first_parent:
        return True, f"merged into {into} (through a merge commit)"
    return None, f"no commits of its own found (tip lies on the history of {into})"


def worktree_blocker(record, here):
    path = record["worktree"]
    if record["main"]:
        return "checked out in the main checkout"
    if norm_path(path) == here:
        return "checked out in the worktree this script runs from"
    if "locked" in record:
        return "worktree is locked"
    if "prunable" in record or not os.path.isdir(path):
        return None
    status = git("status", "--porcelain", cwd=path, check=False)
    if status.returncode != 0:
        return f"git status failed in worktree: {status.stderr.strip()}"
    if status.stdout.strip():
        return "worktree has modified or untracked files"
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="delete; without it, only report")
    parser.add_argument("--into", action="append", metavar="BRANCH",
                        help="count only merges into this branch (repeatable); default: every non-claude branch")
    parser.add_argument("--include-unverified", action="store_true",
                        help="also delete branches with no evidence of commits of their own")
    args = parser.parse_args()

    try:
        here = norm_path(git("rev-parse", "--show-toplevel").stdout.strip())
        targets = target_refs(args.into)
        first_parent = first_parent_commits(target_refs(None))
        branches = list_refs(PREFIX.rstrip("/"))
        trees = worktrees()
    except RuntimeError as e:
        sys.exit(str(e))

    if not branches:
        print("No claude/ branches.")
        return

    to_delete, kept = [], []
    for ref, sha in sorted(branches.items()):
        name = ref.removeprefix("refs/heads/")
        merged, reason = classify(ref, sha, targets, first_parent)
        if merged is None and args.include_unverified:
            merged = True
        if not merged:
            kept.append((name, reason))
            continue
        record = trees.get(ref)
        blocker = record and worktree_blocker(record, here)
        if blocker:
            kept.append((name, f"{reason}, but {blocker}"))
            continue
        to_delete.append((name, record, reason))

    for name, reason in kept:
        print(f"keep    {name}: {reason}")
    for name, record, reason in to_delete:
        where = f" + worktree {record['worktree']}" if record else ""
        print(f"delete  {name}{where}: {reason}")

    if not args.apply:
        if to_delete:
            print("\nDry run; pass --apply to delete.")
        return

    failures = 0
    if any(record for _, record, _ in to_delete):
        git("worktree", "prune", check=False)
    for name, record, _ in to_delete:
        if record and os.path.isdir(record["worktree"]):
            result = git("worktree", "remove", record["worktree"], check=False)
            if result.returncode != 0:
                print(f"FAILED  {name}: worktree remove: {result.stderr.strip()}")
                failures += 1
                continue
        result = git("branch", "-D", name, check=False)
        if result.returncode != 0:
            print(f"FAILED  {name}: branch -D: {result.stderr.strip()}")
            failures += 1
            continue
        print(f"deleted {name}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
