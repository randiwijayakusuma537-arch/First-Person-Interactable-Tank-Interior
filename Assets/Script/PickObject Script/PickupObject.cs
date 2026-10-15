using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on any object you want the player to be able to grab.
/// It works together with your existing Interactable (prompt, hold-to-interact, etc.)
/// and is picked up by PickupHolder when the Interactor triggers the interaction.
///
/// This script owns everything that belongs to the OBJECT:
/// permissions, per-object overrides, saved/restored Rigidbody state, impact sounds, events.
/// PickupHolder (on the player) owns everything that belongs to the PLAYER:
/// input, hold distance, movement, throwing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Interactable))]
public class PickupObject : MonoBehaviour
{
    [Header("Permissions")]
    [SerializeField] private bool canBePickedUp = true;
    [SerializeField] private bool canBeThrown = true;
    [SerializeField] private bool canBeRotated = true;

    [Header("Per-Object Overrides")]
    [Tooltip("If on, this object uses its own hold distance instead of the holder's default.")]
    [SerializeField] private bool overrideHoldDistance = false;
    [Min(0f)]
    [SerializeField] private float holdDistance = 2f;

    [Tooltip("Multiplies the holder's throw force for this object (0.5 = weaker, 2 = stronger).")]
    [Min(0f)]
    [SerializeField] private float throwForceMultiplier = 1f;

    [Tooltip("After being released, wait this long before the Interactable can be used again. " +
             "Prevents instantly re-grabbing when Drop key and Interact key are the same.")]
    [Min(0f)]
    [SerializeField] private float reenableInteractionDelay = 0.25f;

    [Header("Impact")]
    [Tooltip("Drop the object automatically if it hits something hard while held.")]
    [SerializeField] private bool dropOnHardImpact = false;
    [Min(0f)]
    [SerializeField] private float impactDropSpeed = 8f;

    [Tooltip("Optional. Plays a random clip from Impact Clips when the object collides.")]
    [SerializeField] private AudioSource impactAudio;
    [SerializeField] private AudioClip[] impactClips;
    [Min(0f)]
    [SerializeField] private float minImpactSpeed = 2f;
    [Min(0f)]
    [SerializeField] private float impactSpeedForMaxVolume = 10f;
    [Min(0f)]
    [SerializeField] private float impactCooldown = 0.1f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPickedUp;
    [SerializeField] private UnityEvent onDropped;
    [SerializeField] private UnityEvent onThrown;
    [SerializeField] private UnityEvent<float> onImpact;

    // C# events for other scripts.
    public event Action<PickupObject> PickedUp;
    public event Action<PickupObject> Dropped;
    public event Action<PickupObject> Thrown;
    public event Action<PickupObject, float> Impacted;

    // Public read access.
    public Rigidbody Body { get; private set; }
    public Interactable Interactable { get; private set; }
    public PickupHolder Holder { get; private set; }
    public bool IsHeld => Holder != null;

    public bool CanBePickedUp => canBePickedUp;
    public bool CanBeThrown => canBeThrown;
    public bool CanBeRotated => canBeRotated;
    public bool OverrideHoldDistance => overrideHoldDistance;
    public float HoldDistanceOverride => holdDistance;
    public float ThrowForceMultiplier => throwForceMultiplier;

    // Saved Rigidbody state, restored on release.
    private bool savedUseGravity;
    private float savedLinearDamping;
    private float savedAngularDamping;
    private RigidbodyInterpolation savedInterpolation;
    private CollisionDetectionMode savedCollisionMode;

    private Coroutine reenableRoutine;
    private float nextImpactTime;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Interactable = GetComponent<Interactable>();
    }

    private void OnDisable()
    {
        // Make sure a held object is released cleanly if it gets disabled / destroyed.
        if (IsHeld)
            Holder.Drop();
    }

    // ---------------------------------------------------------------- public API

    public void SetCanBePickedUp(bool value)
    {
        canBePickedUp = value;

        if (!value && IsHeld)
            Holder.Drop();
    }

    public void SetCanBeThrown(bool value) => canBeThrown = value;
    public void SetCanBeRotated(bool value) => canBeRotated = value;

    /// <summary>Drops this object if it is currently held. Handy for UnityEvents.</summary>
    public void ForceDrop()
    {
        if (IsHeld)
            Holder.Drop();
    }

    // ---------------------------------------------------------------- called by PickupHolder

    public void OnGrabbed(PickupHolder newHolder, float heldAngularDamping)
    {
        if (IsHeld)
            return;

        Holder = newHolder;

        if (reenableRoutine != null)
        {
            StopCoroutine(reenableRoutine);
            reenableRoutine = null;
        }

        savedUseGravity = Body.useGravity;
        savedLinearDamping = Body.GetLinearDamping();
        savedAngularDamping = Body.GetAngularDamping();
        savedInterpolation = Body.interpolation;
        savedCollisionMode = Body.collisionDetectionMode;

        Body.useGravity = false;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.SetAngularDamping(heldAngularDamping);

        // Hides the prompt and stops the Interactor from targeting us while held.
        Interactable.SetInteractable(false);

        onPickedUp?.Invoke();
        PickedUp?.Invoke(this);
    }

    public void OnReleased(bool thrown)
    {
        if (!IsHeld)
            return;

        Holder = null;

        Body.useGravity = savedUseGravity;
        Body.SetLinearDamping(savedLinearDamping);
        Body.SetAngularDamping(savedAngularDamping);
        Body.interpolation = savedInterpolation;
        Body.collisionDetectionMode = savedCollisionMode;

        if (isActiveAndEnabled && reenableInteractionDelay > 0f)
            reenableRoutine = StartCoroutine(ReenableInteraction());
        else
            EnableInteraction();

        if (thrown)
        {
            onThrown?.Invoke();
            Thrown?.Invoke(this);
        }
        else
        {
            onDropped?.Invoke();
            Dropped?.Invoke(this);
        }
    }

    // ---------------------------------------------------------------- internals

    private IEnumerator ReenableInteraction()
    {
        yield return new WaitForSeconds(reenableInteractionDelay);
        reenableRoutine = null;
        EnableInteraction();
    }

    private void EnableInteraction()
    {
        if (Interactable == null)
            return;

        // Clears "hasBeenUsed" so it works even if the Interactable has Multiple Use turned off.
        Interactable.ResetInteraction();
        Interactable.SetInteractable(true);
    }

    private void OnCollisionEnter(Collision collision)
    {
        float speed = collision.relativeVelocity.magnitude;

        if (speed >= minImpactSpeed && Time.time >= nextImpactTime)
        {
            nextImpactTime = Time.time + impactCooldown;

            if (impactAudio != null && impactClips != null && impactClips.Length > 0)
            {
                AudioClip clip = impactClips[UnityEngine.Random.Range(0, impactClips.Length)];
                float volume = Mathf.Clamp01(speed / Mathf.Max(0.01f, impactSpeedForMaxVolume));
                impactAudio.PlayOneShot(clip, volume);
            }

            onImpact?.Invoke(speed);
            Impacted?.Invoke(this, speed);
        }

        if (IsHeld && dropOnHardImpact && speed >= impactDropSpeed)
            Holder.Drop();
    }
}
