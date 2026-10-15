using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// MULTI-SEAT vehicle component (driver hatch, commander hatch, gunner, loader...).
/// Built for AdvancedFirstPersonController + Interactable + Interactor.
///
/// Put ONE of these on the tank and add as many Seats as you want. Each seat has its own:
///   - Seat Point (feet position), optional Interactable (hatch you press F on)
///   - "Switch Key": press it while seated to teleport / glide to that seat
///   - head IN / head OUT look limits, starting stance, allowed controls
///   - hatch lid (auto open when standing, close when crouching)
///   - exit points, events
///
/// REQUIRES the patch in AdvancedFirstPersonController:
///   SetMotionLocked(bool locked, Transform ignoreRoot = null, bool ignoreAllCollision = false)
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Character/Player Seat (Multi Seat Vehicle)")]
public class PlayerSeat : MonoBehaviour
{
    public enum EnterMode { InteractorKey, CustomKey }
    public enum StartingStance { KeepCurrent, Standing, Crouching }
    private enum SeatState { Empty, Transitioning, Seated }

    [Serializable] public class IntEvent : UnityEvent<int> { }

    [Serializable]
    public class ViewProfile
    {
        [Tooltip("Limit how far the player can turn left/right from the seat's forward direction.")]
        public bool limitYaw = false;
        [Tooltip("Max degrees to each side of the seat's forward direction.")]
        [Range(0f, 180f)] public float yawLimit = 120f;
        [Range(-90f, 0f)] public float minPitch = -85f;
        [Range(0f, 90f)] public float maxPitch = 85f;
    }

    [Serializable]
    public class Seat
    {
        public string seatName = "Seat";

        [Header("Placement")]
        [Tooltip("Empty object where the player's FEET go. Its forward is the seat's forward. Use an UNSCALED object.")]
        public Transform seatPoint;

        [Header("Enter (from outside, or from another seat)")]
        [Tooltip("Optional. The hatch/seat Interactable the player looks at. Leave empty if this seat is only reachable with a Switch Key.")]
        public Interactable interactable;
        [Tooltip("InteractorKey: uses the key set on your Interactor. CustomKey: uses the key below while looking at the Interactable.")]
        public EnterMode enterMode = EnterMode.InteractorKey;
        public KeyCode enterKey = KeyCode.F;
        [Tooltip("Press this WHILE SEATED in this vehicle to teleport to this seat. None = disabled.")]
        public KeyCode switchKey = KeyCode.None;
        [Tooltip("Locked seats cannot be entered or switched to.")]
        public bool locked = false;
        [Tooltip("0 = instant. Otherwise glide time when entering this seat from OUTSIDE.")]
        [Min(0f)] public float enterTransitionTime = 0f;
        [Tooltip("0 = instant teleport. Otherwise glide time when switching to this seat from ANOTHER seat.")]
        [Min(0f)] public float switchTransitionTime = 0f;
        public bool snapLookToSeat = true;
        [Range(-90f, 90f)] public float startPitch = 0f;
        public StartingStance stanceOnEnter = StartingStance.Standing;

        [Header("Leaving")]
        [Tooltip("Can the player exit the vehicle (Exit Key) from this seat?")]
        public bool canExit = true;
        [Tooltip("Can the player switch to other seats from this seat?")]
        public bool canSwitchAway = true;
        [Tooltip("Empty objects (feet positions) tried in order. First one with free space is used.")]
        public Transform[] exitPoints;
        [Tooltip("Used if no Exit Point is free. Offset in this Seat Point's local space.")]
        public Vector3 fallbackExitOffset = new Vector3(0f, 0f, 1.5f);

        [Header("Controls While In This Seat")]
        public bool allowCrouch = true;
        public bool allowProne = false;
        public bool allowZoom = true;
        public bool allowLean = true;

        [Header("View - Head OUT (standing)")]
        public ViewProfile headOutView = new ViewProfile { limitYaw = false, yawLimit = 180f, minPitch = -70f, maxPitch = 85f };

        [Header("View - Head IN (crouching)")]
        public ViewProfile headInView = new ViewProfile { limitYaw = true, yawLimit = 90f, minPitch = -60f, maxPitch = 45f };

