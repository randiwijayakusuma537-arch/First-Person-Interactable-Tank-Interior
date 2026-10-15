
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System;
using UnityEngine.UI;

public class Interactable : MonoBehaviour, IInteractable
{
    private bool hasBeenUsed;
    private bool promptVisible;
    private bool showingSuccessMessage;

    private float successMessageTimer;
    private float currentAlpha;
    private float targetAlpha;
    private string defaultMessage;
    private CanvasGroup canvasGroup;
    private Quaternion initialPromptRotation;
    private bool hasInitialPromptRotation;

    [Header("UI References")]
    [SerializeField] private GameObject interactionPanel;
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private Image holdProgress;

    [Header("Prompt Content")]
    [SerializeField] private string title = "Object";
    [SerializeField] private string message = "Press E to interact";

    [TextArea(2, 4)]
    [SerializeField] private string description = "";

    [Header("Fade Settings")]
    [Tooltip("Time taken to fade the prompt in.")]
    [Min(0f)]
    [SerializeField] private float fadeInDuration = 0.2f;

    [Tooltip("Time taken to fade the prompt out.")]
    [Min(0f)]
    [SerializeField] private float fadeOutDuration = 0.2f;

    [Header("Interaction Settings")]
    [SerializeField] private bool isInteractable = true;
    [SerializeField] private bool holdInteract = false;

    [Min(0f)]
    [SerializeField] private float holdDuration = 1f;

    [SerializeField] private bool multipleUse = true;

    [Header("Success Feedback")]
    [SerializeField] private bool showSuccessMessage = true;
    [SerializeField] private string successMessage = "Interaction completed!";

    [Min(0f)]
    [SerializeField] private float successMessageDuration = 1.5f;

    [SerializeField] private AudioSource interactionAudio;

    [Header("World-Space UI Facing")]
    [Tooltip("Enable only for a world-space prompt that should face the camera.")]
    [SerializeField] private bool faceCamera = false;
    [SerializeField]
    private bool invertFacing = false;

    [Tooltip("Transform of the world-space prompt to rotate.")]
    [SerializeField] private Transform promptFacingTransform;

    [Tooltip("Maximum angle the prompt can rotate away from its initial forward direction.")]
    [Range(0f, 180f)]
    [SerializeField] private float maxFacingAngle = 60f;

    [Header("Events")]
    [SerializeField] private UnityEvent onInteraction;


    [Header("Additional Events")]
    [Tooltip("Called when the interaction prompt becomes visible.")]
    [SerializeField] private UnityEvent onPromptShown;

    [Tooltip("Called when the interaction prompt becomes hidden.")]
    [SerializeField] private UnityEvent onPromptHidden;

    [Tooltip("Called when the interactable's availability changes.")]
    [SerializeField] private UnityEvent onAvailabilityChanged;

    // C# events for other scripts to subscribe to at runtime.
    public event Action<Interactable> InteractionExecuted;
    public event Action<Interactable> PromptShown;
    public event Action<Interactable> PromptHidden;
    public event Action<Interactable, bool> AvailabilityChanged;


    public string Message => message;
    public string Title => title;
    public string Description => description;
    public float HoldDuration => holdDuration;
    public bool HoldInteract => holdInteract;
    public bool MultipleUse => multipleUse;

    public bool IsInteractable =>
        isInteractable && (multipleUse || !hasBeenUsed);

    private void Awake()
    {
        defaultMessage = message;

        if (interactionPanel != null)
        {
            canvasGroup = interactionPanel.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = interactionPanel.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            currentAlpha = 0f;
            targetAlpha = 0f;

            interactionPanel.SetActive(false);
        }

        if (promptFacingTransform != null)
        {
            initialPromptRotation = promptFacingTransform.rotation;
            hasInitialPromptRotation = true;
        }
    }

    private void Update()
    {
        UpdateFade();
        UpdateSuccessMessage();
    }

