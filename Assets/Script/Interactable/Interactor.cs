
using System;
using UnityEngine;

public class Interactor : MonoBehaviour
{

    

    [Header("Interaction Settings")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;


    [Header("Prompt Stability")]
    [SerializeField, Min(0f)]
    private float targetGraceTime = 0.3f;

    private float lastTargetSeenTime;

    private Interactable currentInteractable;
    private float holdTimer;
    private bool holdCompleted;




    // Other scripts can read the current target.
    public Interactable CurrentInteractable => currentInteractable;

    // Other scripts can read the configured interaction key.
    public KeyCode InteractionKey => interactKey;

    // Other scripts can listen for completed interactions.
    public event Action<Interactable> Interacted;



    private void ExecuteInteraction()
    {
        if (currentInteractable == null)
            return;

        currentInteractable.OnInteract();

        // Notify every subscribed script.
        Interacted?.Invoke(currentInteractable);
    }

    private void Update()
    {
        CheckForInteractable();

        if (currentInteractable == null ||
            !currentInteractable.IsInteractable)
        {
            ResetHold();
            return;
        }

        if (currentInteractable.HoldInteract)
        {
            HandleHoldInteraction();
        }
        else
        {
            if (Input.GetKeyDown(interactKey))
                ExecuteInteraction();

            ResetHold();
        }
    }


    private void CheckForInteractable()
    {
        Interactable target = null;

        if (playerCamera != null)
        {
            Ray ray = playerCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f)
            );

            if (Physics.Raycast(
                ray, out RaycastHit hit, interactDistance))
            {
                target = hit.collider
                    .GetComponentInParent<Interactable>();

                if (target != null && !target.IsInteractable)
                    target = null;
            }
        }

        if (target != null)
        {
            lastTargetSeenTime = Time.time;

            if (target != currentInteractable)
            {
                if (currentInteractable != null)
                    currentInteractable.HidePrompt();

                currentInteractable = target;
                ResetHold();
            }
        }
        else if (currentInteractable != null &&
                 Time.time - lastTargetSeenTime > targetGraceTime)
        {
            currentInteractable.HidePrompt();
            currentInteractable = null;
            ResetHold();
        }


        if (currentInteractable != null &&
            currentInteractable.IsInteractable)
        {
            currentInteractable.ShowPrompt();

            if (playerCamera != null)
            {
                currentInteractable.UpdatePromptFacing(
                    playerCamera.transform
                );
            }
        }

        else if (currentInteractable != null)
        {
            currentInteractable.HidePrompt();
            currentInteractable = null;
            ResetHold();
        }
    }


    private void HandleHoldInteraction()
    {
        if (holdCompleted)
        {
            if (!Input.GetKey(interactKey))
                ResetHold();

            return;
        }

        if (!Input.GetKey(interactKey))
        {
            ResetHold();
            currentInteractable.UpdateProgress(0f);
            return;
        }

        holdTimer += Time.deltaTime;

        float duration = Mathf.Max(
            0f, currentInteractable.HoldDuration
        );

        float progress = duration > 0f
            ? Mathf.Clamp01(holdTimer / duration)
            : 1f;

        currentInteractable.UpdateProgress(progress);

        if (holdTimer >= duration)
        {
            ExecuteInteraction();

            holdCompleted = true;
            currentInteractable.UpdateProgress(1f);
        }
    }

    private void ResetHold()
    {
        holdTimer = 0f;
        holdCompleted = false;

        if (currentInteractable != null)
            currentInteractable.UpdateProgress(0f);
    }

    private void OnDisable()
    {
        if (currentInteractable != null)
            currentInteractable.HidePrompt();

        currentInteractable = null;
        ResetHold();
    }
}
