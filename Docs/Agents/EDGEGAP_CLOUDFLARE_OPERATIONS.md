# Edgegap + Cloudflare Operations and Agent Handoff

Last verified against the repositories on 2026-08-11. This is the maintenance runbook for the single-server Compersion deployment. Treat live dashboard state as authoritative when it differs from this snapshot.

## What Lives Where

- Unity/server image repository: `/Users/cfire/Desktop/devvit-unity-project`
  - `Server/Dockerfile` is the source Dockerfile.
  - `Server/start.sh` starts the tunnel and Unity server.
  - `update-edgegap-dockerfile.sh` installs the source Dockerfile into the Edgegap Unity plugin cache and patches the plugin's post-upload browser destination.
- Homepage/Worker repository: `/Users/cfire/Desktop/ramborngames.github.io`
  - `index.html` sends the visitor wake request and polls visible startup state without a refresh.
  - `edgegap-watchdog/` contains the Cloudflare Worker, Durable Object, tests, and Wrangler configuration.

Do not infer live Cloudflare, Edgegap, tunnel, or deployment state from Git alone. Verify it in the dashboards or through authenticated APIs without printing secret values.

## Non-Negotiable Architecture Invariants

1. There is one production Edgegap deployment for the `compersion` application. All clients share it and all versions share one Cloudflare Tunnel.
2. Never start a replacement until Edgegap confirms the old deployment is `TERMINATED`/`STOPPED` or returns a terminal 404/410. An `ERROR` status alone does not prove the old process is gone.
3. The Worker must reconcile all live versions of the application, not only the configured target version. A manual or older-version deployment is still a split-brain conflict.
4. A non-idempotent create attempt gets a unique `wd-*` tag and a durable pending-attempt reservation before the API call. An ambiguous outcome opens the circuit; it is not blindly retried.
5. Circuit-open state stops Durable Object alarms. Multiple live deployments, uncertain stop/create outcomes, deterministic configuration/authentication errors, repeated status errors, timeouts, and hourly/daily caps require manual review.
6. A page visit begins checking. After the first failed check, Durable Object alarms continue once per minute until healthy or circuit-open. This continuation is intentional: visitors do not need to keep the page open, but an incident is not initiated by a cron schedule.
7. Do not rename the Durable Object singleton (`compersion-primary-v4`) casually. A new name creates fresh state and can forget a live incident or deployment reservation. Reconcile Edgegap first if a state reset or namespace change is necessary.
8. Do not run a second connector for the same remotely managed Cloudflare Tunnel on a developer machine or another host. Multiple connectors can load-balance traffic and violate the single-server assumption.

The current configured guardrails are three failed health checks, a 60-second minimum check interval, a 15-minute deployment cooldown, five minutes to confirm stop, ten minutes for replacement readiness, and caps of three attempts per hour and six per day. Read `edgegap-watchdog/wrangler.jsonc` before relying on these values; it is the source of truth.

The service was deliberately parked on 2026-08-11 with the live Worker binding `ENABLE_DEPLOYMENTS=false` and zero live Edgegap deployments. Read `Docs/Agents/EDGEGAP_INCIDENT_2026-08-11.md` before resuming or re-enabling deployment creation.

## Secret Boundaries

- `EDGEGAP_TOKEN` and `ADMIN_TOKEN` are Cloudflare Worker secrets. They must never appear in `wrangler.jsonc`, browser JavaScript, Git, logs, screenshots, shell history, or documentation.
- `CF_TUNNEL_TOKEN` is a hidden environment variable on the stable Edgegap version. It must never be copied into the Docker image, Unity project settings, repository, or Worker.
- `Server/start.sh` writes the tunnel token to a mode-0600 runtime file owned by the unprivileged `tunnel` account, unsets the environment variable, and launches `cloudflared` with `--token-file`. Unity runs as the separate `gameserver` account and must not inherit the token.
- `Assets/EdgegapSettings.asset`, `Server/cloudflare-credentials.json`, and `Server/cloudflare-tunnel.yml` must remain untracked/absent. Before committing, search tracked text for credential filenames and likely token markers; inspect matches without printing secret contents.
- Rotate a credential immediately if it was committed, pasted into public UI/source, included in an image layer, or displayed in captured output. Updating Git history without revoking the credential is insufficient.

Safe verification examples:

```sh
git ls-files Assets/EdgegapSettings.asset Server/cloudflare-credentials.json Server/cloudflare-tunnel.yml
rg -n 'cloudflare-credentials|cloudflare-tunnel\.yml|releases/latest' Server/Dockerfile update-edgegap-dockerfile.sh
```

