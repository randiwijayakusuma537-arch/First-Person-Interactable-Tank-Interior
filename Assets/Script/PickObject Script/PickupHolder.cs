using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the player (same object as the Interactor, or its parent / child).
/// It listens to Interactor.Interacted, and if the interacted object has a PickupObject,
/// it grabs it and carries it in front of the first-person camera using physics
/// (so the held object still collides with walls, other rigidbodies, etc.).
///
/// Controls (all changeable in the Inspector):
///   Drop key ........ drop the object
///   Throw key ....... throw the object (optionally hold to charge)
///   Rotate key ...... hold, then move the mouse to rotate the object
///   Mouse wheel ..... move the object closer / farther
/// </summary>
public class PickupHolder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found on this object / parents / children if left empty.")]
    [SerializeField] private Interactor interactor;
    [Tooltip("The first-person camera. Falls back to Camera.main.")]
    [SerializeField] private Camera playerCamera;

    [Header("General")]
    [SerializeField] private bool pickupEnabled = true;
    [Tooltip("Objects heavier than this cannot be picked up. 0 = no limit.")]
    [Min(0f)]
    [SerializeField] private float maxPickupMass = 50f;

    [Header("Input")]
    [SerializeField] private KeyCode dropKey = KeyCode.E;
    [SerializeField] private KeyCode throwKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode rotateKey = KeyCode.R;
    [SerializeField] private string scrollAxis = "Mouse ScrollWheel";
    [SerializeField] private string rotateXAxis = "Mouse X";
    [SerializeField] private string rotateYAxis = "Mouse Y";

    [Header("Hold Distance")]
    [Min(0f)]
    [SerializeField] private float holdDistance = 2.5f;
    [Min(0f)]
    [SerializeField] private float minHoldDistance = 1f;
    [Min(0f)]
    [SerializeField] private float maxHoldDistance = 4f;
    [Tooltip("How far one mouse wheel notch moves the object.")]
    [SerializeField] private float scrollSpeed = 5f;
    [Tooltip("Start holding at the distance the object was grabbed from instead of the default hold distance.")]
    [SerializeField] private bool keepGrabDistance = false;

    [Header("Movement")]
    [Tooltip("How strongly the object is pulled to the hold point. Higher = snappier.")]
    [Min(0f)]
    [SerializeField] private float followSpeed = 15f;
    [Min(0f)]
    [SerializeField] private float maxFollowSpeed = 20f;
    [Tooltip("Heavier objects follow slower.")]
    [SerializeField] private bool massAffectsSpeed = true;
    [Tooltip("Objects at or below this mass move at full speed.")]
    [Min(0.01f)]
    [SerializeField] private float referenceMass = 5f;
    [Range(0.05f, 1f)]
    [SerializeField] private float minMassSpeedMultiplier = 0.25f;
    [Tooltip("If the object gets farther than this from the hold point (stuck on a wall, etc.) it is dropped.")]
    [Min(0f)]
    [SerializeField] private float breakDistance = 2.5f;

    [Header("Rotation")]
    [Tooltip("Keeps the object's orientation fixed relative to where you look. Turn off to let it tumble freely.")]
    [SerializeField] private bool lockOrientation = true;
    [Min(0f)]
    [SerializeField] private float rotationFollowSpeed = 12f;
    [Min(0f)]
    [SerializeField] private float maxAngularSpeed = 20f;
    [Tooltip("Degrees of rotation per mouse unit while the rotate key is held.")]
    [SerializeField] private float rotateSensitivity = 5f;
    [Min(0f)]
    [SerializeField] private float heldAngularDamping = 5f;

    [Header("Collision")]
    [Tooltip("Stops the held object from being pushed through walls when you press against them.")]
    [SerializeField] private bool preventWallPenetration = true;
    [SerializeField] private LayerMask obstructionMask = ~0;
    [Min(0f)]
    [SerializeField] private float wallPadding = 0.05f;
    [Min(0f)]
    [SerializeField] private float minObstructedDistance = 0.4f;
    [Tooltip("Held object ignores the player's colliders so it can't push or launch the player.")]
    [SerializeField] private bool ignorePlayerCollision = true;
    [Min(0f)]
    [SerializeField] private float reenableCollisionDelay = 0.3f;

    [Header("Drop & Throw")]
    [Tooltip("Hold the throw key to charge, release to throw. Off = throw instantly at max force.")]
    [SerializeField] private bool chargeThrow = false;
    [Min(0f)]
    [SerializeField] private float minThrowForce = 4f;
    [Min(0f)]
    [SerializeField] private float maxThrowForce = 14f;
    [Min(0.01f)]
    [SerializeField] private float maxChargeTime = 1f;
    [Tooltip("On: force is an impulse, so heavy objects fly slower. Off: every object flies at the same speed.")]
    [SerializeField] private bool throwScalesWithMass = true;
    [Tooltip("Speed limit applied to the object when it is dropped, so it doesn't fly off.")]
    [Min(0f)]
    [SerializeField] private float maxDropVelocity = 6f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPickedUp;
    [SerializeField] private UnityEvent onDropped;
    [SerializeField] private UnityEvent onThrown;

    // C# events for other scripts.
    public event Action<PickupObject> ObjectPickedUp;
    public event Action<PickupObject> ObjectDropped;
    public event Action<PickupObject> ObjectThrown;
    public event Action<PickupObject> PickupFailed;
    /// <summary>true when rotate mode starts, false when it ends. Use it to lock your camera look.</summary>
    public event Action<bool> RotationModeChanged;

    // Public state.
    public PickupObject HeldObject => held;
    public bool IsHolding => held != null;
    public bool IsRotating { get; private set; }
    public float ThrowCharge01 { get; private set; }
    public float CurrentHoldDistance => currentHoldDistance;

    public bool PickupEnabled
    {
        get => pickupEnabled;
        set
        {
            pickupEnabled = value;
            if (!value) Drop();
        }
    }

    private PickupObject held;
    private float currentHoldDistance;
    private float heldRadius;
    private Quaternion rotationOffset = Quaternion.identity;
    private int pickupFrame = -1;
    private bool isCharging;

    private Collider[] playerColliders = Array.Empty<Collider>();
    private Collider[] heldColliders;
    private Collider[] pendingRestore;
    private Coroutine restoreRoutine;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        if (interactor == null) interactor = GetComponent<Interactor>();
        if (interactor == null) interactor = GetComponentInParent<Interactor>();
        if (interactor == null) interactor = GetComponentInChildren<Interactor>();

        if (playerCamera == null) playerCamera = Camera.main;

        if (interactor == null)
            Debug.LogWarning("PickupHolder: no Interactor found. Assign one in the Inspector.", this);
        if (playerCamera == null)
            Debug.LogWarning("PickupHolder: no camera found. Assign one in the Inspector.", this);

        playerColliders = transform.root.GetComponentsInChildren<Collider>();
    }

    private void OnEnable()
    {
        if (interactor != null)
            interactor.Interacted += HandleInteracted;
    }

    private void OnDisable()
    {
        if (interactor != null)
            interactor.Interacted -= HandleInteracted;

        Drop();
        FlushPendingRestore();
    }

    // ---------------------------------------------------------------- pickup

    private void HandleInteracted(Interactable interactable)
    {
        if (interactable == null || IsHolding)
            return;

        if (interactable.TryGetComponent(out PickupObject pickup))
            TryPickUp(pickup);
    }

    public bool CanPickUp(PickupObject target)
    {
        if (!pickupEnabled || target == null || playerCamera == null) return false;
        if (target.IsHeld || !target.CanBePickedUp) return false;

        Rigidbody rb = target.Body;
        if (rb == null || rb.isKinematic) return false;
        if (maxPickupMass > 0f && rb.mass > maxPickupMass) return false;

        return true;
    }

    public bool TryPickUp(PickupObject target)
    {
        if (IsHolding)
            return false;

        if (!CanPickUp(target))
        {
            PickupFailed?.Invoke(target);
            return false;
        }

        Rigidbody rb = target.Body;
        Transform cam = playerCamera.transform;

        held = target;
        pickupFrame = Time.frameCount;

        // Starting hold distance.
        if (target.OverrideHoldDistance)
            currentHoldDistance = target.HoldDistanceOverride;
        else if (keepGrabDistance)
            currentHoldDistance = Vector3.Distance(cam.position, rb.worldCenterOfMass);
        else
            currentHoldDistance = holdDistance;

        currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxHoldDistance);

        // Remember the object's orientation relative to where the camera is looking (yaw only).
        rotationOffset = Quaternion.Inverse(CameraYaw(cam)) * rb.rotation;

        heldRadius = CalculateRadius(target);

        FlushPendingRestore();
        heldColliders = target.GetComponentsInChildren<Collider>();
        if (ignorePlayerCollision)
            SetPlayerCollisionIgnored(heldColliders, true);

        target.OnGrabbed(this, heldAngularDamping);

        onPickedUp?.Invoke();
        ObjectPickedUp?.Invoke(target);
        return true;
    }

    // ---------------------------------------------------------------- drop / throw

    public void Drop()
    {
        Release(false, 0f);
    }

    public void Throw(float charge01 = 1f)
    {
        if (held == null)
            return;

        if (!held.CanBeThrown)
        {
            Drop();
            return;
        }

        Release(true, Mathf.Clamp01(charge01));
    }

    private void Release(bool thrown, float charge01)
    {
        if (held == null)
            return;

        PickupObject obj = held;
        Rigidbody rb = obj.Body;
        held = null;

        isCharging = false;
        ThrowCharge01 = 0f;

        if (IsRotating)
        {
            IsRotating = false;
            RotationModeChanged?.Invoke(false);
        }

        if (rb != null)
        {
            if (thrown)
            {
                float force = Mathf.Lerp(minThrowForce, maxThrowForce, charge01) * obj.ThrowForceMultiplier;
                ForceMode mode = throwScalesWithMass ? ForceMode.Impulse : ForceMode.VelocityChange;

                rb.SetLinearVelocity(Vector3.zero);
                rb.AddForce(playerCamera.transform.forward * force, mode);
            }
            else
            {
                rb.SetLinearVelocity(Vector3.ClampMagnitude(rb.GetLinearVelocity(), maxDropVelocity));
            }

            rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxAngularSpeed);
        }

        obj.OnReleased(thrown);

        ScheduleCollisionRestore();

        if (thrown)
        {
            onThrown?.Invoke();
            ObjectThrown?.Invoke(obj);
        }
        else
        {
            onDropped?.Invoke();
            ObjectDropped?.Invoke(obj);
        }
    }

    // ---------------------------------------------------------------- input (Update)

    private void Update()
    {
        if (held == null)
        {
            // The held object might have been destroyed.
            if (IsRotating || isCharging)
            {
                if (IsRotating) { IsRotating = false; RotationModeChanged?.Invoke(false); }
                isCharging = false;
                ThrowCharge01 = 0f;
            }
            return;
        }

        HandleScroll();
        HandleRotationInput();

        // Ignore keys on the exact frame we picked up, so E (interact) doesn't instantly drop.
        if (Time.frameCount == pickupFrame)
            return;

        if (Input.GetKeyDown(dropKey))
        {
            Drop();
            return;
        }

        if (!held.CanBeThrown)
            return;

        if (chargeThrow)
        {
            if (Input.GetKeyDown(throwKey))
            {
                isCharging = true;
                ThrowCharge01 = 0f;
            }

            if (isCharging && Input.GetKey(throwKey))
                ThrowCharge01 = Mathf.Clamp01(ThrowCharge01 + Time.deltaTime / maxChargeTime);

            if (isCharging && Input.GetKeyUp(throwKey))
                Throw(ThrowCharge01);
        }
        else if (Input.GetKeyDown(throwKey))
        {
            Throw(1f);
        }
    }

    private void HandleScroll()
    {
        if (string.IsNullOrEmpty(scrollAxis))
            return;

        float scroll = Input.GetAxis(scrollAxis);

        if (Mathf.Abs(scroll) > 0.0001f)
        {
            currentHoldDistance = Mathf.Clamp(
                currentHoldDistance + scroll * scrollSpeed,
                minHoldDistance,
                maxHoldDistance
            );
        }
    }

    private void HandleRotationInput()
    {
        bool wantsRotate = lockOrientation && held.CanBeRotated && Input.GetKey(rotateKey);

        if (wantsRotate != IsRotating)
        {
            IsRotating = wantsRotate;
            RotationModeChanged?.Invoke(IsRotating);
        }

        if (!IsRotating)
            return;

        Transform cam = playerCamera.transform;
        float mx = Input.GetAxis(rotateXAxis) * rotateSensitivity;
        float my = Input.GetAxis(rotateYAxis) * rotateSensitivity;

        Quaternion yaw = CameraYaw(cam);
        Quaternion world = yaw * rotationOffset;

        // Rotate around the camera's own axes so it feels natural from the player's view.
        world = Quaternion.AngleAxis(-mx, cam.up) *
                Quaternion.AngleAxis(my, cam.right) *
                world;

        rotationOffset = Quaternion.Inverse(yaw) * world;
    }

    // ---------------------------------------------------------------- movement (FixedUpdate)

    private void FixedUpdate()
    {
        if (held == null)
            return;

        Rigidbody rb = held.Body;
        if (rb == null)
            return;

        Transform cam = playerCamera.transform;

        Vector3 target = GetTargetPosition(cam);
        Vector3 toTarget = target - rb.worldCenterOfMass;

        if (breakDistance > 0f && toTarget.magnitude > breakDistance)
        {
            Drop();
            return;
        }

        float massMultiplier = 1f;
        if (massAffectsSpeed && rb.mass > referenceMass)
            massMultiplier = Mathf.Max(minMassSpeedMultiplier, referenceMass / rb.mass);

        Vector3 velocity = Vector3.ClampMagnitude(
            toTarget * followSpeed * massMultiplier,
            maxFollowSpeed * massMultiplier
        );

        rb.SetLinearVelocity(velocity);

        if (lockOrientation)
            ApplyRotation(rb, cam);
    }

    private Vector3 GetTargetPosition(Transform cam)
    {
        Vector3 origin = cam.position;
        Vector3 direction = cam.forward;
        float distance = currentHoldDistance;

        if (preventWallPenetration)
        {
            float probeRadius = Mathf.Clamp(heldRadius * 0.5f, 0.05f, 0.3f);

            int count = Physics.SphereCastNonAlloc(
                origin,
                probeRadius,
                direction,
                hitBuffer,
                distance + heldRadius,
                obstructionMask,
                QueryTriggerInteraction.Ignore
            );

            float closest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i].collider;

                if (col == null || IsHeldOrPlayer(col))
                    continue;

                if (hitBuffer[i].distance < closest)
                    closest = hitBuffer[i].distance;
            }

            if (closest < float.MaxValue)
            {
                float allowed = closest - (heldRadius - probeRadius) - wallPadding;
                distance = Mathf.Clamp(allowed, minObstructedDistance, distance);
            }
        }

        return origin + direction * distance;
    }

    private void ApplyRotation(Rigidbody rb, Transform cam)
    {
        Quaternion targetRotation = CameraYaw(cam) * rotationOffset;
        Quaternion delta = targetRotation * Quaternion.Inverse(rb.rotation);

        delta.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f)
            angle -= 360f;

        if (Mathf.Abs(angle) < 0.01f || float.IsInfinity(axis.x))
        {
            rb.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 angular = axis * (angle * Mathf.Deg2Rad * rotationFollowSpeed);
        rb.angularVelocity = Vector3.ClampMagnitude(angular, maxAngularSpeed);
    }

    // ---------------------------------------------------------------- helpers

    private static Quaternion CameraYaw(Transform cam)
    {
        return Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
    }

    private bool IsHeldOrPlayer(Collider col)
    {
        if (held != null && col.attachedRigidbody == held.Body)
            return true;

        return col.transform.root == transform.root;
    }

    private static float CalculateRadius(PickupObject target)
    {
        Collider[] cols = target.GetComponentsInChildren<Collider>();

        if (cols.Length == 0)
            return 0.25f;

        Bounds bounds = cols[0].bounds;
        for (int i = 1; i < cols.Length; i++)
            bounds.Encapsulate(cols[i].bounds);

        Vector3 e = bounds.extents;
        return Mathf.Max(0.05f, (e.x + e.y + e.z) / 3f);
    }

    private void SetPlayerCollisionIgnored(Collider[] objectColliders, bool ignore)
    {
        if (objectColliders == null)
            return;

        foreach (Collider a in objectColliders)
        {
            if (a == null) continue;

            foreach (Collider b in playerColliders)
            {
                if (b == null || a == b) continue;
                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private void ScheduleCollisionRestore()
    {
        if (!ignorePlayerCollision || heldColliders == null)
            return;

        pendingRestore = heldColliders;
        heldColliders = null;

        if (isActiveAndEnabled && reenableCollisionDelay > 0f)
            restoreRoutine = StartCoroutine(RestoreCollisionAfterDelay());
        else
            FlushPendingRestore();
    }

    private IEnumerator RestoreCollisionAfterDelay()
    {
        yield return new WaitForSeconds(reenableCollisionDelay);
        restoreRoutine = null;
        FlushPendingRestore();
    }

    private void FlushPendingRestore()
    {
        if (restoreRoutine != null)
        {
            StopCoroutine(restoreRoutine);
            restoreRoutine = null;
        }

        if (pendingRestore != null)
        {
            SetPlayerCollisionIgnored(pendingRestore, false);
            pendingRestore = null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(cam.transform.position + cam.transform.forward * holdDistance, 0.15f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            cam.transform.position + cam.transform.forward * minHoldDistance,
            cam.transform.position + cam.transform.forward * maxHoldDistance
        );
    }
}
