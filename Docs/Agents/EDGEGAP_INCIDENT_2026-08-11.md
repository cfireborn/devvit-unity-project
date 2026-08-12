# Edgegap Incident Handoff — 2026-08-11

This is the authoritative pickup note for the parked Compersion multiplayer service. It contains no credential values. Treat dashboards and authenticated APIs as authoritative for live state.

## Resolution recorded 2026-08-12

The tunnel portion of this incident is resolved. The previous tunnel was locally managed while the Edgegap container launched `cloudflared` with `--token-file`, so the connector could become healthy without receiving an ingress route. Production now uses remotely managed tunnel `compersion-edgegap-prod` (ID `6fd08db4-935d-4c7b-b2e0-6424f17bd771`) with a published application mapping `compersion.charliefeuerborn.com` to `http://localhost:7771`. Production DNS points to the new tunnel, and the retired tunnel was deleted.

The hidden Edgegap `CF_TUNNEL_TOKEN` was replaced without recording its value. Exactly one controlled deployment, `77db03e3878e`, was launched from stable version `26.08.11-watchdog-secure`; Edgegap reported Ready, Cloudflare reported one Healthy connector, `cloudflared` logged the remotely delivered ingress configuration, an external HTTP/1.1 WebSocket upgrade returned 101, and the operator confirmed the real game-client test worked. The initial `No ingress rules were defined` startup warning was transient and occurred before the dashboard route reached the connector.

The parked state described below is incident history. On 2026-08-12, after the tunnel fix and an application-wide reconciliation confirmed zero live deployments, Worker version `1fec72e5-a4e1-4b36-9b3f-2592bd8e1c37` was deployed with `ENABLE_DEPLOYMENTS=true`. Its one-time config generation clears the old parked circuit only when the next homepage visitor sends a wake. The watchdog creates only from zero live deployments and never automatically stops or restarts an existing deployment.

## Safe parked state

Verified before handoff on 2026-08-11:

- The production Cloudflare Worker was deployed with `ENABLE_DEPLOYMENTS=false`. Homepage visits cannot create Edgegap deployments while that live binding remains false.
- The Edgegap deployment list showed every listed `compersion` deployment as terminal (`N/A` time remaining). The remaining Dallas deployment was intentionally terminated.
- The Worker's origin-restricted diagnostic returned `listedCount: 0`, `liveCount: 0`, and `reason: no-live-deployment` after termination.
- The Cloudflare Tunnel token was rotated in Cloudflare, but the replacement value was **not** entered into Edgegap. The hidden `CF_TUNNEL_TOKEN` on stable version `26.08.11-watchdog-secure` is therefore expected to be stale/invalid until a human replaces it.
- Do not launch a server until the Edgegap secret has been replaced. Do not re-enable the watchdog merely to test the current secret.

The uploaded immutable image tag intended for the next controlled test is `26.08.11-04.35.47-UTC`. The Edgegap version remains `26.08.11-watchdog-secure`; its date identifies the secured watchdog/tunnel profile baseline, not the current Unity build. Selecting a newer date-bearing image tag on that version affects future deployments only.

## Incident finding

Edgegap reported the Dallas container as running, but the homepage and game could not connect because the Cloudflare Tunnel connector was not healthy. The Worker's sanitized diagnostic classified the container logs as `tunnel-auth-failed`. No raw logs, tokens, or credential material were exposed publicly or written here.

### Controlled recovery test later on 2026-08-11

The rotated tunnel token was successfully saved as hidden `CF_TUNNEL_TOKEN` on the stable Edgegap version. A controlled homepage test was then run:

1. Worker deployment creation was enabled only after both Edgegap and the diagnostic reported zero live deployments.
2. The homepage transitioned without refresh from **Wake request sent** to **Checking server** to **Server booting**.
3. Exactly one watchdog-tagged Chicago deployment was created. No duplicate appeared.
4. Cloudflare changed from Down to Healthy, proving that the replacement tunnel token and connector startup worked.
5. The public origin continued returning HTTP 503 and a direct `wss://compersion.charliefeuerborn.com` handshake failed. The homepage therefore correctly remained **Server booting**.
6. The failed deployment was terminated, the diagnostic returned `listedCount: 0` and `liveCount: 0`, and the production Worker was redeployed with `ENABLE_DEPLOYMENTS=false`.