The first command should print nothing. The second may match the updater's deliberate rejection checks, but must not reveal a token.

## Stable Edgegap Version Workflow

The stable Edgegap version is `26.08.11-watchdog-secure`. Its date records the secured watchdog/tunnel profile baseline created on 2026-08-11; it is not the Unity image build date. The hidden `CF_TUNNEL_TOKEN` is configured on this version once. New server builds update this version's immutable, date-bearing Docker tag; they do not create a new Edgegap version. The Worker continues to request the stable version name, so normal image updates need no Worker configuration change. Renaming it is a coordinated profile migration requiring a new secret-bearing version and Worker target change, not a routine build step.

Normal upload flow:

1. Open Unity and allow package import to finish.
2. From the project root, run `./update-edgegap-dockerfile.sh`.
3. Confirm the script says the secured Dockerfile was installed and names the stable version.
4. In `Tools -> Edgegap Server Hosting`, run **Build**, **Containerize**, then click **Upload image and Create app version**. Despite that stock label, the updater patches the post-upload browser destination; it uploads the image and opens the existing stable version rather than asking the operator to create one.
5. After upload, Chrome should open the stable version's details page, not a create-version page.
6. Select the newly uploaded Docker tag on that existing stable version and click **Save**. Do not create a fresh version and do not paste the tunnel secret again.
7. Let the visitor-triggered watchdog start the next deployment when multiplayer is needed. If a live server already exists, changing its version configuration does not mutate the running container.

UI automation must locate the exact accessible label **Upload image and Create app version**, invoke it only once after successful containerization, and assert that Chrome opens the `26.08.11-watchdog-secure` details URL. A create-version URL is a hard stop, not a page to complete.

The Unity plugin cache under `Library/PackageCache` is disposable and untracked. `update-edgegap-dockerfile.sh` dynamically requires exactly one `com.edgegap.unity-servers-plugin@*/Editor` directory, copies `Server/Dockerfile` there, and applies an exact source patch to `EdgegapWindowV2.cs`. It verifies both results and intentionally fails if package source drift makes the patch ambiguous. Edit only the source Dockerfile/script, never the cached copies. Re-run the updater after deleting `Library`, changing Unity/package versions, or reimporting packages.

If the updater fails:

- Zero plugin caches: open Unity, wait for package restoration, then retry.
- Multiple plugin caches: close Unity and resolve the stale/duplicate package cache rather than choosing one arbitrarily.
- “upload flow changed”: inspect the new plugin's upload callback and update the narrowly scoped regex only after confirming the post-upload semantics.
- Legacy credential/mutable-download rejection: remove credential copies or unpinned downloads from the source Dockerfile; do not weaken the check.

## Visitor Wake and Visible Status

The homepage sends `POST https://compersion.charliefeuerborn.com/watchdog/wake` only when the page loads. It independently probes `wss://compersion.charliefeuerborn.com`. When wake is accepted and the socket is unavailable, the page polls `/watchdog/status` every 10 seconds initially, then every 20 seconds, for up to 11 minutes. Visitors can see wake sent/checking, stopping, booting, operational, or startup-needs-attention without refreshing.

Important limits:

- CORS restricts browser origins but is not authentication. Rate-control both `/watchdog/wake` and `/watchdog/status`, while allowing the documented 10-second status polling cadence.
- The public WebSocket handshake is the intentional health contract, matching website commit `763e6fb`. It proves the tunnel/Bayou listener accepts a connection without requiring a Unity HTTP readiness endpoint. It does not prove game-state correctness, so retain the real game-client smoke test.
- The homepage's first label is based on wake acceptance plus its own socket probe. Durable state from `/watchdog/status` becomes authoritative during follow-up.
- Hidden/background tabs skip UI polling iterations, but the Durable Object alarms continue server-side.

## Pre-Upload and Post-Upload Test Checklist

Run from the Unity repository:

```sh
./update-edgegap-dockerfile.sh
bash -n Server/start.sh
cmp -s Server/Dockerfile Library/PackageCache/com.edgegap.unity-servers-plugin@*/Editor/Dockerfile
```

Then verify the cache patch contains the stable version details URL. Build for `linux/amd64`; success under Apple Silicon emulation is useful but a Mono/emulation failure is not by itself proof that the native Edgegap runtime will fail.

Run from the homepage repository:

```sh
cd edgegap-watchdog
npm test
npx wrangler deploy --dry-run
```

