using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Shared activation rules: random chance, optional delay (<see cref="CancelActivation"/> clears pending work),
/// and single-use consumption with optional auto-disable.
/// <see cref="InteractionTrigger.TriggerNow"/> skips chance and activation delay (see subclass docs).
/// </summary>
public class ActivationTriggerBase : MonoBehaviour
{
    [Header("Activation")]
    [Tooltip("0–100. Chance activation proceeds when attempted. 100 = always.")]
    [Range(0f, 100f)]
    public float activationChancePercent = 100f;
    [Tooltip("Seconds to wait after a successful activation attempt before the activation callback runs. 0 = immediate.")]
    [Min(0f)]
    public float activationDelaySeconds = 0f;
    [Tooltip("If true, only one successful activation is allowed until reset.")]
    public bool singleUse = true;
    [Tooltip("If true, this GameObject is deactivated after a successful activation when singleUse is true.")]
    public bool autoDisableAfterUse = false;

    protected bool _used;
    protected bool _activationDelayPending;
    Coroutine _activationDelayCoroutine;

    protected bool IsBlockedBySingleUse() => _used && singleUse;

    protected bool TryRollActivationChance()
    {
        float chance = Mathf.Clamp01(activationChancePercent / 100f);
        if (chance <= 0f) return false;
        if (chance < 1f && UnityEngine.Random.value > chance) return false;
        return true;
    }

    /// <summary>Single-use, pending-delay, and chance. Call before scheduling activation work.</summary>
    protected bool TryBeginActivationPipeline()
    {
        if (IsBlockedBySingleUse()) return false;
        if (_activationDelayPending) return false;
        return TryRollActivationChance();
    }

    protected void RunDelayedOrImmediate(Action onFire)
    {
        if (activationDelaySeconds <= 0f)
        {
            onFire?.Invoke();
            return;
        }

        print("ActivationTriggerBase: RunDelayedOrImmediate: " + activationDelaySeconds);

        _activationDelayPending = true;
        if (_activationDelayCoroutine != null)
            StopCoroutine(_activationDelayCoroutine);
        _activationDelayCoroutine = StartCoroutine(CoDelayedActivation(onFire));
    }

    IEnumerator CoDelayedActivation(Action onFire)
    {
        yield return new WaitForSeconds(activationDelaySeconds);
        _activationDelayCoroutine = null;
        _activationDelayPending = false;
        onFire?.Invoke();
    }

    protected void ClearActivationPendingOnly()
    {
        _activationDelayPending = false;
        _activationDelayCoroutine = null;
    }

    protected void MarkActivationConsumedAndMaybeDisable()
    {
        _used = true;
        if (singleUse && autoDisableAfterUse)
            enabled = false;
    }

    public void CancelActivation()
    {
        if (_activationDelayCoroutine != null)
        {
            StopCoroutine(_activationDelayCoroutine);
            _activationDelayCoroutine = null;
        }
        _activationDelayPending = false;
    }

    public void ResetActivationState()
    {
        CancelActivation();
        _used = false;
    }

    public void ResetUsedState()
    {
        _used = false;
    }

    protected virtual void OnDestroy()
    {
        CancelActivation();
    }
}
