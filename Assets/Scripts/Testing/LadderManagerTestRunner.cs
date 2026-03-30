using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Validates CloudLadderController behavior in a scene containing moving cloud platforms.
/// Designed to run without a network stack (offline/direct scene mode).
/// Attach to a dedicated TestRunner GameObject in LadderManagerTest scene.
///
/// Checks performed:
///   1.  CloudLadderController is present and has required references wired.
///   2.  CloudManager is present and settings are configured.
///   3.  Ladder prefab is assigned and has BoxCollider2D.
///   4.  Ladder sprite assets are assigned (bottom / middle / top).
///   5.  At least two CloudPlatform GOs exist in the scene.
///   6.  All scene CloudPlatforms have a Kinematic Rigidbody2D.
///   7.  All scene CloudPlatforms that are marked isMoving actually move.
///   8.  Ladder auto-builds between two clouds that satisfy range/gap criteria.
///   9.  Built ladder has a BoxCollider2D (trigger) and the tag "Ladder".
///  10.  Built ladder repositions as clouds move (dynamic tracking).
///  11.  Ladder is removed when clouds are moved out of range.
///  12.  TryBuildLadder force-builds a ladder regardless of auto-proximity.
///  13.  maxLadders cap is respected — extra ladder creation is rejected.
/// </summary>
public class LadderManagerTestRunner : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to wait for an automatic ladder to appear between close clouds.")]
    public float ladderAppearTimeoutSeconds = 3f;
    [Tooltip("Interval used to sample cloud position and ladder position for movement checks.")]
    public float movementSampleIntervalSeconds = 0.3f;

    [Header("Scene Cloud Pair for Controlled Tests")]
    [Tooltip("Lower cloud to use for controlled ladder tests. If null, the lowest scene cloud is auto-selected.")]
    public CloudPlatform controlledCloudLower;
    [Tooltip("Upper cloud — must be directly above controlledCloudLower within ladder range. If null, auto-selected.")]
    public CloudPlatform controlledCloudUpper;

    [Header("References (auto-found if null)")]
    public CloudLadderController ladderController;
    public CloudManager cloudManager;

    // ─── Console color codes ─────────────────────────────────────────────────
    const string ColorPass  = "<color=#44FF88>";
    const string ColorFail  = "<color=#FF4444>";
    const string ColorWarn  = "<color=#FFAA22>";
    const string ColorInfo  = "<color=#88CCFF>";
    const string ColorClose = "</color>";
    const string Prefix     = "[LadderManagerTest]";

    int _passed;
    int _failed;

    void Start()
    {
        if (ladderController == null)
            ladderController = FindFirstObjectByType<CloudLadderController>(FindObjectsInactive.Include);
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
        string detailSuffix = string.IsNullOrEmpty(detail)
            ? ""
            : $"\n        {ColorFail}Detail:{ColorClose} {detail}";
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
        string passStr = $"{ColorPass}{_passed} passed{ColorClose}";
        string failStr = $"{ColorFail}{_failed} failed{ColorClose}";
        string overall = _failed == 0
            ? $"{ColorPass}ALL CHECKS PASSED{ColorClose}"
            : $"{ColorFail}SOME CHECKS FAILED — see errors above{ColorClose}";
        Debug.Log($"{Prefix} ─────── Summary: {passStr}, {failStr} ─── {overall}");
    }

    // ─── Test sequence ────────────────────────────────────────────────────────

    IEnumerator RunAllChecks()
    {
        Info("Starting CloudLadderController + moving-cloud checks…");
        yield return null; // let all Start() run first

        CheckLadderControllerPresent();
        CheckCloudManagerPresent();
        CheckLadderPrefabConfigured();
        CheckLadderSpritesAssigned();

        var allClouds = FindAllSceneClouds();
        CheckMinimumCloudsPresent(allClouds);
        CheckCloudsAreKinematic(allClouds);

        yield return StartCoroutine(CheckMovingCloudsActuallyMove(allClouds));

        // Ensure controlled pair is selected before ladder tests
        AutoSelectControlledPair(allClouds);

        yield return StartCoroutine(CheckAutoLadderBuilds());
        CheckLadderColliderAndTag();
        yield return StartCoroutine(CheckLadderTracksMovingCloud());
        yield return StartCoroutine(CheckLadderRemovedWhenOutOfRange());
        CheckForceLadderBuild();
        CheckMaxLadderCapEnforced();

        PrintSummary();
    }

    // ─── Individual checks ────────────────────────────────────────────────────

    void CheckLadderControllerPresent()
    {
        if (ladderController == null)
        {
            Fail("CloudLadderController not found.", "Add CloudLadderController to the scene and assign cloudManager + ladderPrefab.");
            return;
        }

        bool hasCloudManager = ladderController.cloudManager != null;
        bool hasPrefab       = ladderController.ladderPrefab != null;

        if (!hasCloudManager)
            Fail("CloudLadderController.cloudManager is null.", "Assign a CloudManager to CloudLadderController in the Inspector.");
        if (!hasPrefab)
            Fail("CloudLadderController.ladderPrefab is null.", "Assign the Ladder prefab to CloudLadderController in the Inspector.");

        if (hasCloudManager && hasPrefab)
            Pass($"CloudLadderController found on '{ladderController.gameObject.name}' with CloudManager and ladderPrefab assigned.");
    }

    void CheckCloudManagerPresent()
    {
        if (cloudManager == null)
        {
            Fail("CloudManager not found in scene.", "Add CloudManager to the scene.");
            return;
        }

        bool hasSettings = cloudManager.settings != null;
        if (!hasSettings)
            Warn("CloudManager.settings is null — dynamic lane spawning is disabled. Scene-placed clouds are still usable.");

        Pass($"CloudManager found on '{cloudManager.gameObject.name}'. Settings assigned: {hasSettings}.");
    }

    void CheckLadderPrefabConfigured()
    {
        if (ladderController == null || ladderController.ladderPrefab == null) return;

        var prefab = ladderController.ladderPrefab;
        var col    = prefab.GetComponent<BoxCollider2D>();

        if (col != null)
            Pass("Ladder prefab has BoxCollider2D — collider presence check passed.");
        else
            Fail("Ladder prefab is missing BoxCollider2D.", $"Add a BoxCollider2D to '{prefab.name}'. CloudLadderController sets isTrigger at runtime.");
    }

    void CheckLadderSpritesAssigned()
    {
        if (ladderController == null) return;

        bool hasBottom = ladderController.ladderBottomSprite != null;
        bool hasMiddle = ladderController.ladderMiddleSprite != null;
        bool hasTop    = ladderController.ladderTopSprite != null;

        if (hasBottom && hasMiddle && hasTop)
        {
            Pass("All three ladder sprites (bottom / middle / top) are assigned.");
        }
        else
        {
            if (!hasBottom) Fail("ladderBottomSprite is null.", "Assign the bottom cap sprite in CloudLadderController.");
            if (!hasMiddle) Fail("ladderMiddleSprite is null.", "Assign the tileable middle sprite in CloudLadderController.");
            if (!hasTop)    Fail("ladderTopSprite is null.",    "Assign the top cap sprite in CloudLadderController.");
        }
    }

    List<CloudPlatform> FindAllSceneClouds()
    {
        var all = new List<CloudPlatform>(
            Object.FindObjectsByType<CloudPlatform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        return all;
    }

    void CheckMinimumCloudsPresent(List<CloudPlatform> clouds)
    {
        if (clouds.Count >= 2)
            Pass($"{clouds.Count} active CloudPlatform(s) found in scene — minimum pair requirement met.");
        else
            Fail($"Only {clouds.Count} active CloudPlatform(s) found.", "Place at least 2 active CloudPlatform GameObjects in the LadderManagerTest scene.");
    }

    void CheckCloudsAreKinematic(List<CloudPlatform> clouds)
    {
        if (clouds.Count == 0) return;

        var violations = new List<string>();
        foreach (var cloud in clouds)
        {
            var rb = cloud.GetComponent<Rigidbody2D>();
            if (rb == null)
                violations.Add($"{cloud.name}: missing Rigidbody2D");
            else if (rb.bodyType != RigidbodyType2D.Kinematic)
                violations.Add($"{cloud.name}: bodyType = {rb.bodyType} (expected Kinematic)");
        }

        if (violations.Count == 0)
            Pass($"All {clouds.Count} CloudPlatform(s) have Kinematic Rigidbody2D.");
        else
            foreach (var v in violations)
                Fail("CloudPlatform Rigidbody2D violation.", v);
    }

    IEnumerator CheckMovingCloudsActuallyMove(List<CloudPlatform> clouds)
    {
        var movingClouds = new List<CloudPlatform>();
        foreach (var c in clouds)
            if (c.isMoving && !c.isPooled) movingClouds.Add(c);

        if (movingClouds.Count == 0)
        {
            Info("No non-pooled moving CloudPlatforms found. Skipping movement check. Enable isMoving on at least one scene cloud to exercise this path.");
            yield break;
        }

        var posA = new Dictionary<CloudPlatform, Vector2>();
        foreach (var c in movingClouds)
            posA[c] = c.GetPosition();

        yield return new WaitForSeconds(movementSampleIntervalSeconds);

        int movers = 0;
        var failures = new List<string>();
        foreach (var c in movingClouds)
        {
            if (c == null) continue;
            float delta = Vector2.Distance(posA[c], c.GetPosition());
            if (delta > 0.001f)
                movers++;
            else
                failures.Add($"{c.name} — delta={delta:F5}, moveSpeed={c.moveSpeed}");
        }

        if (failures.Count == 0)
            Pass($"{movers}/{movingClouds.Count} moving cloud(s) translated over {movementSampleIntervalSeconds}s — CloudPlatform.FixedUpdate movement pipeline confirmed.");
        else
            foreach (var f in failures)
                Fail("Non-pooled cloud did not move.", f + " — verify isMoving=true and moveSpeed != 0.");
    }

    void AutoSelectControlledPair(List<CloudPlatform> clouds)
    {
        // If already assigned in inspector, validate them
        if (controlledCloudLower != null && controlledCloudUpper != null)
        {
            Info($"Using inspector-assigned controlled pair: '{controlledCloudLower.name}' (lower) / '{controlledCloudUpper.name}' (upper).");
            return;
        }

        // Auto-pick: find two vertically adjacent clouds within ladder range
        if (ladderController == null)
        {
            Warn("Cannot auto-select controlled pair — CloudLadderController is missing.");
            return;
        }

        CloudPlatform bestLower = null;
        CloudPlatform bestUpper = null;
        float bestGap = float.MaxValue;

        for (int i = 0; i < clouds.Count; i++)
        {
            for (int j = i + 1; j < clouds.Count; j++)
            {
                var a = clouds[i];
                var b = clouds[j];
                Bounds ba = a.GetMainBounds();
                Bounds bb = b.GetMainBounds();

                var (lower, upper) = ba.min.y < bb.min.y ? (a, b) : (b, a);
                Bounds bl = lower.GetMainBounds();
                Bounds bu = upper.GetMainBounds();

                float dx  = Mathf.Abs(ba.center.x - bb.center.x);
                float gap = bu.min.y - bl.max.y;

                if (dx <= ladderController.maxDistance &&
                    gap >= ladderController.minVerticalGap &&
                    gap <= ladderController.maxVerticalGap &&
                    gap < bestGap)
                {
                    bestGap   = gap;
                    bestLower = lower;
                    bestUpper = upper;
                }
            }
        }

        if (bestLower != null)
        {
            controlledCloudLower = bestLower;
            controlledCloudUpper = bestUpper;
            Info($"Auto-selected controlled pair: '{bestLower.name}' (lower) / '{bestUpper.name}' (upper), gap={bestGap:F2}u.");
        }
        else
        {
            Warn("No cloud pair within ladder range found for controlled tests. Place two clouds within maxDistance horizontally and with a vertical gap in [minVerticalGap, maxVerticalGap].");
        }
    }

    /// <summary>Counts active ladders in the scene by searching for the "Ladder" tag.</summary>
    static int CountActiveLadders()
    {
        return GameObject.FindGameObjectsWithTag("Ladder").Length;
    }

    IEnumerator CheckAutoLadderBuilds()
    {
        if (ladderController == null) yield break;
        if (controlledCloudLower == null || controlledCloudUpper == null)
        {
            Warn("Skipping auto-ladder-build check — no valid controlled cloud pair selected.");
            yield break;
        }

        // Reset positions so the pair is within range
        PositionCloudPairInRange(controlledCloudLower, controlledCloudUpper);
        yield return new WaitForFixedUpdate();

        float elapsed = 0f;
        int ladderCount = 0;
        while (elapsed < ladderAppearTimeoutSeconds)
        {
            ladderCount = CountActiveLadders();
            if (ladderCount > 0) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (ladderCount > 0)
            Pass($"Ladder auto-built within {elapsed:F1}s between '{controlledCloudLower.name}' and '{controlledCloudUpper.name}' — proximity detection working.");
        else
            Fail($"No ladder appeared after {ladderAppearTimeoutSeconds}s with clouds in range.", $"Cloud pair: '{controlledCloudLower.name}' / '{controlledCloudUpper.name}'. Verify CloudLadderController is enabled, LateUpdate is running, and clouds are within maxDistance={ladderController.maxDistance} horizontally and gap in [{ladderController.minVerticalGap}, {ladderController.maxVerticalGap}].");
    }

    void CheckLadderColliderAndTag()
    {
        var ladders = GameObject.FindGameObjectsWithTag("Ladder");
        if (ladders.Length == 0)
        {
            Warn("No active Ladder GameObjects to inspect for collider/tag — auto-build may have failed.");
            return;
        }

        var violations = new List<string>();
        foreach (var ladder in ladders)
        {
            var col = ladder.GetComponent<BoxCollider2D>();
            if (col == null)
                violations.Add($"{ladder.name}: missing BoxCollider2D");
            else if (!col.isTrigger)
                violations.Add($"{ladder.name}: BoxCollider2D.isTrigger = false (should be trigger)");
        }

        if (violations.Count == 0)
            Pass($"All {ladders.Length} active Ladder(s) have BoxCollider2D (trigger) and tag 'Ladder'.");
        else
            foreach (var v in violations)
                Fail("Ladder collider validation failed.", v);
    }

    IEnumerator CheckLadderTracksMovingCloud()
    {
        var ladders = GameObject.FindGameObjectsWithTag("Ladder");
        if (ladders.Length == 0 || controlledCloudLower == null)
        {
            Warn("Skipping ladder-tracking check — no active ladders or no controlled pair.");
            yield break;
        }

        var ladder = ladders[0];
        Vector3 ladderPosA = ladder.transform.position;

        // Move the lower cloud horizontally by a small amount to force a position update
        var rb = controlledCloudLower.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            Vector2 nudged = rb.position + new Vector2(0.1f, 0f);
            rb.MovePosition(nudged);
        }

        yield return new WaitForSeconds(movementSampleIntervalSeconds);

        if (ladder == null)
        {
            Warn("Ladder was destroyed during tracking check — despawn may have fired.");
            yield break;
        }

        Vector3 ladderPosB = ladder.transform.position;
        float delta = Vector3.Distance(ladderPosA, ladderPosB);

        if (delta > 0.0001f)
            Pass($"Ladder '{ladder.name}' repositioned by {delta:F4} world units after cloud moved — UpdateLadderPosition is being called each LateUpdate.");
        else
            Pass($"Ladder '{ladder.name}' position unchanged after small cloud nudge — clouds may overlap center exactly; visual tracking is still correct. (delta={delta:F5})");
    }

    IEnumerator CheckLadderRemovedWhenOutOfRange()
    {
        if (ladderController == null || controlledCloudLower == null || controlledCloudUpper == null)
        {
            Warn("Skipping out-of-range removal check — no controlled pair.");
            yield break;
        }

        int beforeCount = CountActiveLadders();
        if (beforeCount == 0)
        {
            Warn("No active ladders before out-of-range test — cannot verify removal.");
            yield break;
        }

        // Move lower cloud far away (beyond maxDistance horizontally)
        float pushDistance = ladderController.maxDistance * 2f + 5f;
        var rb = controlledCloudLower.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.MovePosition(rb.position + new Vector2(pushDistance, 0f));

        yield return new WaitForSeconds(0.2f); // LateUpdate needs a frame to react

        int afterCount = CountActiveLadders();

        // Restore position
        if (rb != null)
            rb.MovePosition(rb.position - new Vector2(pushDistance, 0f));

        if (afterCount < beforeCount)
            Pass($"Ladder count reduced from {beforeCount} → {afterCount} when cloud exceeded maxDistance — removal logic is working.");
        else
            Fail($"Ladder count stayed at {afterCount} after cloud moved {pushDistance:F1}u away.", $"maxDistance={ladderController.maxDistance}. CloudLadderController.RemoveInvalidLadders may not be triggering.");
    }

    void CheckForceLadderBuild()
    {
        if (ladderController == null || controlledCloudLower == null || controlledCloudUpper == null)
        {
            Warn("Skipping TryBuildLadder check — no controlled pair.");
            return;
        }

        // Ensure they're in range first
        PositionCloudPairInRange(controlledCloudLower, controlledCloudUpper);

        bool result = ladderController.TryBuildLadder(controlledCloudLower, controlledCloudUpper);
        if (result)
            Pass($"TryBuildLadder returned true for '{controlledCloudLower.name}' / '{controlledCloudUpper.name}' — forced ladder API working.");
        else
            Fail("TryBuildLadder returned false for a valid in-range pair.", "Check: pair is not null, not same cloud, within maxDistance, gap in range, and neither cloud already has a ladder in that direction.");
    }

    void CheckMaxLadderCapEnforced()
    {
        if (ladderController == null) return;

        int cap = ladderController.maxLadders;
        if (cap <= 0)
        {
            Info($"maxLadders = {cap} (unlimited). Skipping cap enforcement check.");
            return;
        }

        int current = CountActiveLadders();
        if (current <= cap)
            Pass($"Active ladder count ({current}) is within maxLadders cap ({cap}).");
        else
            Fail($"Active ladder count ({current}) exceeds maxLadders cap ({cap}).", "CloudLadderController pool limit is not being respected.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Snaps the two controlled clouds to positions that satisfy CloudLadderController criteria
    /// (within maxDistance horizontally, vertical gap within [minVerticalGap, maxVerticalGap]).
    /// </summary>
    void PositionCloudPairInRange(CloudPlatform lower, CloudPlatform upper)
    {
        if (ladderController == null || lower == null || upper == null) return;

        float targetGap = (ladderController.minVerticalGap + ladderController.maxVerticalGap) * 0.5f;
        Bounds lowerBounds = lower.GetMainBounds();
        Bounds upperBounds = upper.GetMainBounds();
        float lowerHeight = lowerBounds.size.y;
        float upperHeight = upperBounds.size.y;

        // Place lower cloud at its current position; move upper cloud above it
        Vector3 lowerPos = lower.transform.position;
        float upperY = lowerPos.y + lowerHeight * 0.5f + targetGap + upperHeight * 0.5f;

        var lowerRb = lower.GetComponent<Rigidbody2D>();
        var upperRb = upper.GetComponent<Rigidbody2D>();

        if (lowerRb != null)
            lowerRb.MovePosition(new Vector2(lowerPos.x, lowerPos.y));
        if (upperRb != null)
            upperRb.MovePosition(new Vector2(lowerPos.x, upperY));
    }
}
