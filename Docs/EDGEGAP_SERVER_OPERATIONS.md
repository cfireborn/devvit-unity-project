# Compersion Server Build and Operations Runbook

This is the concise human operator runbook for the single shared Compersion multiplayer server. It covers the Unity Edgegap Server Hosting plugin, the stable Edgegap version, the Cloudflare Tunnel, and the visitor-triggered watchdog. Agents and maintainers changing the implementation must also read `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md`, which records the state-machine invariants and deeper audit checks.

## The invariant

There is one production game server and one Cloudflare Tunnel hostname:

- WebGL players connect to `wss://compersion.charliefeuerborn.com`.
- The remotely managed Cloudflare Tunnel `compersion-edgegap-prod` carries that traffic to Bayou on `http://localhost:7771` inside the Edgegap container.
- The watchdog deploys the stable Edgegap version `26.08.11-watchdog-secure`.
- A new server image is published by changing the Docker tag on that same stable version. Do not create a new Edgegap version for each build.
- A visit to the studio homepage wakes the watchdog. There is no continuously scheduled Worker check while the system is healthy.

The stable-version rule is important: the hidden tunnel secret belongs to the Edgegap version. Reusing the version preserves the secret; creating a new version does not reliably inherit it.

### Why the stable version contains `26.08.11`

`26.08.11-watchdog-secure` names the deployment-profile baseline created when the secured watchdog/tunnel architecture was introduced on 2026-08-11. It is not the Unity server build date and should not be incremented for daily builds. The selected immutable Docker image tag carries the build date instead—for example, a build uploaded on 2026-08-12 should use a tag such as `26.08.12-HH.MM.SS-UTC` while the Edgegap version remains `26.08.11-watchdog-secure`.

Keeping this version preserves its hidden `CF_TUNNEL_TOKEN`, ports, runtime policy, and the Worker's configured target. A future migration to a timeless name such as `watchdog-secure` should be treated as a coordinated configuration migration: disable automatic deployments, verify zero live deployments, create and configure the new version, transfer the secret through the dashboard, update `EDGEGAP_VERSION`, test one controlled launch, and retire the old version. Do not rename merely to match a new image date.

> **Current operational state (2026-08-12):** the tunnel-management mismatch from the 2026-08-11 incident is resolved. Production DNS points to remotely managed tunnel `compersion-edgegap-prod` (ID `6fd08db4-935d-4c7b-b2e0-6424f17bd771`), whose published application maps `compersion.charliefeuerborn.com` to `http://localhost:7771`. Controlled Edgegap deployment `77db03e3878e` reached Ready, its connector became Healthy, and a public WebSocket upgrade returned HTTP 101. The retired locally managed tunnel was deleted. After confirming zero application-wide live deployments, Worker version `1fec72e5-a4e1-4b36-9b3f-2592bd8e1c37` was deployed with `ENABLE_DEPLOYMENTS=true`. The next homepage visitor wake clears the deliberately parked state and begins the normal three-check startup flow.

## One-time credential setup

Two different credentials exist and must never be put in Git, a Docker image, a Unity asset, browser JavaScript, screenshots, or documentation:

| Credential | Stored in | Used by |
|---|---|---|
| Edgegap API token | Cloudflare Worker secret `EDGEGAP_TOKEN` | The watchdog when listing, stopping, and creating Edgegap deployments |
| Cloudflare Tunnel token | Hidden environment variable `CF_TUNNEL_TOKEN` on Edgegap version `26.08.11-watchdog-secure` | `Server/start.sh` inside the running container |

The Worker also has an `ADMIN_TOKEN` secret for protected diagnostic endpoints. It is not used by the homepage.

After rotating a credential, replace it at its storage location and revoke the old credential. Do not add either value to a local config file “temporarily.” `Assets/EdgegapSettings.asset`, `Server/cloudflare-tunnel.yml`, and credential JSON files are intentionally ignored or absent.

The tunnel token should be entered into the stable Edgegap version once. Future image uploads keep using that version, so the token does not need to be pasted again.

## Publish a new server build

### 1. Prepare the plugin cache

Open the Unity project and let package import finish. From the project root, run:

```sh
./update-edgegap-dockerfile.sh
```

Expected final output includes:

```text
Done — secured Dockerfile installed.
After upload, Chrome opens 26.08.11-watchdog-secure; select the new tag and click Save.
```

The script discovers the installed `com.edgegap.unity-servers-plugin` package instead of relying on a package hash. It must find exactly one package cache. It then:

