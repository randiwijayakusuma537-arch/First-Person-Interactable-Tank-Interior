
using System.Collections.Generic;
using UnityEngine;

public class InteractionAnimationListener : MonoBehaviour
{
    public enum AnimationAction
    {
        Trigger,
        SetBool,
        ToggleBool,
        SetInteger,
        SetFloat,
        PlayState
    }

    public enum TargetFilter
    {
        ThisObjectOrChildren,
        AnyInteraction,
        SpecificInteractable
    }

    public enum TriggerSelection
    {
        FireAllTriggers,
        FireOneTrigger,
        ToggleBetweenTwoTriggers
    }

    [System.Serializable]
    public class TriggerEntry
    {
        [Tooltip("Exact Trigger parameter name from Animator > Parameters.")]
        public string triggerName = "Open";
    }

    [Header("1. CONNECTIONS")]
    [SerializeField] private Interactor interactor;
    [SerializeField] private Animator animator;

    [Header("2. WHICH OBJECT ACTIVATES THIS?")]
    [SerializeField]
    private TargetFilter targetFilter =
        TargetFilter.ThisObjectOrChildren;

    [SerializeField] private Interactable specificInteractable;

    [Header("3. ANIMATOR PARAMETERS")]
    [SerializeField]
    private AnimationAction action =
        AnimationAction.Trigger;

    [Tooltip("Choose how trigger names are used.")]
    [SerializeField]
    private TriggerSelection triggerSelection =
        TriggerSelection.FireOneTrigger;

    [Tooltip("Add your Animator Trigger parameter names here.")]
    [SerializeField]
    private List<TriggerEntry> triggerNames =
        new List<TriggerEntry>
        {
            new TriggerEntry { triggerName = "Open" },
            new TriggerEntry { triggerName = "Close" }
        };

    [Tooltip("Used by FireOneTrigger. Starts at element 0.")]
    [SerializeField] private int selectedTriggerIndex = 0;

    [Header("Other Animator Parameter Settings")]
    [Tooltip("Used by SetBool, ToggleBool, SetInteger and SetFloat.")]
    [SerializeField] private string parameterName = "IsOpen";

    [SerializeField] private bool boolValue = true;
    [SerializeField] private int integerValue = 1;
    [SerializeField] private float floatValue = 1f;

    [Header("Direct State Playback")]
    [Tooltip("Animator state name or full path. Only used by PlayState.")]
    [SerializeField] private string stateName = "Base Layer.Open";

    [SerializeField] private int layer = 0;

    [Range(0f, 1f)]
    [SerializeField] private float normalizedTime = 0f;

    [Header("Debug")]
    [SerializeField] private bool showWarnings = true;

    private int nextToggleTriggerIndex;

    private void OnEnable()
    {
        if (interactor != null)
            interactor.Interacted += HandleInteraction;
        else
            Warn("Assign your Player's Interactor component.");
    }

    private void OnDisable()
    {
        if (interactor != null)
            interactor.Interacted -= HandleInteraction;
    }

    private void HandleInteraction(Interactable target)
    {
        if (target == null || !MatchesTarget(target))
            return;

        if (animator == null)
        {
            Warn("Assign the Animator component you want to control.");
            return;
        }

        switch (action)
        {
            case AnimationAction.Trigger:
                HandleTriggers();
                break;

            case AnimationAction.SetBool:
                if (HasParameter(parameterName,
                    AnimatorControllerParameterType.Bool))
                    animator.SetBool(parameterName, boolValue);
                break;

            case AnimationAction.ToggleBool:
                if (HasParameter(parameterName,
                    AnimatorControllerParameterType.Bool))
                {
                    animator.SetBool(
                        parameterName,
                        !animator.GetBool(parameterName));
                }
                break;

            case AnimationAction.SetInteger:
                if (HasParameter(parameterName,
                    AnimatorControllerParameterType.Int))
                    animator.SetInteger(parameterName, integerValue);
                break;

            case AnimationAction.SetFloat:
                if (HasParameter(parameterName,
                    AnimatorControllerParameterType.Float))
                    animator.SetFloat(parameterName, floatValue);
                break;

            case AnimationAction.PlayState:
                if (string.IsNullOrWhiteSpace(stateName))
                {
                    Warn("Animator State Name is empty.");
                    return;
                }

                animator.Play(stateName, layer, normalizedTime);
                break;
        }
    }

    private void HandleTriggers()
    {
        if (triggerNames == null || triggerNames.Count == 0)
        {
            Warn("Add at least one name under Animator Parameters > Trigger Names.");
            return;
        }

        switch (triggerSelection)
        {
            case TriggerSelection.FireAllTriggers:
                foreach (TriggerEntry entry in triggerNames)
                    FireTrigger(entry);
                break;

            case TriggerSelection.FireOneTrigger:
                if (selectedTriggerIndex < 0 ||
                    selectedTriggerIndex >= triggerNames.Count)
                {
                    Warn("Selected Trigger Index is outside the list.");
                    return;
                }

                FireTrigger(triggerNames[selectedTriggerIndex]);
                break;

            case TriggerSelection.ToggleBetweenTwoTriggers:
                if (triggerNames.Count < 2)
                {
                    Warn("Toggle Between Two Triggers requires at least two entries.");
                    return;
                }

                int index = nextToggleTriggerIndex;
                FireTrigger(triggerNames[index]);

                // Advance only if the selected parameter was valid.
                if (HasParameter(
                    triggerNames[index]?.triggerName,
                    AnimatorControllerParameterType.Trigger))
                {
                    nextToggleTriggerIndex = 1 - nextToggleTriggerIndex;
                }
                break;
        }
    }

    private void FireTrigger(TriggerEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.triggerName))
        {
            Warn("A Trigger Name entry is empty.");
            return;
        }

        if (HasParameter(entry.triggerName,
            AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(entry.triggerName);
        }
    }

    private bool MatchesTarget(Interactable target)
    {
        switch (targetFilter)
        {
            case TargetFilter.AnyInteraction:
                return true;

            case TargetFilter.SpecificInteractable:
                return specificInteractable != null &&
                       target == specificInteractable;

            case TargetFilter.ThisObjectOrChildren:
                return target.transform == transform ||
                       target.transform.IsChildOf(transform);
        }

        return false;
    }

    private bool HasParameter(
        string requestedName,
        AnimatorControllerParameterType expectedType)
    {
        if (animator == null ||
            string.IsNullOrWhiteSpace(requestedName))
            return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == requestedName &&
                parameter.type == expectedType)
                return true;
        }

        Warn(
            $"Animator parameter '{requestedName}' with type " +
            $"'{expectedType}' was not found on Animator '{animator.name}'. " +
            "Check Animator > Parameters. Use the parameter name, " +
            "not the GameObject or Controller asset name."
        );

        return false;
    }

    private void Warn(string message)
    {
        if (showWarnings)
        {
            Debug.LogWarning(
                $"[{nameof(InteractionAnimationListener)}] {message}",
                this);
        }
    }
}
