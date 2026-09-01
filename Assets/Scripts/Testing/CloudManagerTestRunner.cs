using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
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
///   5. Every cloud prefab has a valid rendered scale interval, physical collider, FishNet
///      component order, and unique server/client spawn-table round trip.
///   6. CloudManager enables after OnStartServer (checked via polling).
///   7. A FishNet player registers directly with the server CloudManager.
///   8. Player registration activates at least one lane.
///   9. CloudManager spawns at least one cloud within timeout.
///  10. Active cloud count stays at or below maxDynamicClouds cap.
///  11. All active clouds have valid Rigidbody2D and are Kinematic.
///  12. Clouds are moving (position delta observed over the active physics clock).
///  13. Networked clouds use FishNet's physics tick rather than Unity FixedUpdate.
///  14. The owner player keeps visuals off the physics root and enables interpolation.
///  15. CloudManager disables on a pure client (offline mode bypass test).
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
        CheckCloudPrefabConfigurations();

        // Give NetworkBootstrapper.Start() and FishNet OnStartServer time to run
        yield return new WaitForSeconds(0.5f);

        CheckBootstrapperStartedServer();
        yield return StartCoroutine(CheckCloudManagerEnabledOnServer());
        yield return StartCoroutine(CheckServerPlayerRegistered());
        yield return StartCoroutine(CheckPlayerActivatedLanes());
        yield return StartCoroutine(CheckCloudSpawnsWithinTimeout());
        CheckMaxCloudCapRespected();
        CheckActiveCloudsAreKinematic();
        CheckNetworkPhysicsClock();
        CheckCloudPerformanceConfiguration();
        CheckNetworkPlayerMotionConfiguration();
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

    void CheckCloudPrefabConfigurations()
    {
        if (cloudManager == null || cloudManager.cloudPrefabs == null) return;

        var violations = new List<string>();
        var prefabIds = new HashSet<int>();
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        for (int i = 0; i < cloudManager.cloudPrefabs.Length; i++)
        {
            GameObject prefab = cloudManager.cloudPrefabs[i];
            if (prefab == null)
            {
                violations.Add($"entry {i}: prefab is null");
                continue;
            }

            if (!cloudManager.TryGetPrefabScaleRange(prefab, out float minScale, out float maxScale))
            {
                Vector2 renderedSize = cloudManager.GetPrefabNativeVisualSizePublic(prefab);
                violations.Add($"{prefab.name}: invalid rendered scale range [{minScale:F3}, {maxScale:F3}] from native size {renderedSize}");
            }

            CloudPlatform platform = prefab.GetComponent<CloudPlatform>();
            int physicalColliderCount = 0;
            foreach (Collider2D collider in prefab.GetComponentsInChildren<Collider2D>(includeInactive: true))
                if (collider != null && collider.enabled && !collider.isTrigger) physicalColliderCount++;
            if (platform == null || physicalColliderCount == 0)
                violations.Add($"{prefab.name}: expected CloudPlatform and at least one enabled non-trigger collider; found platform={platform != null}, colliders={physicalColliderCount}");

            NetworkObject[] networkObjects = prefab.GetComponents<NetworkObject>();
            NetworkTransform[] networkTransforms = prefab.GetComponents<NetworkTransform>();
            NetworkCloud[] networkClouds = prefab.GetComponents<NetworkCloud>();
            if (networkObjects.Length != 1 || networkTransforms.Length != 1 || networkClouds.Length != 1)
            {
                violations.Add($"{prefab.name}: expected one NetworkObject, NetworkTransform, and NetworkCloud; found {networkObjects.Length}/{networkTransforms.Length}/{networkClouds.Length}");
                continue;
            }

            var behaviours = networkObjects[0].NetworkBehaviours;
            if (behaviours == null || behaviours.Count != 2 ||
                behaviours[0] != networkTransforms[0] || behaviours[1] != networkClouds[0])
                violations.Add($"{prefab.name}: NetworkBehaviours must be [NetworkTransform, NetworkCloud]");

            if (networkManager == null)
            {
                violations.Add($"{prefab.name}: NetworkManager unavailable for spawnable-prefab validation");
                continue;
            }

            int prefabId = networkManager.GetPrefabIndex(prefab, asServer: true);
            if (prefabId < 0 || networkObjects[0].PrefabId != prefabId ||
                networkManager.GetPrefab(prefabId, asServer: true) != networkObjects[0] ||
                networkManager.GetPrefab(prefabId, asServer: false) != networkObjects[0])
            {
                violations.Add($"{prefab.name}: FishNet prefab ID {networkObjects[0].PrefabId} does not round-trip through both spawnable-prefab tables (server index {prefabId})");
            }
            else if (!prefabIds.Add(prefabId))
            {
                violations.Add($"{prefab.name}: duplicate FishNet prefab ID {prefabId}");
            }
        }

        if (violations.Count == 0)
            Pass($"All {cloudManager.cloudPrefabs.Length} cloud prefabs have valid rendered scale ranges, physical colliders, FishNet behaviour order, and unique server/client spawn-table IDs.");
        else
            foreach (string violation in violations)
                Fail("Cloud prefab configuration violation.", violation);
    }

    void CheckBootstrapperStartedServer()
    {
        if (InstanceFinder.IsServerStarted)
            Pass("Server is started (InstanceFinder.IsServerStarted = true). NetworkBootstrapper server path confirmed.");
        else
            Fail("Server did not start.", "NetworkBootstrapper may not have reached TryStartServer, or editorStartAsHost is false.");
    }

    void CheckNetworkPhysicsClock()
    {
        TimeManager timeManager = InstanceFinder.TimeManager;
        if (cloudManager == null || timeManager == null) return;

        if (timeManager.PhysicsMode != PhysicsMode.TimeManager)
        {
            Info("FishNet PhysicsMode is Unity; CloudManager correctly retains its FixedUpdate fallback.");
            return;
        }

        if (cloudManager.UsesNetworkPhysicsClockForTests)
            Pass("CloudManager advances from FishNet OnPrePhysicsSimulation in TimeManager physics mode.");
        else
            Fail("CloudManager is not subscribed to FishNet's pre-physics simulation.", "Unity FixedUpdate and FishNet TimeManager physics must not drive clouds on different clocks.");
    }

    void CheckCloudPerformanceConfiguration()
    {
        if (cloudManager == null) return;

        FieldInfo platformClockField = typeof(CloudPlatform).GetField(
            "_subscribedTimeManager", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo intervalField = typeof(NetworkTransform).GetField(
            "_interval", BindingFlags.Instance | BindingFlags.NonPublic);
        bool pooledPlatformsUnsubscribed = true;
        bool transformIntervalHealthy = true;
        int checkedClouds = 0;

        foreach (GameObject cloud in cloudManager.GetActiveClouds())
        {
            if (cloud == null || !cloudManager.IsDynamicCloud(cloud)) continue;
            CloudPlatform platform = cloud.GetComponent<CloudPlatform>();
            NetworkTransform networkTransform = cloud.GetComponent<NetworkTransform>();
            checkedClouds++;
            if (platform != null && platformClockField != null &&
                platformClockField.GetValue(platform) != null)
                pooledPlatformsUnsubscribed = false;
            if (networkTransform != null && intervalField != null &&
                intervalField.GetValue(networkTransform) is byte interval && interval != 3)
                transformIntervalHealthy = false;
        }

        if (checkedClouds == 0)
        {
            Warn("No dynamic cloud was available for performance-configuration validation.");
            return;
        }
        if (pooledPlatformsUnsubscribed && transformIntervalHealthy)
            Pass($"{checkedClouds} dynamic clouds use one manager physics callback and 20 Hz NetworkTransform sends.");
        else
            Fail("Dynamic cloud performance configuration is invalid.",
                $"pooledPlatformsUnsubscribed={pooledPlatformsUnsubscribed}, transformIntervalHealthy={transformIntervalHealthy}");
    }

    void CheckNetworkPlayerMotionConfiguration()
    {
        NetworkPlayerSpawner spawner = FindFirstObjectByType<NetworkPlayerSpawner>(FindObjectsInactive.Include);
        GameObject prefab = spawner != null && spawner.PlayerPrefab != null ? spawner.PlayerPrefab.gameObject : null;
        if (prefab == null)
        {
            Fail("Network player prefab is unavailable for motion validation.");
            return;
        }

        PlayerControllerM controller = prefab.GetComponent<PlayerControllerM>();
        Rigidbody2D playerRb = prefab.GetComponent<Rigidbody2D>();
        NetworkTransform networkTransform = prefab.GetComponent<NetworkTransform>();
        bool dedicatedSprite = controller != null && controller.spriteRenderer != null &&
            controller.spriteTransform == controller.spriteRenderer.transform &&
            controller.spriteTransform != prefab.transform;
        bool interpolatedBody = playerRb != null && playerRb.interpolation == RigidbodyInterpolation2D.Interpolate;
        FieldInfo syncRotationField = typeof(NetworkTransform).GetField("_synchronizeRotation", BindingFlags.Instance | BindingFlags.NonPublic);
        bool rotationSyncDisabled = networkTransform != null && syncRotationField != null &&
            syncRotationField.GetValue(networkTransform) is bool synchronizeRotation && !synchronizeRotation;

        if (dedicatedSprite && interpolatedBody && rotationSyncDisabled)
            Pass("Network player tilts only its Sprite child and interpolates the owner Rigidbody2D.");
        else
            Fail("Network player motion configuration can reintroduce root jitter.", $"dedicatedSprite={dedicatedSprite}, interpolatedBody={interpolatedBody}, rotationSyncDisabled={rotationSyncDisabled}");
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
            cloudCount = CountDynamicClouds();
            if (cloudCount > 0) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cloudCount > 0)
            Pass($"CloudManager spawned {cloudCount} dynamic cloud(s) within {elapsed:F1}s — spawn pipeline is healthy.");
        else
            Fail($"No clouds appeared after {cloudSpawnTimeoutSeconds}s.", "Check: cloudPrefabs assigned, maxDynamicClouds > 0, ActiveLaneCount > 0, and the first-spawn NetworkCloudManager diagnostic.");
    }

    IEnumerator CheckServerPlayerRegistered()
    {
        if (cloudManager == null) yield break;

        float elapsed = 0f;
        while (cloudManager.RegisteredPlayerCount == 0 && elapsed < cloudSpawnTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cloudManager.RegisteredPlayerCount > 0)
            Pass($"Server CloudManager registered {cloudManager.RegisteredPlayerCount} FishNet player(s).");
        else
            Fail("Server CloudManager has no registered players.",
                "NetworkPlayerController.OnStartServer must register its transform directly; GameServices only tracks the local client player.");
    }

    IEnumerator CheckPlayerActivatedLanes()
    {
        if (cloudManager == null || cloudManager.RegisteredPlayerCount == 0) yield break;

        float elapsed = 0f;
        while (cloudManager.ActiveLaneCount == 0 && elapsed < 2f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cloudManager.ActiveLaneCount > 0)
            Pass($"Registered server player activated {cloudManager.ActiveLaneCount} cloud lane(s).");
        else
            Fail("A server player registered, but no cloud lanes activated.",
                "Check the player's server transform, fallback viewport extents, boundary clipping, and lane layout.");
    }

    void CheckMaxCloudCapRespected()
    {
        if (cloudManager == null || cloudManager.settings == null) return;

        int cap    = cloudManager.settings.maxDynamicClouds;
        int active = CountDynamicClouds();

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

    int CountDynamicClouds()
    {
        if (cloudManager == null) return 0;
        int count = 0;
        var clouds = cloudManager.GetActiveClouds();
        for (int i = 0; i < clouds.Count; i++)
            if (cloudManager.IsDynamicCloud(clouds[i])) count++;
        return count;
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
            if (c != null && cloudManager.IsDynamicCloud(c)) { target = c; break; }
        }

        if (target == null)
        {
            Warn("No dynamic cloud was available for the movement check.");
            yield break;
        }

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
            Pass($"Cloud '{target.name}' moved {delta:F4} world units over {movementSampleIntervalSeconds}s — active physics-clock MovePosition pipeline is healthy.");
        else
            Fail($"Cloud '{target.name}' did not move over {movementSampleIntervalSeconds}s.", $"Delta = {delta:F6}. Verify CloudManager is subscribed to the active physics clock and CloudBehaviorSettings.speedRange is non-zero.");
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