    private void UpdateFade()
    {
        if (canvasGroup == null)
            return;

        float duration = targetAlpha > currentAlpha
            ? fadeInDuration
            : fadeOutDuration;

        if (duration <= 0f)
        {
            currentAlpha = targetAlpha;
        }
        else
        {
            currentAlpha = Mathf.MoveTowards(
                currentAlpha,
                targetAlpha,
                Time.unscaledDeltaTime / duration
            );
        }

        canvasGroup.alpha = currentAlpha;

        if (Mathf.Approximately(currentAlpha, 0f) &&
            targetAlpha == 0f &&
            interactionPanel != null &&
            interactionPanel.activeSelf)
        {
            interactionPanel.SetActive(false);
        }
    }

    private void UpdateSuccessMessage()
    {
        if (!showingSuccessMessage)
            return;

        successMessageTimer -= Time.deltaTime;

        if (successMessageTimer <= 0f)
        {
            showingSuccessMessage = false;

            if (promptVisible)
                RefreshPrompt();
        }
    }

    public void ShowPrompt()
    {
        if (!IsInteractable)
            return;

        if (interactionPanel != null &&
            !interactionPanel.activeSelf)
        {
            interactionPanel.SetActive(true);
        }

        if (!promptVisible)
        {
            promptVisible = true;

            if (!showingSuccessMessage)
                RefreshPrompt();

            onPromptShown?.Invoke();
            PromptShown?.Invoke(this);
        }

        targetAlpha = 1f;
    }

    public void HidePrompt()
    {
        bool wasVisible = promptVisible;

        promptVisible = false;
        targetAlpha = 0f;

        UpdateProgress(0f);

        showingSuccessMessage = false;
        successMessageTimer = 0f;
        message = defaultMessage;

        if (wasVisible)
        {
            onPromptHidden?.Invoke();
            PromptHidden?.Invoke(this);
        }
    }

    private void RefreshPrompt()
    {
        if (interactionText == null)
            return;

        if (!string.IsNullOrEmpty(description))
        {
            interactionText.text =
                $"<b>{title}</b>\n{message}\n<size=80%>{description}</size>";
        }
        else
        {
            interactionText.text = $"<b>{title}</b>\n{message}";
        }
    }

    public void UpdateProgress(float progress)
    {
        if (holdProgress != null)
            holdProgress.fillAmount = Mathf.Clamp01(progress);
    }


    public void UpdatePromptFacing(Transform cameraTransform)
    {
        if (!faceCamera ||
            promptFacingTransform == null ||
            cameraTransform == null ||
            !hasInitialPromptRotation)
            return;

        Vector3 direction =
            cameraTransform.position - promptFacingTransform.position;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        // Correct the direction if the prompt's front is reversed.
        if (invertFacing)
            direction = -direction;

        Vector3 initialForward =
            initialPromptRotation * Vector3.forward;

        Vector3 limitedForward = Vector3.RotateTowards(
            initialForward,
            direction,
            maxFacingAngle * Mathf.Deg2Rad,
            0f
        );

        if (limitedForward.sqrMagnitude < 0.0001f)
            return;

        promptFacingTransform.rotation =
            Quaternion.LookRotation(limitedForward, Vector3.up);
    }


    public void OnInteract()
    {
        if (!IsInteractable)
            return;

        hasBeenUsed = true;

        if (interactionAudio != null)
            interactionAudio.Play();

        onInteraction?.Invoke();

        // Notify scripts subscribed to this interaction.
        InteractionExecuted?.Invoke(this);

        Debug.Log("Interacted with: " + gameObject.name);

        if (showSuccessMessage && promptVisible &&
            interactionText != null)
        {
            interactionText.text = successMessage;
            successMessageTimer = successMessageDuration;
            showingSuccessMessage = successMessageDuration > 0f;

            if (!showingSuccessMessage)
                RefreshPrompt();
        }
    }

    public void Interact()
    {
        OnInteract();
    }


    public void SetInteractable(bool state)
    {
        if (isInteractable == state)
            return;

        isInteractable = state;

        onAvailabilityChanged?.Invoke();
        AvailabilityChanged?.Invoke(this, IsInteractable);

        if (!IsInteractable)
            HidePrompt();
    }


    public void ResetInteraction()
    {
        hasBeenUsed = false;
        showingSuccessMessage = false;
        successMessageTimer = 0f;
        message = defaultMessage;

        if (promptVisible)
            RefreshPrompt();
    }
}
