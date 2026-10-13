#!/usr/bin/env python3
"""Delete local `claude/*` branches whose work is safe elsewhere, their worktrees, and leftover
worktree directories.

A `claude/*` branch is deleted when its tip is reachable from a target branch: every local and
remote-tracking branch outside `claude/` by default, or the `--into` branches. Deleting such a
branch loses no commit. A squash or rebase merge leaves the tip unreachable from any target, so
such a branch is kept as unmerged.

A worktree is never forced: one with modified or untracked files, a locked one, the main checkout
and the worktree this script runs from are skipped, and so is their branch. Remote branches are not
touched.

Directories under `.claude/worktrees/` that git no longer lists as worktrees are removed when empty
and reported otherwise.

Dry run by default; pass `--apply` to delete.
"""

import argparse
import os
import subprocess
import sys

PREFIX = "refs/heads/claude/"


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


def is_ancestor(commit, of):
    return git("merge-base", "--is-ancestor", commit, of, check=False).returncode == 0


def worktrees():
    """Return the worktree records; the first is the main checkout."""
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
    return records


def merged_into(sha, targets):
    containing = [t for t, t_sha in targets.items() if is_ancestor(sha, t_sha)]
    return ", ".join(t.removeprefix("refs/heads/").removeprefix("refs/remotes/") for t in containing)


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


def orphan_dirs(main_path, records):
    """Directories under .claude/worktrees/ that git does not list as worktrees."""
    root = os.path.normpath(os.path.join(main_path, ".claude", "worktrees"))
    if not os.path.isdir(root):
        return []
    registered = {norm_path(r["worktree"]) for r in records}
    return sorted(
        path for path in (os.path.join(root, name) for name in os.listdir(root))
        if os.path.isdir(path) and not os.path.islink(path) and norm_path(path) not in registered
    )


def remove_empty_dir(path):
    try:
        os.rmdir(path)
        return None
    except OSError as e:
        return str(e)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="delete; without it, only report")
    parser.add_argument("--into", action="append", metavar="BRANCH",
                        help="count only merges into this branch (repeatable); default: every non-claude branch")
    args = parser.parse_args()

    try:
        here = norm_path(git("rev-parse", "--show-toplevel").stdout.strip())
        targets = target_refs(args.into)
        branches = list_refs(PREFIX.rstrip("/"))
        records = worktrees()
    except RuntimeError as e:
        sys.exit(str(e))
    trees = {r["branch"]: r for r in records if "branch" in r}
    main_path = records[0]["worktree"]

    to_delete, kept = [], []
    for ref, sha in sorted(branches.items()):
        name = ref.removeprefix("refs/heads/")
        into = merged_into(sha, targets)
        if not into:
            kept.append((name, "not merged into any target"))
            continue
        reason = f"tip is on {into}"
        record = trees.get(ref)
        blocker = record and worktree_blocker(record, here)
        if blocker:
            kept.append((name, f"{reason}, but {blocker}"))
            continue
        to_delete.append((name, record, reason))

    orphans = orphan_dirs(main_path, records)
    empty_orphans = [p for p in orphans if not os.listdir(p)]
    full_orphans = [p for p in orphans if p not in empty_orphans]

    for name, reason in kept:
        print(f"keep    {name}: {reason}")
    for path in full_orphans:
        print(f"keep    {path}: not a git worktree, but not empty; inspect it by hand")
    for name, record, reason in to_delete:
        where = f" + worktree {record['worktree']}" if record else ""
        print(f"delete  {name}{where}: {reason}")
    for path in empty_orphans:
        print(f"delete  {path}: empty leftover directory, not a git worktree")

    if not to_delete and not empty_orphans:
        print("Nothing to delete.")
        return
    if not args.apply:
        print("\nDry run; pass --apply to delete.")
        return

    failures = 0
    git("worktree", "prune", check=False)
    for name, record, _ in to_delete:
        path = record and record["worktree"]
        if path and os.path.isdir(path):
            result = git("worktree", "remove", path, check=False)
            if result.returncode != 0:
                print(f"FAILED  {name}: worktree remove: {result.stderr.strip()}")
                failures += 1
                continue
            # On Windows `git worktree remove` can unregister the worktree yet leave an empty directory.
            if os.path.isdir(path) and not os.listdir(path):
                remove_empty_dir(path)
        result = git("branch", "-D", name, check=False)
        if result.returncode != 0:
            print(f"FAILED  {name}: branch -D: {result.stderr.strip()}")
            failures += 1
            continue
        print(f"deleted {name}")
    for path in empty_orphans:
        error = remove_empty_dir(path)
        if error:
            print(f"FAILED  {path}: {error}")
            failures += 1
        else:
            print(f"deleted {path}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
