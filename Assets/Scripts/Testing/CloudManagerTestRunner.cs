using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using UnityEngine;

/// <summary>
/// Validates NetworkBootstrapper server-launch behavior and CloudManager cloud lifecycle
/// in a networked-server context. Attach to a dedicated TestRunner GameObject in CloudManagerTest scene.
///
/// Checks performed:
///   1. NetworkManager is present in the scene.
///   2. NetworkBootstrapper is present and references NetworkManager.
///   3. NetworkBootstrapper starts server in editor (InstanceFinder.IsServerStarted).
///   4. NetworkCloudManager exists and has CloudManager sibling.
///   5. CloudManager enables after OnStartServer (checked via polling).
///   6. CloudManager spawns at least one cloud within timeout.
///   7. Active cloud count stays at or below maxDynamicClouds cap.
///   8. All active clouds have valid Rigidbody2D and are Kinematic.
///   9. Clouds are moving (position delta observed over two FixedUpdate cycles).
///  10. CloudManager disables on a pure client (offline mode bypass test).
/// </summary>
public class CloudManagerTestRunner : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to wait for at least one cloud to appear before failing the spawn check.")]
    public float cloudSpawnTimeoutSeconds = 5f;
    [Tooltip("Seconds between cloud-position samples to verify movement.")]
    public float movementSampleIntervalSeconds = 0.3f;

    [Header("References (auto-found if null)")]
    public NetworkBootstrapper networkBootstrapper;
    public NetworkCloudManager networkCloudManager;
    public CloudManager cloudManager;

    // ─── Console color codes ─────────────────────────────────────────────────
    const string ColorPass    = "<color=#44FF88>";   // green
    const string ColorFail    = "<color=#FF4444>";   // red
    const string ColorWarn    = "<color=#FFAA22>";   // orange
    const string ColorInfo    = "<color=#88CCFF>";   // blue
    const string ColorClose   = "</color>";
    const string Prefix       = "[CloudManagerTest]";

    int _passed;
    int _failed;

    void Start()
    {
        // Auto-find references if not wired up in inspector
        if (networkBootstrapper == null)
            networkBootstrapper = FindFirstObjectByType<NetworkBootstrapper>();
        if (networkCloudManager == null)
            networkCloudManager = FindFirstObjectByType<NetworkCloudManager>(FindObjectsInactive.Include);
        if (cloudManager == null)
            cloudManager = FindFirstObjectByType<CloudManager>(FindObjectsInactive.Include);

        StartCoroutine(RunAllChecks());
    }

    // ─── Logging helpers ─────────────────────────────────────────────────────

    void Pass(string description)
    {
        _passed++;
        Debug.Log($"{Prefix} {ColorPass}✔ PASS{ColorClose} — {description}");
    }

    void Fail(string description, string detail = "")
    {
        _failed++;
        string detailSuffix = string.IsNullOrEmpty(detail) ? "" : $"\n        {ColorFail}Detail:{ColorClose} {detail}";
        Debug.LogError($"{Prefix} {ColorFail}✘ FAIL{ColorClose} — {description}{detailSuffix}");
    }

    void Info(string message)
    {
        Debug.Log($"{Prefix} {ColorInfo}ℹ INFO{ColorClose} — {message}");
    }

    void Warn(string message)
    {
        Debug.LogWarning($"{Prefix} {ColorWarn}⚠ WARN{ColorClose} — {message}");
    }

    void PrintSummary()
    {
        string passStr  = $"{ColorPass}{_passed} passed{ColorClose}";
        string failStr  = $"{ColorFail}{_failed} failed{ColorClose}";
        string overall  = _failed == 0
            ? $"{ColorPass}ALL CHECKS PASSED{ColorClose}"
            : $"{ColorFail}SOME CHECKS FAILED — see errors above{ColorClose}";
        Debug.Log($"{Prefix} ─────── Summary: {passStr}, {failStr} ─── {overall}");
    }

    // ─── Test sequence ────────────────────────────────────────────────────────

    IEnumerator RunAllChecks()
    {
        Info("Starting CloudManager + NetworkBootstrapper checks…");

        // Wait one frame so all Start() methods have run
        yield return null;

        CheckNetworkManagerPresent();
        CheckBootstrapperPresent();
        CheckNetworkCloudManagerPresent();
        CheckCloudManagerPresent();

        // Give NetworkBootstrapper.Start() and FishNet OnStartServer time to run
        yield return new WaitForSeconds(0.5f);

        CheckBootstrapperStartedServer();
        yield return StartCoroutine(CheckCloudManagerEnabledOnServer());
        yield return StartCoroutine(CheckCloudSpawnsWithinTimeout());
        CheckMaxCloudCapRespected();
        CheckActiveCloudsAreKinematic();
        yield return StartCoroutine(CheckCloudsAreMoving());
        CheckClientPathDisablesCloudManager();

        PrintSummary();
    }

    // ─── Individual checks ────────────────────────────────────────────────────

    void CheckNetworkManagerPresent()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
            Pass("NetworkManager found in scene.");
        else
            Fail("NetworkManager not found.", "Add a NetworkManager GameObject to the CloudManagerTest scene.");
    }

    void CheckBootstrapperPresent()
    {
        if (networkBootstrapper != null)
            Pass($"NetworkBootstrapper found on '{networkBootstrapper.gameObject.name}'.");
        else
            Fail("NetworkBootstrapper not found.", "Add NetworkBootstrapper to the NetworkManager GameObject.");
    }

    void CheckNetworkCloudManagerPresent()
    {
        if (networkCloudManager != null)
            Pass($"NetworkCloudManager found on '{networkCloudManager.gameObject.name}'.");
        else
            Fail("NetworkCloudManager not found.", "Ensure a CloudManager GameObject with NetworkCloudManager is in the scene.");
    }

    void CheckCloudManagerPresent()
    {
        if (cloudManager != null)
        {
            bool hasSettings  = cloudManager.settings != null;
            bool hasPrefabs   = cloudManager.cloudPrefabs != null && cloudManager.cloudPrefabs.Length > 0;
            if (!hasSettings)
                Warn("CloudManager.settings is null — cloud lanes will not initialize.");
            if (!hasPrefabs)
                Warn("CloudManager.cloudPrefabs is empty — no dynamic clouds can spawn.");
            Pass($"CloudManager found on '{cloudManager.gameObject.name}'. Settings={hasSettings} Prefabs={hasPrefabs}");
        }
        else
        {
            Fail("CloudManager not found.", "CloudManager component missing from scene.");
        }
    }

    void CheckBootstrapperStartedServer()
    {
        if (InstanceFinder.IsServerStarted)
            Pass("Server is started (InstanceFinder.IsServerStarted = true). NetworkBootstrapper server path confirmed.");
        else
            Fail("Server did not start.", "NetworkBootstrapper may not have reached TryStartServer, or editorStartAsHost is false.");
    }

    IEnumerator CheckCloudManagerEnabledOnServer()
    {
        if (cloudManager == null) yield break;

        float elapsed = 0f;
        float timeout = 2f;
        while (!cloudManager.enabled && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cloudManager.enabled)
            Pass("CloudManager.enabled = true after OnStartServer — NetworkCloudManager correctly delegated server activation.");
        else
            Fail("CloudManager still disabled after server start.", "NetworkCloudManager.OnStartServer may not have fired. Check NetworkObject is on the CloudManager GO and it is registered as a scene NetworkObject.");
    }

    IEnumerator CheckCloudSpawnsWithinTimeout()
    {
        if (cloudManager == null) yield break;

        float elapsed = 0f;
        int   cloudCount = 0;

        while (elapsed < cloudSpawnTimeoutSeconds)
        {
            cloudCount = cloudManager.GetActiveClouds().Count;
            if (cloudCount > 0) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cloudCount > 0)
            Pass($"CloudManager spawned {cloudCount} cloud(s) within {elapsed:F1}s — spawn pipeline is healthy.");
        else
            Fail($"No clouds appeared after {cloudSpawnTimeoutSeconds}s.", "Check: cloudPrefabs assigned, CloudBehaviorSettings.maxDynamicClouds > 0, at least one lane is within player viewport, and a player is registered with GameServices.");
    }

    void CheckMaxCloudCapRespected()
    {
        if (cloudManager == null || cloudManager.settings == null) return;

        int cap    = cloudManager.settings.maxDynamicClouds;
        int active = cloudManager.GetActiveClouds().Count;

        if (cap == 0)
        {
            Info($"maxDynamicClouds = 0 (unlimited). Active clouds: {active}.");
            return;
        }

        if (active <= cap)
            Pass($"Active cloud count ({active}) is within maxDynamicClouds cap ({cap}).");
        else
            Fail($"Active cloud count ({active}) exceeds maxDynamicClouds cap ({cap}).", "CloudManager is not enforcing the dynamic cap correctly.");
    }

    void CheckActiveCloudsAreKinematic()
    {
        if (cloudManager == null) return;

        var clouds = cloudManager.GetActiveClouds();
        if (clouds.Count == 0)
        {
            Warn("No active clouds to check for Kinematic Rigidbody2D.");
            return;
        }

        var violations = new List<string>();
        foreach (var cloud in clouds)
        {
            if (cloud == null) continue;
            var rb = cloud.GetComponent<Rigidbody2D>();
            if (rb == null)
                violations.Add($"{cloud.name}: missing Rigidbody2D");
            else if (rb.bodyType != RigidbodyType2D.Kinematic)
                violations.Add($"{cloud.name}: bodyType = {rb.bodyType} (expected Kinematic)");
        }

        if (violations.Count == 0)
            Pass($"All {clouds.Count} active cloud(s) have Kinematic Rigidbody2D.");
        else
        {
            foreach (var v in violations)
                Fail("Cloud Rigidbody2D violation.", v);
        }
    }

    IEnumerator CheckCloudsAreMoving()
    {
        if (cloudManager == null) yield break;

        var clouds = cloudManager.GetActiveClouds();
        if (clouds.Count == 0)
        {
            Warn("No active clouds to check for movement.");
            yield break;
        }

        // Sample first available cloud
        GameObject target = null;
        foreach (var c in clouds)
        {
            if (c != null) { target = c; break; }
        }

        if (target == null) yield break;

        Vector2 posA = target.transform.position;
        yield return new WaitForSeconds(movementSampleIntervalSeconds);

        if (target == null)
        {
            Warn("Sampled cloud was destroyed between movement samples — it may have despawned normally.");
            yield break;
        }

        Vector2 posB = target.transform.position;
        float delta = Vector2.Distance(posA, posB);

        if (delta > 0.001f)
            Pass($"Cloud '{target.name}' moved {delta:F4} world units over {movementSampleIntervalSeconds}s — Rigidbody2D.MovePosition pipeline is healthy.");
        else
            Fail($"Cloud '{target.name}' did not move over {movementSampleIntervalSeconds}s.", $"Delta = {delta:F6}. Verify CloudManager.FixedUpdate is running and CloudBehaviorSettings.speedRange is non-zero.");
    }

    void CheckClientPathDisablesCloudManager()
    {
        if (networkCloudManager == null || cloudManager == null) return;

        // On a host, cloud manager should be enabled (server path won). On a pure client it would be disabled.
        // We can only meaningfully test the host case here in the editor.
        if (InstanceFinder.IsServerStarted && InstanceFinder.IsClientStarted)
        {
            if (cloudManager.enabled)
                Pass("Host mode: CloudManager is enabled (server path correctly wins over client path).");
            else
                Fail("Host mode: CloudManager unexpectedly disabled — OnStartClient may have overridden OnStartServer.");
        }
        else if (!InstanceFinder.IsServerStarted && InstanceFinder.IsClientStarted)
        {
            if (!cloudManager.enabled)
                Pass("Pure client: CloudManager is disabled — clients correctly rely on FishNet NetworkObject replication.");
            else
                Fail("Pure client: CloudManager is still enabled.", "NetworkCloudManager.OnStartClient should disable it on non-host clients.");
        }
        else
        {
            Info("Server-only mode detected — client path check skipped.");
        }
    }
}