1. rejects a source Dockerfile that references legacy credential files or an unpinned `cloudflared` download;
2. copies `Server/Dockerfile` into the plugin cache;
3. patches the plugin's post-upload browser action to open the stable Edgegap version; and
4. verifies both changes.

If the script fails, stop. Do not containerize with the plugin's stock Dockerfile. Open Unity, wait for package import, and run the script again. If it reports that the Edgegap upload flow changed, the plugin was updated and the patch must be reviewed before publishing.

Run this script after deleting `Library/`, reimporting packages, or updating the Edgegap plugin. Edit `Server/Dockerfile` only; never make a lasting edit directly under `Library/PackageCache`.

### 2. Build, containerize, and upload

1. Start Docker Desktop.
2. In Unity, open **Tools → Edgegap Server Hosting**.
3. Use **Build** to create the Linux headless build under `Builds/EdgegapServer/`.
4. Use **Containerize** and wait for the image build to complete.
5. Click **Upload image and Create app version** and wait for the registry push to complete. This is the plugin's stock button label; with our updater applied, the button uploads the image but does not lead the operator through creating a new version.
6. Chrome should open the details page for `26.08.11-watchdog-secure`.
7. On that existing version, choose the newly uploaded Docker tag and click **Save**.

Do not create a new version and do not paste `CF_TUNNEL_TOKEN` again. Do not manually deploy merely to finish an image update: the homepage/watchdog flow is responsible for starting the one production deployment when a visitor needs it.

If Chrome opens Edgegap's “create version” page instead of the stable version details page, close it without saving and rerun `./update-edgegap-dockerfile.sh`.

For UI automation, target the exact button label **Upload image and Create app version**, click it once only after Containerize succeeds, then verify the opened URL is the details page for `26.08.11-watchdog-secure`. If the browser opens any create-version page, stop the automation without entering or saving data.

## What the container does

`Server/Dockerfile` uses a pinned official `cloudflared` image, then copies its binary into Ubuntu 22.04. It creates separate unprivileged `tunnel` and `gameserver` users.

At startup, `Server/start.sh`:

1. requires `CF_TUNNEL_TOKEN` at runtime;
2. writes it to a mode-0600 file owned by the tunnel user;
3. removes the token from the script environment;
4. starts `cloudflared` using `--token-file` as the tunnel user;
5. starts Unity as the game-server user with a writable home/config directory; and
6. shuts down the surviving process if either critical process exits.

This keeps the token out of the image, Unity's environment, and command-line arguments. Root remains the small supervisor process in order to create the protected runtime file and switch users.

## Visitor-triggered startup

The homepage at `https://ramborngames.github.io/` does three things when loaded:

1. sends `POST https://compersion.charliefeuerborn.com/watchdog/wake`;
2. independently attempts a WebSocket connection to the multiplayer hostname; and
3. polls `/watchdog/status` without requiring a refresh while startup is underway.

The public wake and status routes accept only the configured homepage origin in browser CORS requests. No API or admin credential is sent to the browser.

The Worker and its Durable Object serialize all visitor wakes. A check is accepted at most once per configured minimum interval (currently five seconds), so many simultaneous visitors do not create many deployments. After the first failed public health check, Durable Object alarms continue the incident even if the visitor leaves.

With the current configuration, three failed health checks are required before deployment recovery. The watchdog then reconciles all live deployments for the Edgegap application:

- no live deployment: create one server;
- exactly one unhealthy live deployment: leave it untouched, open the circuit, and require manual review; the watchdog never automatically stops or restarts it;
- more than one live deployment: open the circuit and require manual review.

The replacement must become Edgegap `READY` and pass the public WebSocket check within 10 minutes. Startup attempt caps, a 15-minute deployment cooldown, unique attempt tags, and an ambiguity circuit breaker prevent an infinite server-creation loop.

## What visitors see

The homepage status changes without a refresh:

| Message | Meaning |
|---|---|
| **Operational** | The public WebSocket accepted a connection. |
| **Wake request sent** | The Worker durably scheduled the visitor-initiated check, but the socket is not yet available. |
| **Checking server** | The watchdog is checking health or waiting for the failure threshold. |
| **Restarting server** | Legacy state only; current policy never automatically stops or restarts an existing server. |
| **Server booting** | Edgegap is creating or starting the replacement. |
| **Startup needs attention** | The watchdog circuit is open, or the browser could not obtain a successful recovery result. |

The browser polls every 10 seconds for the first two minutes and every 20 seconds after that, up to 11 minutes. “Operational” is verified again with a direct WebSocket attempt before it is displayed.

## Verification after an image update

