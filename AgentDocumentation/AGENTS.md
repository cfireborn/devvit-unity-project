# Agent Instructions — Compersion

## Core Principle: Minimum Effective Change

Before writing any code, ask: **what is the smallest change that correctly solves this problem?**

A one-word fix beats a one-line fix. A one-line fix beats a five-line fix. A five-line fix beats a new method. A new method beats a new class. Only escalate when the smaller option genuinely cannot work.

If you find yourself rewriting a function to fix one branch of it, stop. Fix the branch.

---

## Before You Change Anything

1. **Read the relevant files first.** Never propose changes to code you haven't read. Understand what's already there — the answer is often already present or a one-line addition away.
2. **Trace the call chain.** A bug that appears in file C was probably introduced in file A. Fix A, not C.
3. **Check for existing patterns.** This codebase has established conventions (FishNet lifecycle flags, offline mode guards, SyncVar → BufferLast RPC pattern). Match them exactly rather than inventing a new approach.

---

## Change Size Rules

**Do not add** comments, logging, error handling, or null checks that aren't directly required by the task.

**Do not refactor** surrounding code while fixing a bug. The scope of a change is the bug, not the file.

**Do not extract** a helper method unless the same logic appears in three or more places and you are asked to.

**Do not introduce** a new abstraction (interface, base class, manager, event system) unless the task explicitly requires it.

**Ask before rewriting.** If the right fix seems to require rewriting more than ~20 lines, pause and confirm with the user that the scope is correct.

---

## Architecture: Think Before You Add

Every new component, event, dictionary, and RPC has a carrying cost: it must be initialized, cleaned up, kept in sync, and understood by the next agent. Before adding one, ask:

- Does an existing FishNet feature already do this? (NetworkTransform, ObserversRpc BufferLast, Objects.Spawned, etc.)
- Can the data be derived from something already synced rather than synced independently?
- Will this still make sense in 6 months when the codebase has grown?

Prefer **deriving** state from already-synced data over **replicating** it separately. Ladder positions are derived from cloud positions — not independently synced — because clouds are already synced. That pattern was chosen deliberately.

---

## FishNet-Specific Rules

- **Never use `IsServerStarted` or `IsClientStarted` in `Update`/`FixedUpdate`** on a NetworkBehaviour that may exist in offline mode. Use cached `_serverRunning`/`_clientRunning` bool flags set in `OnStartServer`/`OnStartClient`.
- **`[SyncVar]` attribute is gone in FishNet v4.** Use `[ObserversRpc(RunLocally = true, BufferLast = true)]` for spawn-time value sync.
- **Late-joiner sync is free** when using NetworkObject + BufferLast RPCs. Do not write manual `TargetRpc` late-joiner passes unless FishNet genuinely cannot cover the case.
- **`InstanceFinder.IsServerStarted`** is safe to call statically anywhere. `NetworkBehaviour.IsServerStarted` is not safe in offline mode.

---

## What Not To Do

- Do not add a new script when a two-line addition to an existing one will work.
- Do not split a component into two components to "separate concerns" unless asked.
- Do not add `Debug.Log` calls unless actively debugging a specific reported issue, and remove them when done.
- Do not rewrite working code to match your preferred style.
- Do not leave TODO comments. Either do the thing or don't.

---

## Project Context

See `AGENTS-MOSTRECENT.md` for current architecture, file map, known gotchas, and the next phase of work. Read it before starting any multiplayer task.
