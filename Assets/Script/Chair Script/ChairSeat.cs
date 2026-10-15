
using System.Collections;
using UnityEngine;

public class ChairSeat : MonoBehaviour
{
    public enum MovementMode
    {
        Teleport,
        SmoothMove
    }

    [Header("Player References")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Behaviour playerMovementScript;

    [Header("Interactor Connection")]
    [SerializeField] private Interactor interactor;

    [Tooltip("Allow this chair to use its own key to enter.")]
    [SerializeField] private bool allowCustomEnterKey = true;

    [Tooltip("Custom key for entering this chair.")]
    [SerializeField] private KeyCode enterKey = KeyCode.F;

    [Header("Seat Positions")]
    [SerializeField] private Transform sittingPoint;
    [SerializeField] private Transform standingPoint;

    [Header("Standing Keys")]
    [SerializeField] private KeyCode standKey = KeyCode.R;

    [Tooltip("Allow the Interactor key to stand up.")]
    [SerializeField] private bool allowInteractorKeyToStand = true;

    [Header("Movement")]
    [SerializeField] private MovementMode sitMovement = MovementMode.SmoothMove;
    [SerializeField] private MovementMode standMovement = MovementMode.SmoothMove;

    [Min(0.01f)]
    [SerializeField] private float sitMoveDuration = 0.35f;

    [Min(0.01f)]
    [SerializeField] private float standMoveDuration = 0.3f;

    [SerializeField] private bool matchSittingRotation = true;
    [SerializeField] private bool matchStandingRotation = true;
    [SerializeField] private bool disableControllerDuringMovement = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    [Header("State")]
    [SerializeField] private bool isSitting;
    [SerializeField] private bool isMoving;

    private Coroutine movementRoutine;
    private bool originalMovementEnabled;
    private bool movementStateSaved;
    private bool controllerWasEnabled;
    private bool controllerStateSaved;

    public bool IsSitting => isSitting;
    public bool IsMoving => isMoving;

    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[ChairSeat: {name}] {message}", this);
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"[ChairSeat: {name}] {message}", this);
    }

    private void Awake()
    {
        Log("Awake: ChairSeat initialized.");

        if (playerRoot == null)
            Warn("Player Root is NOT assigned.");

        if (characterController == null)
            Warn("Character Controller is NOT assigned.");

        if (playerMovementScript == null)
            Warn("Player Movement Script is NOT assigned.");

        if (sittingPoint == null)
            Warn("Sitting Point is NOT assigned.");

        if (standingPoint == null)
            Warn("Standing Point is NOT assigned.");

        if (interactor == null)
            Warn("Interactor is NOT assigned.");
    }

    private void OnEnable()
    {
        ConnectToInteractor();
    }

    private void ConnectToInteractor()
    {
        if (interactor == null)
            return;

        // Prevent duplicate subscriptions.
        interactor.Interacted -= OnInteractorInteracted;
        interactor.Interacted += OnInteractorInteracted;

        Log("Connected to Interactor.Interacted event.");
    }

    private void OnDisable()
    {
        if (interactor != null)
            interactor.Interacted -= OnInteractorInteracted;

        if (movementRoutine != null)
        {
            StopCoroutine(movementRoutine);
            movementRoutine = null;
            Log("Movement coroutine stopped.");
        }

        RestoreCharacterController();
        RestorePlayerMovement();

        isMoving = false;
        isSitting = false;
    }

    private void OnInteractorInteracted(Interactable target)
    {
        if (!IsThisChair(target))
            return;

        Log($"Interactor event received for this chair: {target.name}");

        // The chair was interacted with through the Interactor key.
        Interact();
    }

    private bool IsThisChair(Interactable target)
    {
        if (target == null)
            return false;

        Transform targetTransform = target.transform;

        // Supports Interactable and ChairSeat on the same object,
        // or on parent/child objects within the same hierarchy.
        return targetTransform == transform ||
               targetTransform.IsChildOf(transform) ||
               transform.IsChildOf(targetTransform);
    }

    private void Update()
    {
        if (isMoving)
            return;

        // The custom enter key works only while this chair
        // is the Interactor's current target.
        if (!isSitting &&
            allowCustomEnterKey &&
            interactor != null &&
            Input.GetKeyDown(enterKey))
        {
            Interactable target = interactor.CurrentInteractable;

            if (IsThisChair(target))
            {
                Log($"Custom enter key pressed: {enterKey}");
                Interact();
                return;
            }
        }

        if (!isSitting)
            return;

        if (Input.GetKeyDown(standKey))
        {
            Log($"Stand key pressed: {standKey}");
            StandUp();
            return;
        }

        // Uses the same key configured in Interactor.
        // No second hardcoded E/F setting is necessary.
        if (allowInteractorKeyToStand &&
            interactor != null &&
            Input.GetKeyDown(GetInteractorKey()))
        {
            Log("Interactor key pressed to stand.");
            StandUp();
        }
    }

    private KeyCode GetInteractorKey()
    {
        // ChairSeat uses the Interactor's actual configured key.
        return interactor.InteractionKey;
    }

    public void Interact()
    {
        Log("Interact() received.");

        if (isMoving || isSitting)
        {
            Log("Enter ignored: already sitting or moving.");
            return;
        }

        if (playerRoot == null || sittingPoint == null)
        {
            Warn("Cannot sit: Player Root or Sitting Point is missing.");
            return;
        }

        BeginMovement(
            sittingPoint,
            sitMovement,
            sitMoveDuration,
            matchSittingRotation,
            true);
    }

    public void StandUp()
    {
        Log("StandUp() called.");

        if (!isSitting || isMoving)
        {
            Log("Stand request ignored: not sitting or already moving.");
            return;
        }

        if (standingPoint == null)
        {
            Warn("Cannot stand: Standing Point is missing.");
            return;
        }

        BeginMovement(
            standingPoint,
            standMovement,
            standMoveDuration,
            matchStandingRotation,
            false);
    }

    private void BeginMovement(
        Transform target,
        MovementMode mode,
        float duration,
        bool matchRotation,
        bool enteringSeat)
    {
        if (target == null || playerRoot == null)
        {
            Warn("Movement cancelled: target or Player Root is missing.");
            return;
        }

        if (movementRoutine != null)
            StopCoroutine(movementRoutine);

        movementRoutine = StartCoroutine(
            MovePlayer(target, mode, duration, matchRotation, enteringSeat));
    }

    private IEnumerator MovePlayer(
        Transform target,
        MovementMode mode,
        float duration,
        bool matchRotation,
        bool enteringSeat)
    {
        isMoving = true;

        Log(enteringSeat ? "SIT MOVEMENT STARTED." : "STAND MOVEMENT STARTED.");

        if (enteringSeat && playerMovementScript != null)
        {
            originalMovementEnabled = playerMovementScript.enabled;
            movementStateSaved = true;
            playerMovementScript.enabled = false;
        }

        if (characterController != null)
        {
            controllerWasEnabled = characterController.enabled;
            controllerStateSaved = true;

            if (disableControllerDuringMovement)
                characterController.enabled = false;
        }

        Vector3 startPosition = playerRoot.position;
        Quaternion startRotation = playerRoot.rotation;

        Vector3 endPosition = target.position;
        Quaternion endRotation = matchRotation
            ? target.rotation
            : startRotation;

        if (mode == MovementMode.Teleport)
        {
            playerRoot.SetPositionAndRotation(endPosition, endRotation);
        }
        else
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.01f, duration);

            while (elapsed < duration)
            {
                if (playerRoot == null || target == null)
                {
                    Warn("Movement interrupted: player or target was destroyed.");
                    break;
                }

                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);

                playerRoot.position = Vector3.Lerp(startPosition, endPosition, t);
                playerRoot.rotation = Quaternion.Slerp(startRotation, endRotation, t);

                yield return null;
            }

            if (playerRoot != null && target != null)
                playerRoot.SetPositionAndRotation(endPosition, endRotation);
        }

        RestoreCharacterController();

        if (!enteringSeat)
            RestorePlayerMovement();

        isSitting = enteringSeat;
        isMoving = false;
        movementRoutine = null;

        Log(enteringSeat
            ? "SIT COMPLETE. Player is now sitting."
            : "STAND COMPLETE. Player can move again.");
    }

    private void RestoreCharacterController()
    {
        if (controllerStateSaved && characterController != null)
            characterController.enabled = controllerWasEnabled;

        controllerStateSaved = false;
    }

    private void RestorePlayerMovement()
    {
        if (movementStateSaved && playerMovementScript != null)
            playerMovementScript.enabled = originalMovementEnabled;

        movementStateSaved = false;
    }
}