1. In Edgegap, verify `26.08.11-watchdog-secure` points to the new Docker tag and still shows hidden `CF_TUNNEL_TOKEN` configuration. Never reveal the value while checking.
2. Confirm there is at most one live deployment for the `compersion` application.
3. Open `https://ramborngames.github.io/` in a normal browser tab.
4. Watch the status progress without refreshing. A cold start takes multiple health intervals; **Wake request sent** is not proof that Edgegap has started yet.
5. Confirm the final state is **Operational**.
6. In Cloudflare, confirm the `compersion-edgegap-prod` tunnel has one connected connector and one published application route for `compersion.charliefeuerborn.com` to `http://localhost:7771`.
7. Open the WebGL game and confirm a multiplayer connection, not only the homepage socket probe.

The homepage probe proves that Bayou accepts a WebSocket handshake. It does not prove gameplay state, authentication, or Tugboat UDP. A real game-client smoke test is the final verification.

No separate Unity HTTP readiness endpoint is required or planned. Both the homepage and watchdog intentionally use the direct `wss://compersion.charliefeuerborn.com` handshake introduced by website commit `763e6fb`. Keep this contract unless the architecture is deliberately redesigned; do not block image publishing on implementing another endpoint.

## Failure recovery

### `update-edgegap-dockerfile.sh` cannot find one plugin

Open the project in Unity and wait for package import. If more than one matching package cache remains, close Unity and remove only the obsolete package cache after identifying the active package version. Never weaken the script to pick an arbitrary match.

### Chrome opens the wrong Edgegap page

Do not create a version. Rerun the updater and repeat **Upload image and Create app version**. A plugin update may have changed `EdgegapWindowV2.cs`; review the new plugin implementation before changing the updater's exact patch.

### Edgegap is `READY`, but Cloudflare Tunnel is disconnected

Check, in order:

1. the stable Edgegap version still has hidden `CF_TUNNEL_TOKEN`;
2. the version points to the intended image tag;
3. container logs show both `cloudflared` and the Unity server starting;
4. the remotely managed tunnel still publishes `compersion.charliefeuerborn.com` to `http://localhost:7771`; and
5. no developer machine or old deployment is also running a connector for the same tunnel.

With a remotely managed tunnel, `cloudflared` can briefly log `No ingress rules were defined` before it receives the dashboard configuration. Treat it as a real failure only if the log is not followed by `Updated to new configuration` containing the expected hostname/service mapping, or if the public WebSocket upgrade does not return HTTP 101.

Do not add a credential JSON file or bake a tunnel token into a replacement image as a shortcut.

### Homepage shows **Startup needs attention**

Inspect the Edgegap deployment list before resetting anything:

1. If more than one deployment is live, stop extras and wait until Edgegap confirms only terminal states remain.
2. If one deployment is live, inspect its status and container logs. Do not reset the watchdog while the outcome of a create or stop request is uncertain.
3. Verify the Worker secrets exist and that `EDGEGAP_VERSION` still names the stable version.
4. Verify the Cloudflare Tunnel route and connector state.
5. Only after the real-world state is reconciled to zero or one understood deployment should an authorized operator use the protected watchdog reset/check endpoint.

Resetting Durable Object state does not stop an Edgegap server. Resetting before reconciliation can hide an in-flight deployment and risk a second server.

### Credential rotation

For an Edgegap API-token rotation, replace the Worker secret, deploy/verify the Worker, then revoke the previous Edgegap token. For a Cloudflare Tunnel-token rotation, rotate the remotely managed tunnel token, replace hidden `CF_TUNNEL_TOKEN` on the stable Edgegap version, and launch a fresh deployment to verify the connector before relying on it. Never print either value in terminal logs or commit it.

## Critical files

| File | Purpose |
|---|---|
| `Server/Dockerfile` | Reproducible secured Edgegap image recipe. |
| `Server/start.sh` | Runtime secret isolation and two-process supervision. |
| `update-edgegap-dockerfile.sh` | Reapplies and verifies plugin-cache changes. |
| `Docs/EDGEGAP_SERVER_OPERATIONS.md` | Human publishing, verification, and recovery procedure. |
| `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md` | Implementation invariants and agent maintenance checks. |
| Website repo `edgegap-watchdog/src/index.js` | Worker and Durable Object singleton recovery logic. |
| Website repo `edgegap-watchdog/wrangler.jsonc` | Non-secret Worker configuration and safety limits. |
| Website repo `index.html` | Visitor wake, direct socket probe, and live status display. |

When any of these behaviors change, update this runbook in the same change.