        [Header("Hatch Lid (optional)")]
        public Transform hatchLid;
        public Vector3 lidClosedLocalEuler = Vector3.zero;
        public Vector3 lidOpenLocalEuler = new Vector3(-100f, 0f, 0f);
        [Tooltip("Tick to copy the lid's CURRENT rotation into the Closed value (unticks itself).")]
        public bool captureLidClosed;
        [Tooltip("Tick to copy the lid's CURRENT rotation into the Open value (unticks itself).")]
        public bool captureLidOpen;
        [Min(0f)] public float lidSpeed = 140f;
        public bool startLidOpen = false;
        [Tooltip("Open the lid when standing (head out), close it when crouching (head in).")]
        public bool autoLid = true;
        [Tooltip("Close this seat's lid when the player leaves it (switching seat or exiting).")]
        public bool closeLidOnLeave = true;
        public AudioClip lidOpenClip;
        public AudioClip lidCloseClip;

        [Header("Seat Events")]
        public UnityEvent onEntered;
        public UnityEvent onExited;
        public UnityEvent onHeadOut;
        public UnityEvent onHeadIn;
        public UnityEvent onLidOpened;
        public UnityEvent onLidClosed;

        // runtime
        [NonSerialized] public bool lidOpen;
        [NonSerialized] public bool lidMoving;
    }

    // ------------------------------------------------------------ settings

    [Header("Vehicle")]
    [Tooltip("Root of the vehicle. Its colliders are ignored by the headroom check. Defaults to the parent Rigidbody, or this object.")]
    [SerializeField] private Transform vehicleRoot;
    [Tooltip("While seated, the player ignores ALL collision when crouching/standing/prone (fixes cramped compartments).")]
    [SerializeField] private bool ignorePlayerCollision = true;

    [Header("References (auto-found if empty)")]
    [SerializeField] private AdvancedFirstPersonController player;
    [Tooltip("Only needed for Custom Key enter mode.")]
    [SerializeField] private Interactor interactor;

    [Header("Seats")]
    [SerializeField] private Seat[] seats = new Seat[1] { new Seat() };

    [Header("Enter Options")]
    [Tooltip("Drops whatever the player is carrying (PickupHolder). Delete the PickupHolder lines if you don't use it.")]
    [SerializeField] private bool dropHeldObjectOnEnter = true;
    [Tooltip("Off: the Interactor is switched off while seated.")]
    [SerializeField] private bool allowInteractionWhileSeated = true;
    [Tooltip("The lean wall check would hit the tank's own hull. Turn it off while seated.")]
    [SerializeField] private bool disableLeanWallCheck = true;

    [Header("Seat Switching")]
    [SerializeField] private bool allowSeatSwitching = true;
    [Tooltip("Optional keys that cycle through seats. None = disabled.")]
    [SerializeField] private KeyCode nextSeatKey = KeyCode.None;
    [SerializeField] private KeyCode previousSeatKey = KeyCode.None;
    [Tooltip("While seated, looking at another seat's hatch and pressing its interact key moves you there.")]
    [SerializeField] private bool allowSwitchByInteract = true;

    [Header("Exit")]
    [SerializeField] private KeyCode exitKey = KeyCode.F;
    [SerializeField] private LayerMask exitObstructionMask = ~0;
    [Tooltip("On: the player can't exit if every exit spot is blocked. Off: exits at the first spot anyway.")]
    [SerializeField] private bool blockExitIfNoSpace = false;
    [Tooltip("On: player faces the Exit Point's forward. Off: keeps looking where they were looking.")]
    [SerializeField] private bool faceExitPointDirection = true;
    [SerializeField] private bool standUpOnExit = true;
    [Tooltip("After exiting, the hatches can't be used again for this long (prevents instantly re-entering).")]
    [Min(0f)]
    [SerializeField] private float reenableDelay = 0.3f;

    [Tooltip("How fast the view is pushed back inside the yaw limit (deg/s).")]
    [Min(0f)]
    [SerializeField] private float yawCorrectionSpeed = 720f;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip enterClip;
    [SerializeField] private AudioClip exitClip;
    [SerializeField] private AudioClip switchClip;

    [Header("Vehicle Events")]
    [Tooltip("Player got into the vehicle from outside.")]
    [SerializeField] private UnityEvent onEntered;
    [Tooltip("Player left the vehicle.")]
    [SerializeField] private UnityEvent onExited;
    [Tooltip("Passes the new seat index.")]
    [SerializeField] private IntEvent onSeatChanged;
    [SerializeField] private UnityEvent onExitBlocked;