The remaining blocker is inside the uploaded Unity/server image: Bayou was not reachable on `localhost:7771` even though the Cloudflare connector was healthy. The exact uploaded image also hit a Unity Mono assertion when run locally through Apple-Silicon x86 emulation; that local emulation failure is useful evidence but does not prove the native Edgegap amd64 host failed for the same reason. Obtain native Edgegap container logs during the next controlled launch and do not re-enable automatic creation until Bayou is verified.

The watchdog cold-start interval was shortened from 60 seconds to 5 seconds, retaining three serialized failures and app-wide live-deployment reconciliation before any create call. This should begin a future cold launch in roughly 10–15 seconds without allowing concurrent creates. Edgegap enum-style lifecycle status normalization was also broadened, but the observed live response still produced `edgegap-status-unknown`; response-shape handling remains an explicit blocker to validate before the next launch.

During diagnosis, the Worker was hardened to:

- preserve and reschedule Durable Object alarms when repeated visitor wake requests arrive;
- recursively normalize real Edgegap status payloads;
- hydrate live deployment list entries through their status endpoints before applying the application-wide singleton filter;
- expose only redacted deployment counts and a classified reason through the origin-restricted diagnostic;
- show contact/support help during wake, checking, restarting, booting, and error states; and
- recognize `operational` consistently in the homepage UI.

Seven helper tests passed before the parked Worker deployment. These tests do not replace a live game-client smoke test or prove complete Edgegap pagination behavior.

## Resume in this order

1. Verify the production Worker still shows `ENABLE_DEPLOYMENTS=false`. If not, disable it before doing anything else.
2. Verify Edgegap has zero live deployments across **all** versions of the `compersion` application. Stop if the result is ambiguous.
3. In Cloudflare Zero Trust, open the remotely managed `compersion` tunnel and copy its current token. Do not display, log, screenshot, or save it in a repository.
4. Confirm the hidden `CF_TUNNEL_TOKEN` remains present on existing Edgegap version `26.08.11-watchdog-secure`. It was replaced successfully during the controlled test; never reveal its value.
5. Confirm that version selects immutable image tag `26.08.11-04.35.47-UTC` and retains the 24-hour deployment lifetime.
6. First correct and upload a server image whose native Edgegap logs show Bayou listening on `localhost:7771`. Then launch exactly one controlled deployment. Prefer a single manual launch while the Worker remains disabled, so a failed test cannot cause automatic replacements.
7. Verify, in order: exactly one Edgegap deployment; Edgegap `READY`; one healthy Cloudflare connector; a successful public WSS handshake; homepage `Operational`; and an actual WebGL multiplayer connection.
8. If any check fails, terminate the controlled deployment, confirm zero live deployments, leave `ENABLE_DEPLOYMENTS=false`, and record the sanitized failure. Do not retry blindly.
9. Only after the full test passes, deliberately set `ENABLE_DEPLOYMENTS=true`, run tests, deploy the Worker, and confirm the live binding. Then use one visitor wake test after the controlled deployment has ended.

## Critical safety rules

- The Durable Object singleton is `compersion-primary-v4`. Renaming it creates fresh state; never rename or reset it until live Edgegap state is reconciled.
- Never start a replacement while another deployment may still be live. `ERROR` alone is not proof of termination.
- Do not repeatedly click Deploy, Wake, Save, or the plugin upload action. Non-idempotent creates require one durable attempt and one immutable `wd-*` tag.
- The Edgegap list response observed during this incident omitted application/version fields, so the Worker hydrates live candidates via status. API pagination/completeness is still a known risk; dashboard verification remains mandatory before re-enabling automation.
- Do not paste `EDGEGAP_TOKEN`, `ADMIN_TOKEN`, or `CF_TUNNEL_TOKEN` into source, terminal commands, URLs, screenshots, documentation, chat, or browser JavaScript.
- Stop immediately on any authentication error. Do not rotate, retry, reset, or weaken access controls without the human operator present.

## Relevant files

- Human runbook: `Docs/EDGEGAP_SERVER_OPERATIONS.md`
- Agent invariants: `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md`
- Docker/update flow: `Server/Dockerfile`, `Server/start.sh`, `update-edgegap-dockerfile.sh`
- Website Worker: sibling repository `ramborngames.github.io/edgegap-watchdog/`
- Homepage UI: sibling repository `ramborngames.github.io/index.html`

When resuming, record the dashboard time, selected image tag, live deployment count, connector health, and smoke-test result. Record no secrets.
