# ADR 0002: Atomic writes and refuse-to-overwrite by default

**Status:** accepted · **Date:** 2026-10-10

## Context

`create`, `copy` and `combine` all produce a file. A naive implementation opens the destination and
streams into it, which leaves a truncated file if the process dies or an input turns out to be
unreadable half way through, and silently clobbers existing data.

## Decision

1. All writes go through `AtomicFile.Write`: content is written to `.<name>.<guid>.tmp` in the
   destination directory, flushed to disk, then renamed over the destination.
2. An existing destination is an error (exit code 3) unless `--force` is given.
3. `delete` refuses directories and, with `--force`, tolerates a missing file (`rm -f` semantics).

## Reasons

- `rename(2)` is atomic within a file system; readers see either the old file or the complete new
  one, never a partial write. Placing the temp file beside the destination guarantees the same
  file system.
- Because inputs are fully consumed before the rename, `combine a b a --force` is a safe in-place
  append, a property that falls out of the design rather than needing special code.
- Safe-by-default matches the expectations of a tool that may be driven by scripts; destructive
  behaviour must be requested explicitly.

## Consequences

- Each write briefly needs space for a second copy of the file.
- The destination directory must be writable even when only overwriting a file.
- Tests check for leftover `.*.tmp` files after every failure path.