    // C# events for other scripts.
    public event Action<PlayerSeat, AdvancedFirstPersonController> Entered;
    public event Action<PlayerSeat, AdvancedFirstPersonController> Exited;
    /// <summary>(system, fromIndex, toIndex). fromIndex is -1 when entering from outside.</summary>
    public event Action<PlayerSeat, int, int> SeatChanged;
    public event Action<PlayerSeat, bool> HeadStateChanged;

    // ------------------------------------------------------------ public state

    public static PlayerSeat CurrentSeat { get; private set; }

    public bool IsOccupied => state != SeatState.Empty;
    public bool IsSeated => state == SeatState.Seated;
    public bool IsHeadOut => isHeadOut;
    public int SeatCount => seats != null ? seats.Length : 0;
    public int ActiveSeatIndex => currentIndex;
    public string ActiveSeatName => IsValid(currentIndex) ? seats[currentIndex].seatName : "";
    public AdvancedFirstPersonController Occupant => occupant;

    public bool IsLidOpen(int index) => IsValid(index) && seats[index].lidOpen;
    public bool IsSeatLocked(int index) => IsValid(index) && seats[index].locked;

    // ------------------------------------------------------------ private

    private struct SavedSettings
    {
        public bool canCrouch, canProne, canLean, leanWallCheck, canZoom, lookEnabled, interactorEnabled;
        public float minPitch, maxPitch;
    }

    private sealed class InteractionRelay
    {
        public PlayerSeat owner;
        public int index;
        public void Handle(Interactable source) { owner.OnSeatInteracted(index); }
    }

    private SeatState state = SeatState.Empty;
    private AdvancedFirstPersonController occupant;
    private Interactor playerInteractor;
    private Transform originalParent;
    private SavedSettings saved;
    private InteractionRelay[] relays;

    private int currentIndex = -1;
    private int previousIndex = -1;
    private bool transitionFromOutside;
    private int lastActionFrame = -1;
    private float transitionTimer;
    private float transitionDuration;
    private Vector3 startLocalPos;
    private Quaternion startLocalRot;
    private Quaternion targetLocalRot = Quaternion.identity;

    private bool headStateKnown;
    private bool isHeadOut;

    private bool reenableWaiting;
    private float nextInteractorSearch;
    private Coroutine reenableRoutine;
    private readonly Collider[] overlapBuffer = new Collider[16];

    private bool IsValid(int i) => seats != null && i >= 0 && i < seats.Length && seats[i] != null;

    // ------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (seats == null) seats = new Seat[0];

