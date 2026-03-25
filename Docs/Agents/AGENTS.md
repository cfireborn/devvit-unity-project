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

See `AGENTS-MOSTRECENT.md` in this same folder for the in-depth architecture brief, deployment runbooks, and current priorities. Read it end-to-end before touching multiplayer code or build tooling.

---

## Runtime Modes & Architecture Quick Reference

- **Three modes always matter**: host (server+client), remote client, and offline fallback triggered by `NetworkBootstrapper` after `connectionTimeoutSeconds`. Every NetworkBehaviour must either use cached `_serverRunning/_clientRunning` flags or supply an `ActivateOfflineMode()` path so the scene survives when FishNet shuts down.
- **Wrapper pattern**: gameplay scripts live in `Assets/Scripts/Game|Player|Environment`. Network wrappers in `Assets/Scripts/Network` disable those components in `Awake()`, re-enable on the server in `OnStartServer()`, and provide offline delegates. Follow the existing `NetworkCloudManager` / `NetworkCloudLadderController` template.
- **FishNet transports**: Multipass hosts Tugboat (UDP, editor/standalone) and Bayou (WebGL via WebSocket). `NetworkBootstrapper` selects which sub-transport to use per build target and exposes `edgegap*` inspector fields that the in-game Admin Menu can override at runtime.
- **Testing loop**: Main Unity editor window runs as host, Multiplayer Play Mode (MPPM) virtual players spawn as pure clients, and offline mode can be forced by toggling `AdminMenuPrefs.AttemptConnection`/`UseLocalOverride` or by disconnecting the server.

---

## Edgegap & Hosting Snapshot

- **Deployment target**: Linux dedicated server built via the Edgegap Unity plugin (`Tools → Edgegap Server Hosting`). Build → Containerize → Upload → Deploy writes binaries to `Builds/EdgegapServer/` and bakes them into a Docker image.
- **Networking split**: Tugboat UDP clients connect directly to the hostname Edgegap assigns per deployment (update `edgegapTugboatAddress` + port in `NetworkBootstrapper`). WebGL/Bayou clients stay on the stable Cloudflare Tunnel domain `compersion.charliefeuerborn.com:443`.
- **Cloudflare tunnel**: `Server/cloudflare-credentials.json` (gitignored) plus `Server/cloudflare-tunnel.yml` instruct `cloudflared`—started by `Server/start.sh`—to forward wss:// traffic to the Bayou transport inside the container (ws://localhost:7771). Keep a copy of the credentials in `~/.cloudflared/` and refresh them if the tunnel is recreated.
- **Server folder**: `Server/Dockerfile` installs `cloudflared`, copies tunnel config + start script, exposes `7777/udp`, and runs the headless build as PID 1. Legacy helpers (`nginx.conf`, `stunnel.conf`) are unused now that Cloudflare handles TLS; keep them only for reference.

---

## Linux Server Build & Deployment Checklist

1. **Prereqs**: Unity 6.0.0f2+, FishNet 4.6.22 already imported, Docker Desktop running, Edgegap plugin logged in, and tunnel credentials placed at `Server/cloudflare-credentials.json`.
2. **Build**: In Unity, open `Tools → Edgegap Server Hosting` and click **Build** to emit `ServerBuild` under `Builds/EdgegapServer/`.
3. **Containerize**: Click **Containerize**—Edgegap's plugin copies `Server/Dockerfile` into its package cache and runs `docker build`. Confirm the log shows Cloudflare binaries getting installed.
4. **Upload & Deploy**: Use **Upload** (push to Edgegap registry) followed by **Deploy**. Record the deployment hostname + UDP port and update the `NetworkBootstrapper` inspector (or Admin Menu) before playtesting.
5. **Connect**: Editor/standalone clients use Tugboat + the fresh hostname; WebGL stays pointed at the Cloudflare address. If no server responds within five seconds, offline mode automatically re-enables gameplay components.

---

## Edgegap Plugin Dockerfile Override

- Unity stores packages under `Library/PackageCache/`, so updating Unity or nuking `Library/` causes the Edgegap plugin to revert to its stock Dockerfile (no Cloudflare tunnel, no start script).
- Run `./update-edgegap-dockerfile.sh` after every package reimport. It copies `Server/Dockerfile` into `Library/PackageCache/com.edgegap.unity-servers-plugin@35356e28ab54/Editor/Dockerfile` so container builds keep launching `cloudflared` and `Server/start.sh`.
- If the plugin updates, its hash suffix (`@35356e28ab54`) will change. Update the script's `PLUGIN_DIR` constant accordingly or the copy step will fail.
- Treat the copied Dockerfile as ephemeral—**edit the source in `Server/Dockerfile` only**, then re-run the script to fan out the change.