After upload/configuration, verify in this order:

1. Edgegap stable version points to the intended new image tag and still shows hidden `CF_TUNNEL_TOKEN` without exposing its value.
2. No more than one live deployment exists across all `compersion` versions.
3. A homepage visit produces a successful wake request and visible state transitions without refresh.
4. Edgegap reaches `READY`; the Cloudflare Tunnel connector becomes healthy; the public WebSocket probe succeeds; the page shows Operational.
5. The deployment retains the intended 24-hour maximum duration.
6. Worker logs contain no credential values and show no duplicate create attempt for the incident.

Never perform a destructive outage test against active players without explicit approval. The current helper unit tests cover only deployment gating and status normalization. They do not establish full Durable Object state-machine behavior or prove live Edgegap API response shapes, pagination, dashboard configuration, tunnel routing, or Unity readiness.

## Failure Triage

| Symptom | Likely cause | Safe first checks |
|---|---|---|
| **Upload image and Create app version** opens “create version” | Plugin cache was rebuilt or updater patch drifted | Re-run updater; verify exactly one cache and stable details URL. Do not complete the create form. |
| Stable version asks for tunnel secret again | A new Edgegap version was created instead of updating the stable version | Stop before pasting; return to `26.08.11-watchdog-secure` and update only its Docker tag. |
| Edgegap is READY, tunnel is Down | Missing/invalid hidden token, old image digest, `cloudflared` startup failure, or a second connector conflict | Check version secret presence, selected tag/digest, container logs, and Cloudflare connector list without revealing the token. |
| Tunnel is healthy, WebSocket is down | Published hostname/service mapping, Bayou listener on localhost:7771, Unity startup, or protocol mismatch | Check Cloudflare public-hostname route and Unity/Bayou logs. |
| Unity cannot write config | Wrong runtime identity or HOME/XDG paths | Confirm it runs as `gameserver` with writable `/var/lib/compersion` and `XDG_CONFIG_HOME`. |
| “Startup needs attention” | Circuit opened, replacement timed out/failed, API errors repeated, or live deployments are ambiguous | Read authenticated Worker state and logs; list all live app deployments before any reset. |
| Multiple live deployments | Manual deploy, stale older version, or ambiguous create outcome | Do not reset/retry. Identify request IDs and tunnel connectors, then intentionally terminate extras with approval. |
| Watchdog never runs | No page visit, route/DNS mismatch, bad origin, Worker deployment missing, or request rate-limited | Inspect browser Network for `/watchdog/wake`, Worker route, `PUBLIC_ORIGIN`, and logs. |
| Repeated boots | Health URL never succeeds, singleton state was reset/renamed, caps changed, or manual deployments evade expected app identification | Disable deployments first, preserve state/logs, reconcile all live deployments, then diagnose health routing. |

## Manual Recovery Rules

1. If deployment behavior is uncertain, set `ENABLE_DEPLOYMENTS` to `false` and deploy that Worker configuration before changing state.
2. Inspect authenticated `/admin/status`, Worker logs, and all live Edgegap deployments. Do not include the bearer token in URLs or screenshots.
3. Reconcile `pendingAttemptId`, replacement ID, stopping ID, live deployment IDs, and attempt tags. Preserve evidence before resetting anything.
4. `/admin/reset` deletes Durable Object state and its alarm. It does not stop Edgegap deployments. Never reset until Edgegap is reconciled, because doing so can erase the only durable record of an ambiguous create request.
5. After correcting the cause, confirm zero or one live deployment, restore the intended stable version/image, re-enable deployments if appropriate, and use one controlled visitor wake.

## Truth and Maintenance Caveats

- Dashboard configuration and credentials are intentionally not reproducible from this document. That is a security property, not missing documentation.
- The stable version name, Durable Object name, routes, image tag, timings, and plugin source can change. Verify each against code and dashboards before operating production.
- The Worker currently treats public WebSocket acceptance as health. Do not document it as proof that players can join, that the simulation is correct, or that data is durable.
- The stable Edgegap version was configured for a 24-hour maximum deployment lifetime at the last dashboard check; verify that live setting rather than treating Git as authoritative. The design starts a new instance only after a visitor-initiated incident check; once an incident starts, alarms finish recovery even if the visitor leaves.
- Keep this file synchronized with `Server/Dockerfile`, `Server/start.sh`, `update-edgegap-dockerfile.sh`, homepage status code, Worker state machine, and Wrangler variables whenever any of them changes.