        if (vehicleRoot == null)
        {
            Rigidbody rb = GetComponentInParent<Rigidbody>();
            vehicleRoot = rb != null ? rb.transform : transform;
        }

        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null) continue;

            if (s.seatPoint == null)
                s.seatPoint = transform;

            Vector3 sc = s.seatPoint.lossyScale;
            if (Mathf.Abs(sc.x - 1f) > 0.01f || Mathf.Abs(sc.y - 1f) > 0.01f || Mathf.Abs(sc.z - 1f) > 0.01f)
                Debug.LogWarning("PlayerSeat: seat '" + s.seatName + "' has a scaled Seat Point. The player is parented to it " +
                                 "and will be scaled too. Use an unscaled empty object.", this);
        }
    }

    private void Start()
    {
        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null) continue;

            s.lidOpen = s.startLidOpen;
            if (s.hatchLid != null)
                s.hatchLid.localRotation = Quaternion.Euler(s.lidOpen ? s.lidOpenLocalEuler : s.lidClosedLocalEuler);
        }

        RefreshInteractables();
    }

    private void OnEnable()
    {
        if (seats == null) return;

        relays = new InteractionRelay[seats.Length];
        for (int i = 0; i < seats.Length; i++)
        {
            if (seats[i] == null || seats[i].interactable == null) continue;

            relays[i] = new InteractionRelay { owner = this, index = i };
            seats[i].interactable.InteractionExecuted += relays[i].Handle;
        }
    }

    private void OnDisable()
    {
        if (relays != null && seats != null)
        {
            for (int i = 0; i < relays.Length && i < seats.Length; i++)
            {
                if (relays[i] != null && seats[i] != null && seats[i].interactable != null)
                    seats[i].interactable.InteractionExecuted -= relays[i].Handle;
            }
        }
        relays = null;

        if (state != SeatState.Empty)
            ForceExit();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        UpdateLids(dt);

        if (state == SeatState.Empty)
        {
            PollCustomEnter();
            return;
        }

        // Player got destroyed while seated.
        if (occupant == null)
        {
            ResetState();
            return;
        }

        if (state == SeatState.Transitioning)
        {
            UpdateTransition(dt);
            return;
        }

        // Seated. Frame check: if Enter and Exit are the same key, the press that entered must not also exit.
        if (Time.frameCount == lastActionFrame)
            return;

        PollCustomEnter(); // looking at another hatch + its key = switch
        if (state == SeatState.Seated)
            HandleSeatedInput();
    }

    private void LateUpdate()
    {
        if (state != SeatState.Seated || occupant == null || !IsValid(currentIndex))
            return;

        // Hold the player exactly on the seat point (it is parented, so the tank carries it).
        occupant.transform.localPosition = Vector3.zero;

        UpdateView();
        ApplyYawLimit(Time.deltaTime);
    }

    // ------------------------------------------------------------ input

    private void HandleSeatedInput()
    {
        Seat cur = seats[currentIndex];

        if (allowSeatSwitching && cur.canSwitchAway)
        {
            for (int i = 0; i < seats.Length; i++)
            {
                if (i == currentIndex || seats[i] == null || seats[i].switchKey == KeyCode.None)
                    continue;

                if (Input.GetKeyDown(seats[i].switchKey) && TrySwitchTo(i))
                    return;
            }

            if (nextSeatKey != KeyCode.None && Input.GetKeyDown(nextSeatKey))
            {
                int n = CycleIndex(1);
                if (n >= 0 && TrySwitchTo(n)) return;
            }

            if (previousSeatKey != KeyCode.None && Input.GetKeyDown(previousSeatKey))
            {
                int n = CycleIndex(-1);
                if (n >= 0 && TrySwitchTo(n)) return;
            }
        }

        if (cur.canExit && Input.GetKeyDown(exitKey))
            TryExit();
    }

    private int CycleIndex(int dir)
    {
        int n = seats.Length;
        for (int k = 1; k < n; k++)
        {
            int idx = (((currentIndex + dir * k) % n) + n) % n;
            if (seats[idx] != null && !seats[idx].locked)
                return idx;
        }
        return -1;
    }

    private void PollCustomEnter()
    {
        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null || s.interactable == null || s.enterMode != EnterMode.CustomKey || s.locked)
                continue;

            if (interactor == null && Time.unscaledTime >= nextInteractorSearch)
            {
                interactor = FindFirst<Interactor>();
                nextInteractorSearch = Time.unscaledTime + 1f;
            }
            if (interactor == null)
                return;

            if (interactor.CurrentInteractable == s.interactable &&
                s.interactable.IsInteractable &&
                Input.GetKeyDown(s.enterKey))
            {
                RequestSeat(i);
                return;
            }
        }
    }

    private void OnSeatInteracted(int index)
    {
        if (!IsValid(index) || seats[index].enterMode != EnterMode.InteractorKey)
            return;

        RequestSeat(index);
    }

    private void RequestSeat(int index)
    {
        AdvancedFirstPersonController p = ResolvePlayer();

        if (state == SeatState.Empty)
        {
            TryEnter(index, p);
        }
        else if (state == SeatState.Seated && allowSwitchByInteract && p == occupant && seats[currentIndex].canSwitchAway)
        {
            if (!TrySwitchTo(index))
                ResetSeatInteraction(index);
        }
        else
        {
            ResetSeatInteraction(index);
        }
    }

    // ------------------------------------------------------------ enter

    public bool CanEnter(int index, AdvancedFirstPersonController p)
    {
        return p != null && IsValid(index) && state == SeatState.Empty && !seats[index].locked && !p.IsMotionLocked;
    }

    /// <summary>For UnityEvents / buttons: enters (from outside) or switches to the given seat.</summary>
    public void EnterSeat(int index)
    {
        if (state == SeatState.Empty) TryEnter(index, ResolvePlayer());
        else TrySwitchTo(index);
    }

    public bool TryEnter(int index, AdvancedFirstPersonController p)
    {
        if (!CanEnter(index, p))
        {
            // Don't "use up" a single-use Interactable on a failed attempt.
            ResetSeatInteraction(index);
            return false;
        }

        occupant = p;
        CurrentSeat = this;
        lastActionFrame = Time.frameCount;

        if (reenableRoutine != null)
        {
            StopCoroutine(reenableRoutine);
            reenableRoutine = null;
        }
        reenableWaiting = false;

        if (dropHeldObjectOnEnter)
        {
            PickupHolder holder = p.GetComponentInChildren<PickupHolder>();
            if (holder == null) holder = p.GetComponentInParent<PickupHolder>();
            if (holder != null) holder.Drop();
        }

        playerInteractor = p.GetComponentInChildren<Interactor>();
        if (playerInteractor == null) playerInteractor = p.GetComponentInParent<Interactor>();

        SaveSettings(p);
        originalParent = p.transform.parent;

        p.SetMotionLocked(true, vehicleRoot, ignorePlayerCollision);

        transitionFromOutside = true;
        previousIndex = -1;
        AttachTo(index);

        PlaySound(enterClip);
        return true;
    }

    // ------------------------------------------------------------ switch seats

    /// <summary>Teleport / glide to another seat while seated.</summary>
    public bool TrySwitchTo(int index)
    {
        if (state != SeatState.Seated || occupant == null)
            return false;

        if (!IsValid(index) || index == currentIndex || seats[index].locked)
            return false;

        Seat prev = seats[currentIndex];
        if (prev.closeLidOnLeave)
            SetLid(currentIndex, false);

        prev.onExited?.Invoke();

        previousIndex = currentIndex;
        transitionFromOutside = false;
        lastActionFrame = Time.frameCount;

        AttachTo(index);

        PlaySound(switchClip);
        return true;
    }

    public bool TrySwitchTo(string seatName)
    {
        for (int i = 0; i < seats.Length; i++)
            if (seats[i] != null && seats[i].seatName == seatName)
                return TrySwitchTo(i);
        return false;
    }

    // ------------------------------------------------------------ attach (shared by enter + switch)

    private void AttachTo(int index)
    {
        currentIndex = index;
        Seat seat = seats[index];
        AdvancedFirstPersonController p = occupant;
        Transform pt = p.transform;

        ApplySeatRestrictions(p, seat);

        if (seat.snapLookToSeat)
            p.SetLookRotation(pt.eulerAngles.y, seat.startPitch);

        pt.SetParent(seat.seatPoint, true);

        startLocalPos = pt.localPosition;
        startLocalRot = pt.localRotation;
        targetLocalRot = seat.snapLookToSeat
            ? Quaternion.identity
            : Quaternion.Euler(0f, Mathf.DeltaAngle(0f, startLocalRot.eulerAngles.y), 0f);

        // Stance for this seat.
        AdvancedFirstPersonController.Stance stance = p.CurrentStance;
        switch (seat.stanceOnEnter)
        {
            case StartingStance.Standing: stance = AdvancedFirstPersonController.Stance.Standing; break;
            case StartingStance.Crouching: stance = AdvancedFirstPersonController.Stance.Crouching; break;
        }
        if (stance == AdvancedFirstPersonController.Stance.Prone && !seat.allowProne)
            stance = AdvancedFirstPersonController.Stance.Crouching;
        p.SetStance(stance);

        float time = transitionFromOutside ? seat.enterTransitionTime : seat.switchTransitionTime;

        RefreshInteractables();

        if (time > 0.001f)
        {
            state = SeatState.Transitioning;
            transitionTimer = 0f;
            transitionDuration = time;
            p.SetLookEnabled(false);
        }
        else
        {
            FinishAttach();
        }
    }

    private void UpdateTransition(float dt)
    {
        transitionTimer += dt / Mathf.Max(0.01f, transitionDuration);
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transitionTimer));

        Transform pt = occupant.transform;
        pt.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, s);
        pt.localRotation = Quaternion.Slerp(startLocalRot, targetLocalRot, s);

        if (transitionTimer >= 1f)
            FinishAttach();
    }

    private void FinishAttach()
    {
        Transform pt = occupant.transform;
        pt.localPosition = Vector3.zero;
        pt.localRotation = targetLocalRot;

        occupant.SetLookEnabled(saved.lookEnabled);

        state = SeatState.Seated;
        headStateKnown = false; // forces this seat's view profile to apply on the next LateUpdate

        seats[currentIndex].onEntered?.Invoke();

        if (transitionFromOutside)
        {
            onEntered?.Invoke();
            Entered?.Invoke(this, occupant);
        }

        onSeatChanged?.Invoke(currentIndex);
        SeatChanged?.Invoke(this, previousIndex, currentIndex);
    }

    // ------------------------------------------------------------ exit

    /// <summary>Convenience for UnityEvents / buttons.</summary>
    public void Exit()
    {
        TryExit();
    }

    public bool TryExit()
    {
        if (state != SeatState.Seated || occupant == null || !IsValid(currentIndex))
            return false;

        if (!FindExitPose(seats[currentIndex], blockExitIfNoSpace, out Vector3 pos, out float yaw))
        {
            onExitBlocked?.Invoke();
            return false;
        }

        DoExit(pos, yaw);
        return true;
    }

    /// <summary>Exits immediately, even if every exit spot is blocked or the player is mid-transition.</summary>
    public void ForceExit()
    {
        if (state == SeatState.Empty)
            return;

        if (occupant == null || !IsValid(currentIndex))
        {
            ResetState();
            return;
        }

        FindExitPose(seats[currentIndex], false, out Vector3 pos, out float yaw);
        DoExit(pos, yaw);
    }

    private bool FindExitPose(Seat seat, bool requireClear, out Vector3 position, out float yaw)
    {
        float currentYaw = occupant.transform.eulerAngles.y;

        if (seat.exitPoints != null)
        {
            foreach (Transform point in seat.exitPoints)
            {
                if (point == null || !IsExitClear(point.position))
                    continue;

                position = point.position;
                yaw = faceExitPointDirection ? point.eulerAngles.y : currentYaw;
                return true;
            }
        }

        Vector3 fallback = seat.seatPoint.TransformPoint(seat.fallbackExitOffset);

        if (IsExitClear(fallback))
        {
            position = fallback;
            yaw = currentYaw;
            return true;
        }

        // Nothing is clear.
        if (requireClear)
        {
            position = fallback;
            yaw = currentYaw;
            return false;
        }

        Transform first = FirstExitPoint(seat);

        if (first != null)
        {
            position = first.position;
            yaw = faceExitPointDirection ? first.eulerAngles.y : currentYaw;
        }
        else
        {
            position = fallback;
            yaw = currentYaw;
        }

        return true;
    }

    private static Transform FirstExitPoint(Seat seat)
    {
        if (seat.exitPoints == null)
            return null;

        foreach (Transform t in seat.exitPoints)
            if (t != null) return t;

        return null;
    }

    private bool IsExitClear(Vector3 feet)
    {
        CharacterController cc = occupant.GetComponent<CharacterController>();
        if (cc == null)
            return true;

        float r = cc.radius;
        float h = Mathf.Max(occupant.crouchHeight, r * 2f + 0.02f);

        Vector3 p0 = feet + Vector3.up * (r + 0.1f);
        Vector3 p1 = feet + Vector3.up * Mathf.Max(h - r, r + 0.1f);

        int count = Physics.OverlapCapsuleNonAlloc(
            p0, p1, r * 0.9f, overlapBuffer, exitObstructionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];

            if (c == null || c.transform.IsChildOf(occupant.transform))
                continue;

            return false;
        }

        return true;
    }

    private void DoExit(Vector3 position, float yaw)
    {
        AdvancedFirstPersonController p = occupant;
        Transform pt = p.transform;
        float pitch = p.CameraPitch;
        int exitedIndex = currentIndex;
        Seat seat = seats[exitedIndex];

        if (standUpOnExit)
            p.SetStance(AdvancedFirstPersonController.Stance.Standing);

        pt.SetParent(originalParent, true);

        // Restore limits BEFORE teleporting so the saved pitch isn't clamped by the seat's limits.
        RestoreSettings(p);

        p.Teleport(position, yaw, pitch);
        p.SetMotionLocked(false);

        if (seat.closeLidOnLeave)
            SetLid(exitedIndex, false);

        PlaySound(exitClip);

        state = SeatState.Empty;
        occupant = null;
        currentIndex = -1;
        previousIndex = -1;
        headStateKnown = false;
        if (CurrentSeat == this) CurrentSeat = null;

        if (isActiveAndEnabled && reenableDelay > 0f)
        {
            reenableWaiting = true;
            RefreshInteractables();
            reenableRoutine = StartCoroutine(ReenableAfterDelay());
        }
        else
        {
            reenableWaiting = false;
            RefreshInteractables();
        }

        seat.onExited?.Invoke();
        onExited?.Invoke();
        Exited?.Invoke(this, p);
    }

    private void ResetState()
    {
        state = SeatState.Empty;
        occupant = null;
        currentIndex = -1;
        previousIndex = -1;
        headStateKnown = false;
        reenableWaiting = false;
        if (CurrentSeat == this) CurrentSeat = null;
        RefreshInteractables();
    }

    private IEnumerator ReenableAfterDelay()
    {
        yield return new WaitForSeconds(reenableDelay);
        reenableRoutine = null;
        reenableWaiting = false;
        RefreshInteractables();
    }

    // ------------------------------------------------------------ interactables

    private void ResetSeatInteraction(int index)
    {
        if (IsValid(index) && seats[index].interactable != null)
            seats[index].interactable.ResetInteraction();
    }

    /// <summary>Turns every seat's Interactable on/off to match the current state.</summary>
    private void RefreshInteractables()
    {
        if (seats == null) return;

        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null || s.interactable == null)
                continue;

            bool on;
            if (s.locked || reenableWaiting)
                on = false;
            else if (state == SeatState.Empty)
                on = true;
            else
                on = allowSwitchByInteract && i != currentIndex; // can click other hatches to move there

            if (on && !s.interactable.IsInteractable)
            {
                // Clears "hasBeenUsed" so it works even if the Interactable has Multiple Use off.
                s.interactable.ResetInteraction();
                s.interactable.SetInteractable(true);
            }
            else if (!on && s.interactable.IsInteractable)
            {
                s.interactable.SetInteractable(false);
            }
        }
    }

    // ------------------------------------------------------------ view (head in / head out)

    private void UpdateView()
    {
        Seat seat = seats[currentIndex];
        bool headOut = occupant.CurrentStance == AdvancedFirstPersonController.Stance.Standing;

        if (headStateKnown && headOut == isHeadOut)
            return;

        headStateKnown = true;
        isHeadOut = headOut;

        ViewProfile view = headOut ? seat.headOutView : seat.headInView;
        occupant.minPitch = view.minPitch;
        occupant.maxPitch = view.maxPitch;

        if (seat.autoLid)
            SetLid(currentIndex, headOut);

        if (headOut) seat.onHeadOut?.Invoke();
        else seat.onHeadIn?.Invoke();

        HeadStateChanged?.Invoke(this, headOut);
    }

    private void ApplyYawLimit(float dt)
    {
        Seat seat = seats[currentIndex];
        ViewProfile view = isHeadOut ? seat.headOutView : seat.headInView;

        if (!view.limitYaw)
            return;

        Transform t = occupant.transform;

        float yaw = Mathf.DeltaAngle(0f, t.localEulerAngles.y);
        float clamped = Mathf.Clamp(yaw, -view.yawLimit, view.yawLimit);

        if (Mathf.Approximately(yaw, clamped))
            return;

        yaw = Mathf.MoveTowards(yaw, clamped, yawCorrectionSpeed * dt);
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // ------------------------------------------------------------ player settings

    private void SaveSettings(AdvancedFirstPersonController p)
    {
        saved = new SavedSettings
        {
            canCrouch = p.canCrouch,
            canProne = p.canProne,
            canLean = p.canLean,
            leanWallCheck = p.leanWallCheck,
            canZoom = p.canZoom,
            lookEnabled = p.lookEnabled,
            minPitch = p.minPitch,
            maxPitch = p.maxPitch,
            interactorEnabled = playerInteractor != null && playerInteractor.enabled
        };
    }

    private void ApplySeatRestrictions(AdvancedFirstPersonController p, Seat seat)
    {
        p.canCrouch = seat.allowCrouch;
        p.canProne = seat.allowProne;
        p.canLean = seat.allowLean;
        p.canZoom = seat.allowZoom;

        if (disableLeanWallCheck)
            p.leanWallCheck = false;

        if (!allowInteractionWhileSeated && playerInteractor != null)
            playerInteractor.enabled = false;
    }

    private void RestoreSettings(AdvancedFirstPersonController p)
    {
        p.canCrouch = saved.canCrouch;
        p.canProne = saved.canProne;
        p.canLean = saved.canLean;
        p.leanWallCheck = saved.leanWallCheck;
        p.canZoom = saved.canZoom;
        p.minPitch = saved.minPitch;
        p.maxPitch = saved.maxPitch;
        p.SetLookEnabled(saved.lookEnabled);

        if (playerInteractor != null)
            playerInteractor.enabled = saved.interactorEnabled;
    }

    // ------------------------------------------------------------ hatch lids

    public void OpenLid(int index) => SetLid(index, true);
    public void CloseLid(int index) => SetLid(index, false);
    public void ToggleLid(int index) { if (IsValid(index)) SetLid(index, !seats[index].lidOpen); }

    public void SetLid(int index, bool open)
    {
        if (!IsValid(index))
            return;

        Seat s = seats[index];
        if (open == s.lidOpen)
            return;

        s.lidOpen = open;
        PlaySound(open ? s.lidOpenClip : s.lidCloseClip);

        if (s.hatchLid == null)
        {
            // No lid object: still fire the events so an Animator etc. can react.
            if (open) s.onLidOpened?.Invoke(); else s.onLidClosed?.Invoke();
            return;
        }

        s.lidMoving = true;
    }

    private void UpdateLids(float dt)
    {
        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null || s.hatchLid == null || !s.lidMoving)
                continue;

            Quaternion target = Quaternion.Euler(s.lidOpen ? s.lidOpenLocalEuler : s.lidClosedLocalEuler);
            s.hatchLid.localRotation = Quaternion.RotateTowards(s.hatchLid.localRotation, target, s.lidSpeed * dt);

            if (Quaternion.Angle(s.hatchLid.localRotation, target) < 0.01f)
            {
                s.hatchLid.localRotation = target;
                s.lidMoving = false;

                if (s.lidOpen) s.onLidOpened?.Invoke();
                else s.onLidClosed?.Invoke();
            }
        }
    }

    // ------------------------------------------------------------ public helpers

    /// <summary>Lock / unlock a seat (e.g. a damaged hatch, a quest condition).</summary>
    public void SetSeatLocked(int index, bool value)
    {
        if (!IsValid(index))
            return;

        seats[index].locked = value;
        RefreshInteractables();
    }

    public void LockSeat(int index) => SetSeatLocked(index, true);
    public void UnlockSeat(int index) => SetSeatLocked(index, false);

    // ------------------------------------------------------------ misc

    private AdvancedFirstPersonController ResolvePlayer()
    {
        if (player == null)
            player = FindFirst<AdvancedFirstPersonController>();

        return player;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindObjectOfType<T>();
    }

    private void OnValidate()
    {
        if (seats == null) return;

        foreach (Seat s in seats)
        {
            if (s == null) continue;

            if (s.captureLidClosed)
            {
                s.captureLidClosed = false;
                if (s.hatchLid != null) s.lidClosedLocalEuler = s.hatchLid.localEulerAngles;
            }

            if (s.captureLidOpen)
            {
                s.captureLidOpen = false;
                if (s.hatchLid != null) s.lidOpenLocalEuler = s.hatchLid.localEulerAngles;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (seats == null) return;

        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null) continue;

            Transform sp = s.seatPoint != null ? s.seatPoint : transform;

            // Seat point (feet) + forward.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(sp.position + Vector3.up * 0.1f, 0.15f);
            Gizmos.DrawRay(sp.position + Vector3.up * 1.2f, sp.forward * 0.8f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(sp.position + Vector3.up * 1.5f,
                i + ": " + s.seatName + (s.switchKey != KeyCode.None ? "  [" + s.switchKey + "]" : ""));
#endif

            // Yaw limits.
            Vector3 eye = sp.position + sp.up * 1.6f;
            DrawYawLimit(eye, sp, s.headOutView, Color.yellow);
            DrawYawLimit(eye, sp, s.headInView, new Color(1f, 0.5f, 0f));

            // Exit points.
            Gizmos.color = Color.green;
            if (s.exitPoints != null)
            {
                foreach (Transform p in s.exitPoints)
                {
                    if (p == null) continue;
                    Gizmos.DrawWireSphere(p.position + Vector3.up * 0.1f, 0.15f);
                    Gizmos.DrawRay(p.position + Vector3.up * 1f, p.forward * 0.5f);
                    Gizmos.DrawLine(sp.position + Vector3.up * 0.1f, p.position + Vector3.up * 0.1f);
                }
            }

            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.DrawWireSphere(sp.TransformPoint(s.fallbackExitOffset) + Vector3.up * 0.1f, 0.1f);
        }
    }

    private static void DrawYawLimit(Vector3 eye, Transform sp, ViewProfile view, Color color)
    {
        if (!view.limitYaw)
            return;

        Gizmos.color = color;
        Gizmos.DrawRay(eye, Quaternion.AngleAxis(view.yawLimit, sp.up) * sp.forward * 1.5f);
        Gizmos.DrawRay(eye, Quaternion.AngleAxis(-view.yawLimit, sp.up) * sp.forward * 1.5f);
    }
}