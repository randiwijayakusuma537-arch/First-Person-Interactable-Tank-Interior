// ============================================================================
//  AdvancedFirstPersonController.cs
//  A feature-rich, fully tweakable first person controller for Unity.
//
//  QUICK SETUP
//   1. Create an empty GameObject (or capsule) -> add this script.
//      A CharacterController is added automatically.
//   2. Make your Main Camera a CHILD of that object (position it at eye level).
//   3. Drag the camera into "Player Camera" (or leave empty: it auto-finds one).
//   4. Put the player on its own layer and untick that layer in
//      "Ground Mask" / "Obstruction Mask".
//   5. Uses the legacy Input Manager (Project Settings > Player > Active Input
//      Handling = "Input Manager (Old)" or "Both").
//
//  FEATURES
//   Movement  : walk / sprint / slow-walk / crouch / prone, acceleration &
//               deceleration, air control, directional speed multipliers,
//               slope speed scaling, slope projection, steep-slope sliding,
//               moving platforms, rigidbody pushing, external impulses,
//               named speed modifiers (buffs / debuffs)
//   Jumping   : coyote time, jump buffer, variable jump height, air jumps,
//               fall gravity multiplier, terminal velocity, head-bump
//   Stamina   : drain / regen / exhaustion
//   Stances   : toggle OR hold, ceiling checks, smooth collider resize
//   Slide     : momentum slide with friction, slope boost, steering, jump-out
//   Mantle    : automatic ledge climbing
//   Camera    : head bob, breathing, strafe tilt, landing dip spring, lean with
//               wall check, sprint / slide / zoom FOV, recoil, trauma shake
//   Zoom      : hold / toggle ADS with sensitivity + speed scaling
//   Audio     : footsteps per surface tag, jump & land sounds
//   Events    : UnityEvents for jump, land, footsteps, stance, sprint, slide...
//   Debug     : on-screen overlay + gizmos
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("Character/Advanced First Person Controller")]
public class AdvancedFirstPersonController : MonoBehaviour
{
    // ------------------------------------------------------------------ types
    public enum Stance { Standing = 0, Crouching = 1, Prone = 2 }
    public enum InputMode { Toggle, Hold }
    public enum MovementState { Idle, Walking, Sprinting, Crouching, Prone, Sliding, Airborne, Mantling }

    [Serializable] public class FloatEvent : UnityEvent<float> { }
    [Serializable] public class StringEvent : UnityEvent<string> { }
    [Serializable] public class StanceEvent : UnityEvent<Stance> { }

    [Serializable]
    public class SurfaceSound
    {
        [Tooltip("Tag of the ground object this sound set is used for.")]
        public string surfaceTag = "Untagged";
        public AudioClip[] footsteps;
    }

    // ============================================================ SETTINGS ===

    [Header("References")]
    [Tooltip("Camera transform. MUST be a child of this object. Auto-detected if empty.")]
    public Transform playerCamera;
    [Tooltip("Layers counted as walkable ground.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Layers that block stand-up, leaning and mantling. Exclude the player's own layer.")]
    public LayerMask obstructionMask = ~0;

    [Header("Character Controller")]
    [Tooltip("If on, the values below are written to the CharacterController at start.")]
    public bool applyControllerSettings = true;
    [Range(0f, 89f)] public float slopeLimit = 45f;
    [Min(0f)] public float stepOffset = 0.35f;
    [Min(0.0001f)] public float skinWidth = 0.05f;

    [Header("Control")]
    public bool movementEnabled = true;
    public bool lookEnabled = true;
    [Tooltip("Pause all controller logic while Time.timeScale is 0.")]
    public bool pauseWhenTimeFrozen = true;

    // ------------------------------------------------------------------ input
    [Header("Input - Axes")]
    public string horizontalAxis = "Horizontal";
    public string verticalAxis = "Vertical";
    public string mouseXAxis = "Mouse X";
    public string mouseYAxis = "Mouse Y";
    [Range(0f, 0.5f)] public float inputDeadzone = 0.1f;

    [Header("Input - Keys")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.C;
    public KeyCode proneKey = KeyCode.Z;
    public KeyCode slowWalkKey = KeyCode.LeftControl;
    public KeyCode leanLeftKey = KeyCode.Q;
    public KeyCode leanRightKey = KeyCode.E;
    public KeyCode zoomKey = KeyCode.Mouse1;
    public KeyCode unlockCursorKey = KeyCode.Escape;

    [Header("Input - Modes")]
    public InputMode sprintMode = InputMode.Hold;
    public InputMode crouchMode = InputMode.Toggle;
    public InputMode proneMode = InputMode.Toggle;
    public InputMode zoomMode = InputMode.Hold;

    // --------------------------------------------------------------- movement
    [Header("Movement - Speeds (m/s)")]
    [Min(0f)] public float walkSpeed = 4.5f;
    [Min(0f)] public float sprintSpeed = 7.5f;
    [Min(0f)] public float slowWalkSpeed = 2.5f;
    [Min(0f)] public float crouchSpeed = 2.5f;
    [Min(0f)] public float proneSpeed = 1.2f;
    [Tooltip("Master multiplier applied to every ground/air speed.")]
    [Min(0f)] public float globalSpeedMultiplier = 1f;
    public bool canSprint = true;
    public bool enableSlowWalk = true;
    [Tooltip("If on, sprinting needs forward input. If off, any direction works.")]
    public bool sprintForwardOnly = true;

    [Header("Movement - Direction Multipliers")]
    [Range(0.1f, 1.5f)] public float backwardSpeedMultiplier = 0.7f;
    [Range(0.1f, 1.5f)] public float strafeSpeedMultiplier = 0.9f;

    [Header("Movement - Acceleration")]
    [Min(0f)] public float groundAcceleration = 14f;
    [Min(0f)] public float groundDeceleration = 18f;
    [Tooltip("Acceleration while airborne. Lower = floatier, higher = more air control.")]
    [Min(0f)] public float airAcceleration = 4f;
    [Tooltip("Deceleration while airborne with no input. 0 = full momentum is kept.")]
    [Min(0f)] public float airDeceleration = 0.5f;

    [Header("Movement - Slopes")]
    public bool projectMovementOnSlope = true;
    [Range(0f, 1f)] public float uphillSpeedPenalty = 0.25f;
    [Range(0f, 1f)] public float downhillSpeedBonus = 0.15f;
    public bool slideDownSteepSlopes = true;
    [Min(0f)] public float steepSlopeSlideSpeed = 8f;
    [Tooltip("Small downward push that keeps you glued to the ground.")]
    [Min(0f)] public float groundStickForce = 3f;
    [Tooltip("Extra distance below the feet used to detect ground.")]
    [Min(0f)] public float groundCheckDistance = 0.15f;

    [Header("Movement - External Forces")]
    [Tooltip("How fast knock-back / impulse velocity fades while on the ground.")]
    [Min(0f)] public float externalGroundDrag = 8f;
    [Tooltip("How fast it fades in the air.")]
    [Min(0f)] public float externalAirDrag = 0.4f;
    [Tooltip("Multiplier on gravity. 1 = normal, 0.2 = moon, -1 = reverse gravity.")]
    public float gravityScale = 1f;

    [Header("Moving Platforms & Physics")]
    public bool movingPlatformSupport = true;
    public bool rotateWithPlatform = true;
    public bool inheritPlatformMomentum = true;
    public bool pushRigidbodies = true;
    [Tooltip("How quickly pushed objects reach the push speed.")]
    [Min(0f)] public float pushStrength = 10f;
    [Range(0f, 2f)] public float pushSpeedFactor = 1f;
    [Tooltip("Heavier objects are pushed slower.")]
    [Min(0f)] public float pushMassResistance = 0.1f;

    // ------------------------------------------------------------------- jump
    [Header("Jump & Gravity")]
    [Min(0f)] public float jumpHeight = 1.2f;
    [Tooltip("Gravity strength (positive number, m/s^2).")]
    [Min(0f)] public float gravity = 20f;
    [Tooltip("Gravity multiplier while falling. Higher = snappier, less floaty.")]
    [Min(0.1f)] public float fallGravityMultiplier = 1.4f;
    [Min(1f)] public float terminalVelocity = 55f;
    [Tooltip("You can still jump this long after walking off a ledge.")]
    [Range(0f, 0.5f)] public float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this early before landing still counts.")]
    [Range(0f, 0.5f)] public float jumpBufferTime = 0.12f;
    [Range(0f, 1f)] public float jumpCooldown = 0.1f;
    [Tooltip("Releasing jump early cuts the jump short.")]
    public bool variableJumpHeight = true;
    [Min(1f)] public float lowJumpGravityMultiplier = 2.2f;
    [Tooltip("Extra jumps in the air (1 = double jump).")]
    [Min(0)] public int maxAirJumps = 0;
    [Range(0.1f, 2f)] public float airJumpHeightMultiplier = 0.9f;
    [Tooltip("Air jumps redirect your momentum toward the held direction.")]
    public bool airJumpRedirectsMomentum = true;
    public bool canJumpWhileCrouched = true;
    [Range(0.1f, 1.5f)] public float crouchJumpHeightMultiplier = 0.8f;
    [Tooltip("Pressing jump while prone stands you up instead.")]
    public bool jumpFromProneStandsUp = true;
    [Tooltip("Runtime multiplier on jump height (power-ups).")]
    public float jumpHeightMultiplier = 1f;

    [Header("Landing")]
    [Tooltip("Impacts slower than this (m/s) are ignored.")]
    [Min(0f)] public float minLandImpact = 3f;
    [Tooltip("Impacts faster than this cause a hard landing (slowdown).")]
    [Min(0f)] public float hardLandSpeed = 12f;
    [Min(0f)] public float hardLandSlowTime = 0.35f;
    [Range(0f, 1f)] public float hardLandSpeedMultiplier = 0.4f;

    // ---------------------------------------------------------------- stamina
    [Header("Stamina")]
    public bool useStamina = true;
    [Min(1f)] public float maxStamina = 100f;
    [Min(0f)] public float sprintStaminaDrain = 18f;
    [Min(0f)] public float staminaRegenRate = 12f;
    [Min(0f)] public float staminaRegenDelay = 1.2f;
    [Tooltip("Regen multiplier while standing still.")]
    [Min(0f)] public float idleRegenMultiplier = 1.5f;
    [Tooltip("After running dry you must regain this much before sprinting again.")]
    [Min(0f)] public float minStaminaToSprint = 15f;
    [Min(0f)] public float jumpStaminaCost = 0f;
    [Tooltip("Speed multiplier while exhausted.")]
    [Range(0.2f, 1f)] public float exhaustedSpeedMultiplier = 0.85f;

    // ---------------------------------------------------------------- stances
    [Header("Crouch & Prone")]
    public bool canCrouch = true;
    public bool canProne = true;
    [Tooltip("Collider height while crouching (standing height is read from the CharacterController).")]
    [Min(0.3f)] public float crouchHeight = 1.3f;
    [Min(0.3f)] public float proneHeight = 0.65f;
    [Tooltip("How fast the collider resizes (m/s).")]
    [Min(0.1f)] public float stanceTransitionSpeed = 6f;
    [Tooltip("0 = eye stays put, 1 = eye follows the top of the collider down.")]
    [Range(0f, 1.5f)] public float eyeHeightFollow = 1f;
    [Min(0f)] public float eyeSmoothTime = 0.05f;

    // ------------------------------------------------------------------ slide
    [Header("Slide")]
    public bool enableSlide = true;
    [Tooltip("Slide only starts while sprinting.")]
    public bool slideRequiresSprint = true;
    [Min(0f)] public float slideMinStartSpeed = 5f;
    [Tooltip("Speed boost applied when the slide starts.")]
    [Min(0.5f)] public float slideStartBoost = 1.2f;
    [Min(0f)] public float slideMaxSpeed = 11f;
    [Tooltip("Deceleration on flat ground (m/s^2).")]
    [Min(0f)] public float slideFriction = 5.5f;
    [Tooltip("Slide ends when speed drops below this.")]
    [Min(0f)] public float slideEndSpeed = 3f;
    [Tooltip("Hard time limit. 0 = no limit.")]
    [Min(0f)] public float slideMaxDuration = 1.6f;
    [Tooltip("Acceleration gained / lost per second on slopes.")]
    [Min(0f)] public float slideSlopeAcceleration = 18f;
    [Tooltip("How fast you can steer during a slide (deg/s). 0 = locked direction.")]
    [Min(0f)] public float slideSteerSpeed = 60f;
    [Min(0.3f)] public float slideColliderHeight = 1.0f;
    [Min(0f)] public float slideCooldown = 0.5f;
    [Tooltip("Extra camera drop while sliding.")]
    [Min(0f)] public float slideEyeDrop = 0.1f;
    public bool slideJumpCancels = true;
    [Range(0.5f, 2f)] public float slideJumpSpeedMultiplier = 1.1f;
    [Tooltip("Stay crouched after the slide ends (otherwise stand up).")]
    public bool crouchAfterSlide = true;

    // ----------------------------------------------------------------- mantle
    [Header("Mantle / Ledge Climb")]
    public bool enableMantle = true;
    [Tooltip("Must hold jump to grab a ledge.")]
    public bool mantleRequiresJumpInput = true;
    public bool mantleRequiresForwardInput = true;
    [Tooltip("Ledges lower than this above the feet are ignored.")]
    [Min(0f)] public float mantleMinHeight = 0.7f;
    [Tooltip("Ledges higher than this above the feet cannot be reached.")]
    [Min(0.1f)] public float mantleMaxHeight = 1.8f;
    [Tooltip("How far in front of the capsule the wall can be.")]
    [Min(0.05f)] public float mantleReach = 0.6f;
    [Tooltip("Cannot mantle while falling faster than this.")]
    [Min(0f)] public float mantleMaxFallSpeed = 12f;
    [Min(0.05f)] public float mantleBaseDuration = 0.25f;
    [Min(0f)] public float mantleTimePerMeter = 0.2f;
    [Tooltip("Fraction of the animation spent going straight up.")]
    [Range(0.1f, 0.9f)] public float mantleRiseFraction = 0.6f;
    [Min(0f)] public float mantleExitSpeed = 1.5f;
    [Min(0f)] public float mantleCooldown = 0.4f;
    [Header("Mantle - Crouch Finish & Camera Feedback")]
    [Tooltip("Automatically crouch during the climb, then request standing when the climb finishes.")]
    public bool mantleAutoCrouch = true;
    public bool mantleStayCrouchedAfter = true;
    [Tooltip("Extra trauma added when a mantle begins (uses the Camera Shake settings below).")]
    [Range(0f, 1f)] public float mantleStartShake = 0.12f;
    [Tooltip("Extra trauma added when the player lands on top of the ledge.")]
    [Range(0f, 1f)] public float mantleFinishShake = 0.18f;
    [Tooltip("Duration of additional procedural mantle camera noise, in seconds.")]
    [Min(0f)] public float mantleNoiseDuration = 0.28f;
    [Tooltip("Strength of additional random camera position noise, in metres.")]
    [Min(0f)] public float mantleNoisePosition = 0.012f;
    [Tooltip("Strength of additional random camera rotation noise, in degrees.")]
    [Min(0f)] public float mantleNoiseRotation = 0.8f;
    [Tooltip("Frequency of the additional mantle camera noise.")]
    [Min(0f)] public float mantleNoiseFrequency = 24f;

    // ------------------------------------------------------------- mouse look
    [Header("Mouse Look")]
    [Min(0f)] public float sensitivityX = 2f;
    [Min(0f)] public float sensitivityY = 2f;
    public bool invertY = false;
    [Tooltip("0 = raw input. Higher = smoother but laggier.")]
    [Min(0f)] public float lookSmoothing = 0f;
    [Range(-90f, 0f)] public float minPitch = -85f;
    [Range(0f, 90f)] public float maxPitch = 85f;

    [Header("Cursor")]
    public bool manageCursor = true;
    public bool lockCursorOnStart = true;
    public bool relockOnClick = true;
    [Tooltip("Mouse look only works while the cursor is locked.")]
    public bool requireCursorLock = true;

    // ------------------------------------------------------------------- zoom
    [Header("Zoom / Aim Down Sights")]
    public bool canZoom = true;
    [Range(10f, 100f)] public float zoomFov = 40f;
    [Min(0.1f)] public float zoomSpeed = 12f;
    [Range(0.05f, 1f)] public float zoomSensitivityMultiplier = 0.5f;
    [Range(0.1f, 1f)] public float zoomMoveSpeedMultiplier = 0.7f;
    public bool canSprintWhileZoomed = false;

    // -------------------------------------------------------------------- fov
    [Header("Field Of View")]
    public bool controlFov = true;
    public bool useCameraFovAsBase = true;
    [Range(30f, 130f)] public float baseFov = 75f;
    [Range(0f, 30f)] public float sprintFovBoost = 8f;
    [Range(0f, 30f)] public float slideFovBoost = 12f;
    [Min(0.1f)] public float fovChangeSpeed = 8f;
    [Tooltip("Add to this from other scripts for effects (speed boosts, damage...).")]
    public float extraFov = 0f;

    // ------------------------------------------------------------------- lean
    [Header("Peek / Lean")]
    public bool canLean = true;
    [Range(0f, 45f)] public float leanAngle = 12f;
    [Min(0f)] public float leanDistance = 0.3f;
    [Min(0.1f)] public float leanSmoothSpeed = 9f;
    public bool leanWallCheck = true;
    [Min(0f)] public float leanWallPadding = 0.15f;
    public bool leanWhileSprinting = false;
    public bool leanWhileAirborne = false;

    // -------------------------------------------------------------- camera fx
    [Header("Head Bob")]
    public bool enableHeadBob = true;
    [Tooltip("Steps per second at walk speed.")]
    [Min(0f)] public float bobFrequency = 1.9f;
    [Min(0f)] public float bobVerticalAmount = 0.035f;
    [Min(0f)] public float bobHorizontalAmount = 0.025f;
    [Range(0f, 3f)] public float bobRollAngle = 0.35f;
    [Min(0f)] public float bobSprintMultiplier = 1.35f;
    [Min(0f)] public float bobCrouchMultiplier = 0.6f;
    [Min(0f)] public float bobProneMultiplier = 0.4f;
    [Min(0f)] public float bobZoomMultiplier = 0.3f;
    [Min(0.1f)] public float bobFadeSpeed = 6f;

    [Header("Idle Breathing")]
    public bool enableBreathing = true;
    [Min(0f)] public float breathAmount = 0.004f;
    [Min(0f)] public float breathSpeed = 0.25f;
    [Min(0f)] public float breathSwayAngle = 0.15f;

    [Header("Camera Tilt")]
    [Tooltip("Roll when strafing (degrees).")]
    [Range(0f, 5f)] public float strafeTilt = 1.2f;

    [Header("Movement Lean (Forward Tilt)")]
    public bool enableMoveTilt = true;
    [Tooltip("Degrees the camera pitches DOWN at full speed while moving forward.")]
    [Range(0f, 10f)] public float moveTiltForwardAngle = 2.5f;
    [Tooltip("Degrees the camera pitches UP at full speed while moving backward.")]
    [Range(0f, 10f)] public float moveTiltBackwardAngle = 1.2f;
    [Tooltip("Camera pushes forward this many meters at full speed (the 'leaning in' feel).")]
    [Min(0f)] public float moveTiltForwardOffset = 0.03f;
    [Min(0f)] public float moveTiltBackwardOffset = 0.015f;
    [Tooltip("Speed that counts as 'full'. 0 = use Sprint Speed.")]
    [Min(0f)] public float moveTiltReferenceSpeed = 0f;
    [Tooltip("How quickly the tilt follows your movement.")]
    [Min(0.1f)] public float moveTiltSmoothSpeed = 7f;
    public bool moveTiltWhileAirborne = false;
    public bool moveTiltWhileSliding = false;

    [Header("Landing Camera Dip")]
    [Min(0f)] public float landingDipStrength = 0.25f;
    [Min(0f)] public float landingDipMax = 0.25f;
    [Min(1f)] public float landingSpringStiffness = 140f;
    [Min(0f)] public float landingSpringDamping = 13f;

    [Header("Recoil")]
    [Tooltip("How quickly the target recoil recovers.")]
    [Min(0f)] public float recoilRecoverySpeed = 6f;
    [Tooltip("How quickly the camera follows the recoil target.")]
    [Min(0f)] public float recoilSnappiness = 18f;

    [Header("Camera Shake (Trauma)")]
    [Min(0f)] public float traumaDecay = 1.2f;
    [Min(0f)] public float maxShakeAngle = 4f;
    [Min(0f)] public float maxShakeOffset = 0.06f;
    [Min(0f)] public float shakeFrequency = 22f;

    // ------------------------------------------------------------------ audio
    [Header("Audio")]
    public bool enableAudio = true;
    public bool autoCreateAudioSource = true;
    public AudioClip[] defaultFootsteps;
    public SurfaceSound[] surfaceSounds;
    public AudioClip[] jumpClips;
    public AudioClip[] landClips;
    [Range(0f, 1f)] public float footstepVolume = 0.6f;
    [Range(0f, 1f)] public float crouchFootstepVolume = 0.25f;
    [Range(0f, 1f)] public float proneFootstepVolume = 0.12f;
    [Range(0f, 1f)] public float sprintFootstepVolume = 0.85f;
    [Range(0f, 1f)] public float jumpLandVolume = 0.8f;
    [Range(0f, 0.5f)] public float pitchVariation = 0.08f;

    [Header("Footsteps")]
    public bool enableFootsteps = true;
    [Tooltip("Distance travelled between footsteps (meters).")]
    [Min(0.1f)] public float walkStepDistance = 1.7f;
    [Min(0.1f)] public float sprintStepDistance = 2.3f;
    [Min(0.1f)] public float crouchStepDistance = 1.2f;
    [Min(0.1f)] public float proneStepDistance = 0.8f;

    // ----------------------------------------------------------------- events
    [Header("Events")]
    public UnityEvent onJump = new UnityEvent();
    public UnityEvent onAirJump = new UnityEvent();
    [Tooltip("Passes the impact speed (m/s). Use it for fall damage.")]
    public FloatEvent onLand = new FloatEvent();
    [Tooltip("Passes the tag of the ground surface.")]
    public StringEvent onFootstep = new StringEvent();
    public StanceEvent onStanceChanged = new StanceEvent();
    public UnityEvent onSprintStart = new UnityEvent();
    public UnityEvent onSprintStop = new UnityEvent();
    public UnityEvent onSlideStart = new UnityEvent();
    public UnityEvent onSlideEnd = new UnityEvent();
    public UnityEvent onMantleStart = new UnityEvent();
    public UnityEvent onMantleEnd = new UnityEvent();
    public UnityEvent onStaminaDepleted = new UnityEvent();

    [Header("Debug")]
    public bool showDebugOverlay = false;
    public bool drawGizmos = true;

    // ============================================================ PUBLIC API ===

    public bool IsGrounded => grounded;
    public bool IsSprinting => isSprinting;
    public bool IsSliding => isSliding;
    public bool IsMantling => isMantling;
    public bool IsZooming => isZooming;
    public bool IsCrouching => stance == Stance.Crouching;
    public bool IsProne => stance == Stance.Prone;
    public bool IsExhausted => exhausted;
    public bool IsMoving => actualHorizontalSpeed > 0.1f;
    public bool IsOnSteepSlope => isOnSteepSlope;
    public Stance CurrentStance => stance;
    public float HorizontalSpeed => actualHorizontalSpeed;
    public float VerticalSpeed => verticalVelocity;
    public Vector3 Velocity => horizontalVelocity + externalVelocity + Vector3.up * verticalVelocity;
    public float Stamina => stamina;
    public float Stamina01 => stamina / Mathf.Max(maxStamina, 0.0001f);
    public float SlopeAngle => slopeAngle;
    public Vector3 GroundNormal => groundNormal;
    public Collider GroundCollider => groundCollider;
    public string GroundTag => groundCollider != null ? groundCollider.tag : "Untagged";
    public float CameraPitch => pitch;
    public float CurrentHeight => currentHeight;
    public Camera PlayerCameraComponent => cam;

    public MovementState State
    {
        get
        {
            if (isMantling) return MovementState.Mantling;
            if (isSliding) return MovementState.Sliding;
            if (!grounded) return MovementState.Airborne;
            if (stance == Stance.Prone) return MovementState.Prone;
            if (stance == Stance.Crouching) return MovementState.Crouching;
            if (isSprinting) return MovementState.Sprinting;
            return IsMoving ? MovementState.Walking : MovementState.Idle;
        }
    }

    public void SetMovementEnabled(bool value)
    {
        movementEnabled = value;
        if (!value) { sprintToggled = false; }
    }

    public void SetLookEnabled(bool value) { lookEnabled = value; }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>Instantly adds velocity (knock-back, explosions, launch pads, jump pads).</summary>
    public void AddImpulse(Vector3 velocityChange)
    {
        externalVelocity += new Vector3(velocityChange.x, 0f, velocityChange.z);
        if (Mathf.Abs(velocityChange.y) > 0.001f)
        {
            verticalVelocity = velocityChange.y > 0f ? Mathf.Max(verticalVelocity, 0f) + velocityChange.y : verticalVelocity + velocityChange.y;
            grounded = false;
            lastJumpTime = Time.time;
            lastGroundedTime = -999f;
        }
    }

    /// <summary>Kick the camera. up = degrees upward, side = degrees sideways.</summary>
    public void AddRecoil(float up, float side = 0f)
    {
        recoilTarget += new Vector2(-up, side);
    }

    /// <summary>Add camera shake. 0..1 (stacks, clamped to 1).</summary>
    public void AddTrauma(float amount) { trauma = Mathf.Clamp01(trauma + amount); }

    public void Teleport(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        ResetMotion();
    }

    public void Teleport(Vector3 position, float yaw, float newPitch = 0f)
    {
        cc.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cc.enabled = true;
        pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        ResetMotion();
    }

    public void SetLookRotation(float yaw, float newPitch)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
    }

    public void ResetMotion()
    {
        horizontalVelocity = Vector3.zero;
        externalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        platform = null;
        if (isSliding) EndSlide();
        isMantling = false;
    }

    public void SetStance(Stance newStance) { desiredStance = newStance; }

    public void SetStamina(float value) { stamina = Mathf.Clamp(value, 0f, maxStamina); }
    public void AddStamina(float value) { SetStamina(stamina + value); }

    public void ForceStopSlide() { if (isSliding) EndSlide(); }



    // ---------------------------------------------------------------- Motion lock (used by PlayerSeat)

    bool motionLocked;
    Transform lockedIgnoreRoot;
    bool lockedIgnoreAllHeadroom;   // NEW

    public bool IsMotionLocked => motionLocked;

    public void SetMotionLocked(bool locked, Transform ignoreRoot = null, bool ignoreAllCollision = false)
    {
        lockedIgnoreRoot = locked ? ignoreRoot : null;
        lockedIgnoreAllHeadroom = locked && ignoreAllCollision;   // NEW

        if (motionLocked == locked)
            return;
        // ... rest unchanged

        motionLocked = locked;

        if (locked)
        {
            if (isSliding) EndSlide();
            isMantling = false;
            sprintToggled = false;
            SetSprinting(false);

            horizontalVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            actualHorizontalSpeed = 0f;
            platform = null;
            isOnSteepSlope = false;
            grounded = true;      // lets lean work
            cc.enabled = false;   // so the capsule can't collide with the vehicle it sits in
        }
        else
        {
            cc.enabled = true;
            grounded = false;     // re-detected next frame
            verticalVelocity = 0f;
        }
    }

    bool IsIgnoredWhileLocked(Collider c)
    {
        return motionLocked && lockedIgnoreRoot != null && c.transform.IsChildOf(lockedIgnoreRoot);
    }

    void UpdateLocked(float dt)
    {
        moveInput = Vector2.zero;
        hasMoveInput = false;
        jumpPressed = false;
        jumpHeld = false;
        isSlowWalking = false;

        UpdateZoom(dt);
        HandleStanceInput();
        UpdateStance(dt);
        UpdateStamina(dt);
    }



    /// <summary>Register a named speed multiplier (e.g. "slowed", 0.5). Re-adding the same id replaces it.</summary>
    public void AddSpeedModifier(string id, float multiplier)
    {
        speedModifiers[id] = multiplier;
        RecalculateSpeedModifier();
    }

    public void RemoveSpeedModifier(string id)
    {
        if (speedModifiers.Remove(id)) RecalculateSpeedModifier();
    }

    public void ClearSpeedModifiers()
    {
        speedModifiers.Clear();
        RecalculateSpeedModifier();
    }

    // ============================================================== PRIVATE ===

    CharacterController cc;
    Camera cam;
    AudioSource audioSource;

    // motion
    Vector3 horizontalVelocity;
    Vector3 externalVelocity;
    float verticalVelocity;
    float actualHorizontalSpeed;
    CollisionFlags lastFlags;

    // input snapshot
    Vector2 moveInput;
    bool hasMoveInput;
    bool jumpPressed;
    bool jumpHeld;

    // ground
    bool grounded, wasGrounded, isOnSteepSlope;
    Vector3 groundNormal = Vector3.up;
    float slopeAngle;
    Collider groundCollider;
    float lastGroundedTime = -999f;
    float lastJumpTime = -999f;
    float lastJumpPressTime = -999f;
    float airTime;
    int airJumpsUsed;
    float hardLandTimer;

    // platform
    Transform platform;
    Vector3 platformLocalPoint;
    Quaternion platformRotation;
    Vector3 platformVelocity;

    // look
    float pitch;
    Vector2 smoothedLook;
    Vector2 recoilTarget, recoilCurrent;

    // stance
    Stance stance = Stance.Standing;
    Stance desiredStance = Stance.Standing;
    float defaultHeight, currentHeight, bottomLocalY;
    Vector3 defaultCenter;
    readonly Collider[] overlapBuffer = new Collider[8];

    // sprint / stamina
    bool isSprinting, sprintToggled, exhausted, isSlowWalking;
    float stamina;
    float lastStaminaUseTime = -999f;

    // slide
    bool isSliding;
    Vector3 slideDir;
    float slideSpeed, slideTimer, slideCooldownEnd, slideBlend;

    // mantle
    bool isMantling;
    Vector3 mantleStart, mantleMid, mantleEnd;
    float mantleProgress, mantleDurationCurrent, nextMantleTime;
    float mantleNoiseTimer;
    float mantleNoiseSeed;
    bool mantleRequestedCrouch;

    // zoom
    bool isZooming, zoomToggled;
    float zoomBlend, sprintFovBlend, slideFovBlend, currentBaseFov;

    // camera
    float baseCamX, baseCamZ, standingEyeY, eyeY, eyeVelocity;
    float leanAmount, leanOffset, leanScale;
    float bobPhase, bobBlend, strafeTiltCurrent;
    float moveTiltPitch, moveTiltOffsetZ;
    float landingOffset, landingVelocity;
    float trauma;

    // footsteps / audio
    float stepAccumulator;
    int lastClipIndex = -1;

    // modifiers
    readonly Dictionary<string, float> speedModifiers = new Dictionary<string, float>();
    float speedModifierProduct = 1f;

    // ---------------------------------------------------------------- helpers
    Vector3 FeetPosition =>
        transform.TransformPoint(new Vector3(cc.center.x, cc.center.y - cc.height * 0.5f, cc.center.z));

    float WorldRadius =>
        cc.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));

    float WorldScaleY => Mathf.Abs(transform.lossyScale.y);

    static float ExpLerp(float rate, float dt) { return 1f - Mathf.Exp(-rate * dt); }

    void RecalculateSpeedModifier()
    {
        speedModifierProduct = 1f;
        foreach (var kv in speedModifiers) speedModifierProduct *= kv.Value;
    }

    // ================================================================= UNITY ===

    void Reset()
    {
        var c = GetComponent<CharacterController>();
        if (c != null)
        {
            c.height = 2f;
            c.radius = 0.4f;
            c.center = new Vector3(0f, 1f, 0f);
        }
    }

    void OnValidate()
    {
        gravity = Mathf.Abs(gravity);
        crouchHeight = Mathf.Max(0.3f, crouchHeight);
        proneHeight = Mathf.Max(0.3f, proneHeight);
        slideColliderHeight = Mathf.Max(0.3f, slideColliderHeight);
        mantleMaxHeight = Mathf.Max(mantleMinHeight + 0.1f, mantleMaxHeight);
        maxStamina = Mathf.Max(1f, maxStamina);
        minPitch = Mathf.Min(minPitch, 0f);
        maxPitch = Mathf.Max(maxPitch, 0f);
    }

    void Awake()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Debug.LogError("AdvancedFirstPersonController uses the legacy Input Manager. Set Project Settings > Player > Active Input Handling to 'Both' or 'Input Manager (Old)'.", this);
        enabled = false;
        return;
#endif
        cc = GetComponent<CharacterController>();

        if (applyControllerSettings)
        {
            cc.slopeLimit = slopeLimit;
            cc.stepOffset = stepOffset;
            cc.skinWidth = skinWidth;
        }

        if (playerCamera == null)
        {
            Camera c = GetComponentInChildren<Camera>();
            if (c != null) playerCamera = c.transform;
            else if (Camera.main != null) playerCamera = Camera.main.transform;
        }

        if (playerCamera == null)
        {
            Debug.LogError("AdvancedFirstPersonController: no camera found. Make a Camera a child of the player and assign it.", this);
            enabled = false;
            return;
        }

        if (!playerCamera.IsChildOf(transform))
            Debug.LogWarning("AdvancedFirstPersonController: Player Camera should be a child of the player object.", this);

        cam = playerCamera.GetComponent<Camera>();
        if (cam != null && useCameraFovAsBase) baseFov = cam.fieldOfView;
        currentBaseFov = baseFov;

        // collider bookkeeping (keeps the feet at the same height when resizing)
        defaultHeight = cc.height;
        defaultCenter = cc.center;
        currentHeight = defaultHeight;
        bottomLocalY = defaultCenter.y - defaultHeight * 0.5f;

        baseCamX = playerCamera.localPosition.x;
        baseCamZ = playerCamera.localPosition.z;
        standingEyeY = playerCamera.localPosition.y;
        eyeY = standingEyeY;

        stamina = maxStamina;

        if (enableAudio)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null && autoCreateAudioSource)
                audioSource = gameObject.AddComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }
    }

    void Start()
    {
        if (manageCursor && lockCursorOnStart) SetCursorLocked(true);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (pauseWhenTimeFrozen && Time.timeScale <= 0f) return;
        if (dt <= 0f) return;

        HandleCursor();
        HandleLook(dt);

        if (motionLocked)
        {
            UpdateLocked(dt);
            return;
        }

        UpdateGround(dt);


        UpdateGround(dt);
        ApplyPlatformMotion(dt);
        ReadMovementInput();
        UpdateZoom(dt);
        HandleStanceInput();
        UpdateStance(dt);
        UpdateSprint();
        HandleMantleDetection();

        if (isMantling)
        {
            UpdateMantle(dt);
        }
        else
        {
            HandleJump();
            UpdateHorizontal(dt);
            UpdateVertical(dt);
            MoveCharacter(dt);
        }

        UpdateStamina(dt);
        UpdateFootsteps(dt);
        RecordPlatform();
    }

    void LateUpdate()
    {
        if (pauseWhenTimeFrozen && Time.timeScale <= 0f) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateFov(dt);
        UpdateCamera(dt);
    }

    // ================================================================ CURSOR ===

    void HandleCursor()
    {
        if (!manageCursor) return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            if (Input.GetKeyDown(unlockCursorKey)) SetCursorLocked(false);
        }
        else if (relockOnClick && Input.GetMouseButtonDown(0))
        {
            SetCursorLocked(true);
        }
    }

    // ================================================================== LOOK ===

    void HandleLook(float dt)
    {
        if (!lookEnabled) return;
        if (requireCursorLock && Cursor.lockState != CursorLockMode.Locked) return;

        float zoomScale = Mathf.Lerp(1f, zoomSensitivityMultiplier, zoomBlend);
        Vector2 raw = new Vector2(
            Input.GetAxis(mouseXAxis) * sensitivityX,
            Input.GetAxis(mouseYAxis) * sensitivityY) * zoomScale;

        if (lookSmoothing > 0f)
            smoothedLook = Vector2.Lerp(smoothedLook, raw, ExpLerp(lookSmoothing, dt));
        else
            smoothedLook = raw;

        transform.Rotate(0f, smoothedLook.x, 0f, Space.Self);

        pitch += invertY ? smoothedLook.y : -smoothedLook.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    // =============================================================== GROUND ===

    void UpdateGround(float dt)
    {
        wasGrounded = grounded;

        bool ccGround = cc.isGrounded;
        float r = WorldRadius * 0.95f;
        Vector3 feet = FeetPosition;

        Vector3 origin = feet + Vector3.up * (r + 0.05f);
        RaycastHit sphereHit;
        bool sphereGround = Physics.SphereCast(origin, r, Vector3.down, out sphereHit,
            0.05f + groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

        groundNormal = Vector3.up;
        groundCollider = null;
        slopeAngle = 0f;

        if (sphereGround)
        {
            groundCollider = sphereHit.collider;
            // a straight ray gives the exact slope, avoiding bogus edge normals from the sphere
            RaycastHit rayHit;
            if (Physics.Raycast(feet + Vector3.up * 0.2f, Vector3.down, out rayHit,
                0.2f + groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
            {
                groundNormal = rayHit.normal;
                groundCollider = rayHit.collider;
            }
            slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
        }

        bool candidate = ccGround || sphereGround;
        if (verticalVelocity > 0.1f) candidate = false; // still rising from a jump

        isOnSteepSlope = candidate && sphereGround && slopeAngle > cc.slopeLimit + 0.5f;
        grounded = candidate && !isOnSteepSlope;

        if (grounded)
        {
            lastGroundedTime = Time.time;
            if (!wasGrounded)
            {
                float impact = Mathf.Max(0f, -verticalVelocity);
                OnLanded(impact);
            }
            airTime = 0f;
        }
        else
        {
            airTime += dt;
        }

        if (hardLandTimer > 0f) hardLandTimer -= dt;
    }

    void OnLanded(float impact)
    {
        airJumpsUsed = 0;
        if (impact < minLandImpact) return;

        landingVelocity -= impact * landingDipStrength;
        if (hardLandSpeed > 0f && impact >= hardLandSpeed) hardLandTimer = hardLandSlowTime;

        PlayRandom(landClips, jumpLandVolume * Mathf.Clamp01(impact / 15f + 0.3f));
        onLand.Invoke(impact);
    }

    // ============================================================= PLATFORM ===

    void ApplyPlatformMotion(float dt)
    {
        if (!movingPlatformSupport) { platform = null; return; }

        Transform p = null;
        if (grounded && groundCollider != null)
        {
            p = groundCollider.attachedRigidbody != null
                ? groundCollider.attachedRigidbody.transform
                : groundCollider.transform;
            if (p.gameObject.isStatic) p = null;
        }

        if (p == null)
        {
            platform = null;
            return;
        }

        if (p != platform)
        {
            platform = p;
            platformLocalPoint = p.InverseTransformPoint(transform.position);
            platformRotation = p.rotation;
            platformVelocity = Vector3.zero;
            return;
        }

        Vector3 world = platform.TransformPoint(platformLocalPoint);
        Vector3 delta = world - transform.position;
        platformVelocity = delta / dt;
        if (delta.sqrMagnitude > 1e-8f) cc.Move(delta);

        if (rotateWithPlatform)
        {
            Quaternion d = platform.rotation * Quaternion.Inverse(platformRotation);
            Vector3 f = d * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f)
            {
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                transform.Rotate(0f, yaw, 0f, Space.World);
            }
        }
    }

    void RecordPlatform()
    {
        if (platform == null) return;
        platformLocalPoint = platform.InverseTransformPoint(transform.position);
        platformRotation = platform.rotation;
    }

    // =============================================================== INPUT ===

    void ReadMovementInput()
    {
        moveInput = Vector2.zero;
        if (movementEnabled)
        {
            moveInput = new Vector2(Input.GetAxisRaw(horizontalAxis), Input.GetAxisRaw(verticalAxis));
            if (moveInput.magnitude < inputDeadzone) moveInput = Vector2.zero;
            else if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
        }

        hasMoveInput = moveInput.sqrMagnitude > 0.0001f;
        jumpPressed = movementEnabled && Input.GetKeyDown(jumpKey);
        jumpHeld = movementEnabled && Input.GetKey(jumpKey);
        if (jumpPressed) lastJumpPressTime = Time.time;

        isSlowWalking = movementEnabled && enableSlowWalk && Input.GetKey(slowWalkKey);
    }

    // ================================================================= ZOOM ===

    void UpdateZoom(float dt)
    {
        bool want = false;
        if (canZoom && lookEnabled)
        {
            if (zoomMode == InputMode.Toggle)
            {
                if (Input.GetKeyDown(zoomKey)) zoomToggled = !zoomToggled;
                want = zoomToggled;
            }
            else
            {
                want = Input.GetKey(zoomKey);
            }
        }
        else zoomToggled = false;

        isZooming = want;
        zoomBlend = Mathf.Lerp(zoomBlend, isZooming ? 1f : 0f, ExpLerp(zoomSpeed, dt));
        if (Mathf.Abs(zoomBlend - (isZooming ? 1f : 0f)) < 0.001f) zoomBlend = isZooming ? 1f : 0f;
    }

    // =============================================================== STANCE ===

    float StanceHeight(Stance s)
    {
        float min = cc.radius * 2f + 0.01f;
        switch (s)
        {
            case Stance.Crouching: return Mathf.Clamp(crouchHeight, min, defaultHeight);
            case Stance.Prone: return Mathf.Clamp(proneHeight, min, defaultHeight);
            default: return defaultHeight;
        }
    }

    void HandleStanceInput()
    {
        bool crouchDown = Input.GetKeyDown(crouchKey);
        bool proneDown = Input.GetKeyDown(proneKey);

        // crouch key + sprinting = slide
        if (crouchDown && enableSlide && TryStartSlide()) return;

        if (canCrouch)
        {
            if (crouchMode == InputMode.Toggle)
            {
                if (crouchDown)
                    desiredStance = desiredStance == Stance.Crouching ? Stance.Standing : Stance.Crouching;
            }
            else
            {
                if (crouchDown && desiredStance != Stance.Prone) desiredStance = Stance.Crouching;
                if (Input.GetKeyUp(crouchKey))
                {
                    if (desiredStance == Stance.Crouching) desiredStance = Stance.Standing;
                    if (isSliding) EndSlide();
                }
            }
        }

        if (canProne)
        {
            if (proneMode == InputMode.Toggle)
            {
                if (proneDown)
                    desiredStance = desiredStance == Stance.Prone ? Stance.Standing : Stance.Prone;
            }
            else
            {
                if (proneDown) desiredStance = Stance.Prone;
                if (Input.GetKeyUp(proneKey) && desiredStance == Stance.Prone) desiredStance = Stance.Standing;
            }
        }

        if (!canCrouch && desiredStance == Stance.Crouching) desiredStance = Stance.Standing;
        if (!canProne && desiredStance == Stance.Prone) desiredStance = Stance.Standing;
    }

    void UpdateStance(float dt)
    {
        if (desiredStance != stance)
        {
            bool goingTaller = (int)desiredStance < (int)stance;

            if (!goingTaller)
            {
                ChangeStance(desiredStance);
            }
            else if (!isSliding)
            {
                if (HasHeadroom(StanceHeight(desiredStance))) ChangeStance(desiredStance);
                else if (stance == Stance.Prone && desiredStance == Stance.Standing &&
                         HasHeadroom(StanceHeight(Stance.Crouching)))
                    ChangeStance(Stance.Crouching);
            }
        }

        // smooth collider resize
        float target = isSliding ? Mathf.Min(slideColliderHeight, StanceHeight(stance)) : StanceHeight(stance);
        if (target > currentHeight + 0.001f && !HasHeadroom(target)) target = currentHeight;

        currentHeight = Mathf.MoveTowards(currentHeight, target, stanceTransitionSpeed * dt);
        currentHeight = Mathf.Max(currentHeight, cc.radius * 2f + 0.01f);

        cc.height = currentHeight;
        cc.center = new Vector3(defaultCenter.x, bottomLocalY + currentHeight * 0.5f, defaultCenter.z);
    }

    void ChangeStance(Stance s)
    {
        if (stance == s) return;
        stance = s;
        onStanceChanged.Invoke(s);
    }







    /// <summary>Is there room above the current head to grow the collider to localHeight?</summary>
    bool HasHeadroom(float localHeight)
    {
        if (motionLocked && lockedIgnoreAllHeadroom) return true;   // NEW
        // ... rest unchanged

        float scale = WorldScaleY;
        float r = WorldRadius;
        float curH = cc.height * scale;
        float newH = localHeight * scale;
        if (newH <= curH + 0.001f) return true;

        Vector3 feet = FeetPosition;
        Vector3 p0 = feet + Vector3.up * (curH - r);
        Vector3 p1 = feet + Vector3.up * (newH - r);
        int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r * 0.9f, overlapBuffer, obstructionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == cc || c.transform.IsChildOf(transform) || IsIgnoredWhileLocked(c)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Is a full capsule of worldHeight free when standing on feetPos?</summary>
    bool CapsuleFree(Vector3 feetPos, float worldHeight)
    {
        float r = WorldRadius;
        Vector3 p0 = feetPos + Vector3.up * (r + 0.1f);
        Vector3 p1 = feetPos + Vector3.up * Mathf.Max(worldHeight - r, r + 0.1f);
        int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r * 0.9f, overlapBuffer, obstructionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == cc || c.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    // ================================================================ SPRINT ===

    void UpdateSprint()
    {
        if (!canSprint || !movementEnabled)
        {
            sprintToggled = false;
            SetSprinting(false);
            return;
        }

        if (sprintMode == InputMode.Toggle && Input.GetKeyDown(sprintKey))
            sprintToggled = !sprintToggled;

        bool wants = sprintMode == InputMode.Toggle ? sprintToggled : Input.GetKey(sprintKey);

        bool directionOk = sprintForwardOnly ? moveInput.y > 0.1f : hasMoveInput;
        bool allowed = stance == Stance.Standing && !isSliding && !isMantling && directionOk &&
                       (grounded || isSprinting) && (!isZooming || canSprintWhileZoomed);

        if (useStamina)
            allowed &= isSprinting ? stamina > 0f : (!exhausted && stamina >= minStaminaToSprint);

        if (!allowed && grounded && sprintMode == InputMode.Toggle) sprintToggled = false;

        SetSprinting(wants && allowed);
    }

    void SetSprinting(bool value)
    {
        if (isSprinting == value) return;
        isSprinting = value;
        if (value) onSprintStart.Invoke();
        else onSprintStop.Invoke();
    }

    void UpdateStamina(float dt)
    {
        if (!useStamina)
        {
            stamina = maxStamina;
            exhausted = false;
            return;
        }

        if (isSprinting && hasMoveInput)
        {
            stamina -= sprintStaminaDrain * dt;
            lastStaminaUseTime = Time.time;

            if (stamina <= 0f)
            {
                stamina = 0f;
                if (!exhausted)
                {
                    exhausted = true;
                    onStaminaDepleted.Invoke();
                }
                sprintToggled = false;
                SetSprinting(false);
            }
        }
        else if (Time.time - lastStaminaUseTime >= staminaRegenDelay)
        {
            float rate = staminaRegenRate * (hasMoveInput ? 1f : idleRegenMultiplier);
            stamina = Mathf.Min(maxStamina, stamina + rate * dt);
        }

        if (exhausted && stamina >= minStaminaToSprint) exhausted = false;
    }

    bool ConsumeStamina(float cost)
    {
        if (!useStamina || cost <= 0f) return true;
        if (stamina < cost) return false;
        stamina -= cost;
        lastStaminaUseTime = Time.time;
        return true;
    }

    // ================================================================== JUMP ===

    void HandleJump()
    {
        if (!movementEnabled) return;
        if (Time.time - lastJumpPressTime > jumpBufferTime) return;

        bool coyote = Time.time - lastGroundedTime <= coyoteTime;
        bool offCooldown = Time.time - lastJumpTime >= jumpCooldown;

        if ((grounded || coyote) && offCooldown)
        {
            if (isSliding && !slideJumpCancels) return;

            if (stance == Stance.Prone)
            {
                if (jumpFromProneStandsUp) desiredStance = Stance.Standing;
                lastJumpPressTime = -999f;
                return;
            }

            if (stance == Stance.Crouching && !canJumpWhileCrouched && !isSliding)
            {
                desiredStance = Stance.Standing;
                lastJumpPressTime = -999f;
                return;
            }

            if (!ConsumeStamina(jumpStaminaCost)) return;

            float mult = stance == Stance.Crouching ? crouchJumpHeightMultiplier : 1f;
            PerformJump(jumpHeight * mult * jumpHeightMultiplier, false);
        }
        else if (jumpPressed && airJumpsUsed < maxAirJumps)
        {
            if (!ConsumeStamina(jumpStaminaCost)) return;
            airJumpsUsed++;

            if (airJumpRedirectsMomentum && hasMoveInput)
            {
                Vector3 wish = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized;
                float speed = Mathf.Max(horizontalVelocity.magnitude, walkSpeed);
                horizontalVelocity = wish * speed;
            }

            PerformJump(jumpHeight * airJumpHeightMultiplier * jumpHeightMultiplier, true);
        }
    }

    void PerformJump(float height, bool air)
    {
        float g = Mathf.Max(0.01f, Mathf.Abs(gravity) * Mathf.Abs(gravityScale));
        float v = Mathf.Sqrt(2f * g * Mathf.Max(height, 0.01f));
        verticalVelocity = gravityScale >= 0f ? v : -v;

        grounded = false;
        lastJumpTime = Time.time;
        lastGroundedTime = -999f;
        lastJumpPressTime = -999f;

        if (isSliding)
        {
            EndSlide();
            horizontalVelocity *= slideJumpSpeedMultiplier;
        }

        if (!air && inheritPlatformMomentum && platform != null)
            externalVelocity += new Vector3(platformVelocity.x, 0f, platformVelocity.z);

        PlayRandom(jumpClips, jumpLandVolume * 0.7f);
        if (air) onAirJump.Invoke(); else onJump.Invoke();
    }

    // ============================================================ HORIZONTAL ===

    void UpdateHorizontal(float dt)
    {
        Vector3 wish = transform.right * moveInput.x + transform.forward * moveInput.y;
        wish = Vector3.ClampMagnitude(wish, 1f);

        if (isSliding)
        {
            UpdateSlide(dt, wish);
        }
        else
        {
            float speed = GetTargetSpeed(wish);
            Vector3 targetVel = wish * speed;

            float accel;
            if (grounded)
            {
                bool overspeed = horizontalVelocity.magnitude > targetVel.magnitude + 0.05f;
                accel = (hasMoveInput && !overspeed) ? groundAcceleration : groundDeceleration;
            }
            else
            {
                accel = hasMoveInput ? airAcceleration : airDeceleration;
            }

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVel, accel * dt);
        }

        // fade external velocity
        float drag = grounded ? externalGroundDrag : externalAirDrag;
        externalVelocity *= Mathf.Exp(-drag * dt);
        if (externalVelocity.sqrMagnitude < 0.0004f) externalVelocity = Vector3.zero;
    }

    float GetTargetSpeed(Vector3 wish)
    {
        float speed;
        if (stance == Stance.Prone) speed = proneSpeed;
        else if (stance == Stance.Crouching) speed = crouchSpeed;
        else if (isSprinting) speed = sprintSpeed;
        else if (isSlowWalking) speed = slowWalkSpeed;
        else speed = walkSpeed;

        // direction multipliers
        if (hasMoveInput)
        {
            Vector2 n = moveInput.normalized;
            float m = Mathf.Lerp(1f, strafeSpeedMultiplier, Mathf.Abs(n.x));
            if (n.y < 0f) m = Mathf.Lerp(m, backwardSpeedMultiplier, -n.y);
            speed *= m;
        }

        // slopes
        if (grounded && slopeAngle > 1f && wish.sqrMagnitude > 0.0001f)
        {
            Vector3 downhill = new Vector3(groundNormal.x, 0f, groundNormal.z);
            if (downhill.sqrMagnitude > 0.0001f)
            {
                downhill.Normalize();
                float along = Vector3.Dot(wish.normalized, downhill); // +1 downhill, -1 uphill
                float t = Mathf.Clamp01(slopeAngle / Mathf.Max(cc.slopeLimit, 1f));
                if (along < 0f) speed *= 1f - uphillSpeedPenalty * t * -along;
                else speed *= 1f + downhillSpeedBonus * t * along;
            }
        }

        if (isZooming) speed *= zoomMoveSpeedMultiplier;
        if (exhausted && useStamina) speed *= exhaustedSpeedMultiplier;
        if (hardLandTimer > 0f && hardLandSlowTime > 0f)
            speed *= Mathf.Lerp(1f, hardLandSpeedMultiplier, hardLandTimer / hardLandSlowTime);

        return speed * globalSpeedMultiplier * speedModifierProduct;
    }

    // ================================================================ SLIDE ===

    bool TryStartSlide()
    {
        if (!enableSlide || isSliding || isMantling || !movementEnabled) return false;
        if (Time.time < slideCooldownEnd) return false;
        if (!grounded || stance != Stance.Standing) return false;
        if (slideRequiresSprint && !isSprinting) return false;

        float speed = horizontalVelocity.magnitude;
        if (speed < slideMinStartSpeed) return false;

        slideDir = horizontalVelocity / speed;
        slideDir.y = 0f;
        slideDir.Normalize();
        slideSpeed = Mathf.Min(Mathf.Max(speed, sprintSpeed) * slideStartBoost, slideMaxSpeed);
        slideTimer = 0f;

        sprintToggled = false;
        SetSprinting(false);

        isSliding = true;
        desiredStance = Stance.Crouching;
        ChangeStance(Stance.Crouching);
        onSlideStart.Invoke();
        return true;
    }

    void UpdateSlide(float dt, Vector3 wish)
    {
        slideTimer += dt;

        // steering
        if (wish.sqrMagnitude > 0.01f && slideSteerSpeed > 0f)
        {
            slideDir = Vector3.RotateTowards(slideDir, wish.normalized, slideSteerSpeed * Mathf.Deg2Rad * dt, 0f);
            slideDir.y = 0f;
            slideDir.Normalize();
        }

        if (grounded)
        {
            // slope acceleration (downhill speeds you up, uphill slows you)
            if (slopeAngle > 1f)
            {
                Vector3 downhill = new Vector3(groundNormal.x, 0f, groundNormal.z);
                float steepness = Mathf.Clamp01(downhill.magnitude);
                if (downhill.sqrMagnitude > 0.0001f)
                {
                    downhill.Normalize();
                    slideSpeed += Vector3.Dot(slideDir, downhill) * steepness * slideSlopeAcceleration * dt;
                }
            }
            slideSpeed -= slideFriction * dt;
        }

        slideSpeed = Mathf.Min(slideSpeed, slideMaxSpeed);
        horizontalVelocity = slideDir * Mathf.Max(slideSpeed, 0f);

        bool tooSlow = slideSpeed <= slideEndSpeed;
        bool timeUp = slideMaxDuration > 0f && slideTimer >= slideMaxDuration;
        bool airborneTooLong = !grounded && airTime > 0.25f;

        if (tooSlow || timeUp || airborneTooLong) EndSlide();
    }

    void EndSlide()
    {
        if (!isSliding) return;
        isSliding = false;
        slideCooldownEnd = Time.time + slideCooldown;

        bool holdingCrouch = crouchMode == InputMode.Hold && Input.GetKey(crouchKey);
        if (!crouchAfterSlide && !holdingCrouch) desiredStance = Stance.Standing;

        onSlideEnd.Invoke();
    }

    // =============================================================== MANTLE ===

    void HandleMantleDetection()
    {
        if (!enableMantle || isMantling || grounded || !movementEnabled) return;
        if (Time.time < nextMantleTime) return;
        if (mantleRequiresJumpInput && !jumpHeld) return;
        if (mantleRequiresForwardInput && moveInput.y <= 0.1f) return;
        if (verticalVelocity < -mantleMaxFallSpeed) return;

        TryMantle();
    }

    bool TryMantle()
    {
        Vector3 feet = FeetPosition;
        Vector3 fwd = transform.forward;
        float r = WorldRadius;
        float scale = WorldScaleY;
        float minH = mantleMinHeight * scale;
        float maxH = mantleMaxHeight * scale;
        float dist = r + mantleReach;

        // 1. there must be a wall in front at the minimum ledge height
        RaycastHit wall;
        if (!Physics.Raycast(feet + Vector3.up * minH, fwd, out wall, dist, obstructionMask, QueryTriggerInteraction.Ignore))
            return false;
        if (wall.normal.y > 0.5f) return false; // slope, not a wall

        // 2. it must NOT be blocked above the maximum reach
        Vector3 topOrigin = feet + Vector3.up * (maxH + 0.1f);
        if (Physics.Raycast(topOrigin, fwd, dist + 0.2f, obstructionMask, QueryTriggerInteraction.Ignore))
            return false;

        // 3. find the ledge surface behind the wall edge
        Vector3 into = -new Vector3(wall.normal.x, 0f, wall.normal.z);
        if (into.sqrMagnitude < 0.0001f) into = fwd;
        into.Normalize();

        Vector3 downOrigin = new Vector3(wall.point.x, topOrigin.y, wall.point.z) + into * (r + 0.05f);
        RaycastHit ledge;
        if (!Physics.Raycast(downOrigin, Vector3.down, out ledge, maxH + 0.1f - minH + 0.05f,
                obstructionMask, QueryTriggerInteraction.Ignore))
            return false;

        if (ledge.normal.y < 0.7f) return false;

        float ledgeHeight = ledge.point.y - feet.y;
        if (ledgeHeight < minH - 0.05f || ledgeHeight > maxH + 0.05f) return false;

        // 4. make sure we actually fit up there
        Vector3 targetFeet = ledge.point + Vector3.up * 0.02f;
        float mantleFitHeight = currentHeight;
        if (mantleAutoCrouch)
            mantleFitHeight = Mathf.Min(mantleFitHeight, StanceHeight(Stance.Crouching));
        if (!CapsuleFree(targetFeet, mantleFitHeight * scale)) return false;

        // go!
        Vector3 rootOffset = transform.position - feet;
        mantleStart = transform.position;
        mantleEnd = targetFeet + rootOffset;
        mantleMid = new Vector3(mantleStart.x, mantleEnd.y, mantleStart.z);
        mantleProgress = 0f;
        mantleDurationCurrent = Mathf.Max(0.1f, mantleBaseDuration + ledgeHeight / Mathf.Max(scale, 0.01f) * mantleTimePerMeter);

        isMantling = true;
        mantleRequestedCrouch = mantleAutoCrouch;
        if (mantleAutoCrouch && canCrouch)
        {
            desiredStance = Stance.Crouching;
            ChangeStance(Stance.Crouching);
        }
        mantleNoiseTimer = mantleNoiseDuration;
        mantleNoiseSeed = UnityEngine.Random.Range(0f, 1000f);
        AddTrauma(mantleStartShake);
        sprintToggled = false;
        SetSprinting(false);
        if (isSliding) EndSlide();
        onMantleStart.Invoke();
        return true;
    }


    void UpdateMantle(float dt)
    {
        // Progress through the mantle animation.
        mantleProgress += dt / Mathf.Max(0.05f, mantleDurationCurrent);
        float t = Mathf.Clamp01(mantleProgress);

        float split = Mathf.Clamp(mantleRiseFraction, 0.1f, 0.9f);

        // Horizontal direction used for forward/backward body movement.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(mantleEnd - mantleStart, Vector3.up);

        forward = forward.sqrMagnitude > 0.001f
            ? forward.normalized
            : Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // ---------------------------------------------------------
        // PHASE 1: Pull upward, then move across the ledge.
        // Smooth acceleration/deceleration for the main path.
        // ---------------------------------------------------------

        float riseT = Mathf.Clamp01(t / split);
        float crossT = Mathf.Clamp01((t - split) / (1f - split));

        riseT = Mathf.SmoothStep(0f, 1f, riseT);
        crossT = Mathf.SmoothStep(0f, 1f, crossT);

        Vector3 p = t < split
            ? Vector3.Lerp(mantleStart, mantleMid, riseT)
            : Vector3.Lerp(mantleMid, mantleEnd, crossT);

        // ---------------------------------------------------------
        // PHASE 2: Anticipation.
        // A tiny backward weight shift before pulling up.
        // Starts and finishes at zero to avoid position snapping.
        // ---------------------------------------------------------

        float anticipationT = Mathf.Clamp01(t / 0.16f);
        float anticipation = Mathf.Sin(Mathf.PI * anticipationT);

        Vector3 anticipationOffset = -forward * (0.045f * anticipation);

        // ---------------------------------------------------------
        // PHASE 3: Forward reach and torso/hip movement.
        // The root leans toward the ledge while climbing.
        // ---------------------------------------------------------

        float reachCurve = Mathf.Sin(Mathf.PI * t);

        Vector3 forwardReach = forward * (0.10f * reachCurve);

        // Small lateral weight shift, like moving one side over first.
        float lateralShift = Mathf.Sin(2f * Mathf.PI * t) * 0.025f;
        Vector3 lateralOffset = right * lateralShift;

        // ---------------------------------------------------------
        // PHASE 4: Body compression and leg tuck.
        // A subtle upward pulse followed by a small compression
        // as the player gets their weight over the ledge.
        // ---------------------------------------------------------

        float tuckIn = Mathf.SmoothStep(0.40f, 0.67f, t);
        float tuckOut = 1f - Mathf.SmoothStep(0.72f, 0.96f, t);
        float tuck = tuckIn * tuckOut;

        float verticalMotion =
            Mathf.Sin(Mathf.PI * t) * 0.035f
            - tuck * 0.025f;

        Vector3 verticalOffset = Vector3.up * verticalMotion;

        // ---------------------------------------------------------
        // PHASE 5: Apply the procedural path.
        // Every additional offset returns to zero at the end.
        // ---------------------------------------------------------

        Vector3 targetPosition =
            p
            + anticipationOffset
            + forwardReach
            + lateralOffset
            + verticalOffset;

        Vector3 before = transform.position;

        cc.Move(targetPosition - transform.position);

        actualHorizontalSpeed = 0f;
        horizontalVelocity = Vector3.zero;
        externalVelocity = Vector3.zero;
        verticalVelocity = 0f;

        // Finish as soon as the intended animation reaches its end.
        if (t >= 1f ||
            (transform.position - before).sqrMagnitude < 1e-10f
            && mantleProgress > 1.5f)
        {
            EndMantle();
        }
    }

    void EndMantle()
    {
        isMantling = false;
        nextMantleTime = Time.time + mantleCooldown;

        horizontalVelocity = transform.forward * mantleExitSpeed;
        verticalVelocity = 0f;

        // The crouch is intentional during the pull-up. Once on top, request
        // standing; UpdateStance still checks headroom and will keep us crouched
        // if the ceiling is too low.
        if (mantleRequestedCrouch)
        {
            // Only auto-stand if you've turned "Mantle Stay Crouched After" off.
            if (!mantleStayCrouchedAfter) desiredStance = Stance.Standing;
            mantleRequestedCrouch = false;
        }

        mantleNoiseTimer = Mathf.Max(mantleNoiseTimer, mantleNoiseDuration);
        AddTrauma(mantleFinishShake);
        onMantleEnd.Invoke();
    }


    // ============================================================== VERTICAL ===

    void UpdateVertical(float dt)
    {
        if (grounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -groundStickForce;
            return;
        }

        float g = Mathf.Abs(gravity) * gravityScale;
        bool falling = gravityScale >= 0f ? verticalVelocity < 0f : verticalVelocity > 0f;

        if (falling) g *= fallGravityMultiplier;
        else if (variableJumpHeight && !jumpHeld) g *= lowJumpGravityMultiplier;

        verticalVelocity -= g * dt;
        verticalVelocity = Mathf.Clamp(verticalVelocity, -terminalVelocity, terminalVelocity);
    }

    // ================================================================= MOVE ===

    void MoveCharacter(float dt)
    {
        Vector3 moveVel = horizontalVelocity;

        if (grounded && projectMovementOnSlope && groundNormal.y > 0.0001f)
        {
            Vector3 projected = Vector3.ProjectOnPlane(horizontalVelocity, groundNormal);
            if (projected.sqrMagnitude > 1e-6f)
                moveVel = projected.normalized * horizontalVelocity.magnitude;
        }

        Vector3 slopeSlide = Vector3.zero;
        if (isOnSteepSlope && slideDownSteepSlopes)
        {
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            if (downhill.sqrMagnitude > 1e-6f) slopeSlide = downhill.normalized * steepSlopeSlideSpeed;
        }

        Vector3 total = moveVel
                        + new Vector3(externalVelocity.x, 0f, externalVelocity.z)
                        + slopeSlide
                        + Vector3.up * verticalVelocity;

        Vector3 before = transform.position;
        lastFlags = cc.Move(total * dt);

        Vector3 moved = (transform.position - before) / dt;
        moved.y = 0f;
        actualHorizontalSpeed = moved.magnitude;

        // ceiling bump
        if ((lastFlags & CollisionFlags.Above) != 0)
        {
            if (gravityScale >= 0f && verticalVelocity > 0f) verticalVelocity = 0f;
        }

        // wall hit: kill momentum that is pushing into the wall
        if ((lastFlags & CollisionFlags.Sides) != 0 && horizontalVelocity.magnitude > 0.5f)
        {
            if (actualHorizontalSpeed < horizontalVelocity.magnitude * 0.5f)
            {
                horizontalVelocity = moved;
                if (isSliding && moved.magnitude < slideEndSpeed) EndSlide();
            }
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!pushRigidbodies) return;

        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic) return;
        if (hit.moveDirection.y < -0.3f) return; // standing on it

        Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        float targetSpeed = actualHorizontalSpeed * pushSpeedFactor / (1f + body.mass * pushMassResistance);
        Vector3 dv = dir * targetSpeed - body.GetPointVelocity(hit.point);
        dv.y = 0f;
        body.AddForce(dv * Mathf.Min(1f, pushStrength * Time.deltaTime), ForceMode.VelocityChange);
    }

    // =========================================================== FOOTSTEPS ===

    void UpdateFootsteps(float dt)
    {
        if (!enableFootsteps) return;
        if (!grounded || isSliding || isMantling || actualHorizontalSpeed < 0.5f) return;

        float stepDist;
        float volume;
        if (stance == Stance.Prone) { stepDist = proneStepDistance; volume = proneFootstepVolume; }
        else if (stance == Stance.Crouching) { stepDist = crouchStepDistance; volume = crouchFootstepVolume; }
        else if (isSprinting) { stepDist = sprintStepDistance; volume = sprintFootstepVolume; }
        else { stepDist = walkStepDistance; volume = footstepVolume; }

        stepAccumulator += actualHorizontalSpeed * dt;
        if (stepAccumulator < stepDist) return;
        stepAccumulator = 0f;

        string tag = GroundTag;
        AudioClip[] clips = defaultFootsteps;
        if (surfaceSounds != null)
        {
            for (int i = 0; i < surfaceSounds.Length; i++)
            {
                SurfaceSound s = surfaceSounds[i];
                if (s != null && s.surfaceTag == tag && s.footsteps != null && s.footsteps.Length > 0)
                {
                    clips = s.footsteps;
                    break;
                }
            }
        }

        PlayRandom(clips, volume);
        onFootstep.Invoke(tag);
    }

    void PlayRandom(AudioClip[] clips, float volume)
    {
        if (!enableAudio || audioSource == null || clips == null || clips.Length == 0) return;

        int i = UnityEngine.Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == lastClipIndex) i = (i + 1) % clips.Length;
        lastClipIndex = i;
        if (clips[i] == null) return;

        audioSource.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        audioSource.PlayOneShot(clips[i], volume);
    }

    // ================================================================== FOV ===

    void UpdateFov(float dt)
    {
        if (cam == null || !controlFov) return;

        sprintFovBlend = Mathf.Lerp(sprintFovBlend, isSprinting ? 1f : 0f, ExpLerp(fovChangeSpeed, dt));
        slideFovBlend = Mathf.Lerp(slideFovBlend, isSliding ? 1f : 0f, ExpLerp(fovChangeSpeed, dt));

        float fov = baseFov
                    + sprintFovBoost * sprintFovBlend
                    + slideFovBoost * slideFovBlend
                    + extraFov;

        cam.fieldOfView = Mathf.Lerp(fov, zoomFov, zoomBlend);
    }

    // ============================================================== CAMERA ===

    static float Noise(float t, float seed)
    {
        return Mathf.PerlinNoise(t, seed * 17.31f + 0.5f) * 2f - 1f;
    }

    void UpdateCamera(float dt)
    {
        float sdt = Mathf.Min(dt, 0.05f);

        // ---------- eye height follows the collider
        float eyeTarget = standingEyeY
                          - (defaultHeight - currentHeight) * eyeHeightFollow
                          - slideEyeDrop * slideBlend;
        slideBlend = Mathf.MoveTowards(slideBlend, isSliding ? 1f : 0f, dt * 8f);
        eyeY = eyeSmoothTime > 0f
            ? Mathf.SmoothDamp(eyeY, eyeTarget, ref eyeVelocity, eyeSmoothTime)
            : eyeTarget;

        // ---------- lean
        float leanInput = 0f;
        bool leanAllowed = canLean && movementEnabled && !isSliding && !isMantling &&
                           (!isSprinting || leanWhileSprinting) && (grounded || leanWhileAirborne);
        if (leanAllowed)
        {
            if (Input.GetKey(leanLeftKey)) leanInput -= 1f;
            if (Input.GetKey(leanRightKey)) leanInput += 1f;
        }

        float targetScale = 1f;
        if (leanInput != 0f && leanWallCheck && leanDistance > 0.001f)
        {
            Vector3 origin = transform.TransformPoint(new Vector3(baseCamX, eyeY, baseCamZ));
            RaycastHit hit;
            float check = leanDistance + leanWallPadding;
            if (Physics.Raycast(origin, transform.right * Mathf.Sign(leanInput), out hit, check,
                    obstructionMask, QueryTriggerInteraction.Ignore))
            {
                targetScale = Mathf.Clamp01((hit.distance - leanWallPadding) / leanDistance);
            }
        }

        float k = ExpLerp(leanSmoothSpeed, dt);
        leanScale = Mathf.Lerp(leanScale, targetScale, k);
        leanAmount = Mathf.Lerp(leanAmount, leanInput * leanAngle * leanScale, k);
        leanOffset = Mathf.Lerp(leanOffset, leanInput * leanDistance * leanScale, k);

        // ---------- head bob
        float speed = actualHorizontalSpeed;
        bool bobbing = enableHeadBob && grounded && !isSliding && !isMantling && speed > 0.3f;
        bobBlend = Mathf.MoveTowards(bobBlend, bobbing ? 1f : 0f, dt * bobFadeSpeed);

        if (bobbing)
        {
            float speedRatio = Mathf.Clamp(speed / Mathf.Max(walkSpeed, 0.01f), 0.3f, 2.5f);
            bobPhase += dt * bobFrequency * speedRatio * Mathf.PI * 2f;
        }

        float bobMult = 1f;
        if (stance == Stance.Prone) bobMult = bobProneMultiplier;
        else if (stance == Stance.Crouching) bobMult = bobCrouchMultiplier;
        else if (isSprinting) bobMult = bobSprintMultiplier;
        if (isZooming) bobMult *= bobZoomMultiplier;
        float bobAmp = bobBlend * bobMult;

        float bobY = Mathf.Sin(bobPhase) * bobVerticalAmount * bobAmp;
        float bobX = Mathf.Cos(bobPhase * 0.5f) * bobHorizontalAmount * bobAmp;
        float bobRoll = Mathf.Cos(bobPhase * 0.5f) * bobRollAngle * bobAmp;

        // ---------- breathing
        float breathY = 0f, breathPitch = 0f;
        if (enableBreathing)
        {
            float idle = 1f - bobBlend;
            float tt = Time.time * breathSpeed * Mathf.PI * 2f;
            breathY = Mathf.Sin(tt) * breathAmount * idle;
            breathPitch = Mathf.Sin(tt * 0.5f + 1.3f) * breathSwayAngle * idle;
        }

        // ---------- strafe tilt
        float localX = Vector3.Dot(horizontalVelocity, transform.right);
        float tiltTarget = -(localX / Mathf.Max(sprintSpeed, 0.01f)) * strafeTilt;
        strafeTiltCurrent = Mathf.Lerp(strafeTiltCurrent, tiltTarget, ExpLerp(8f, dt));

        // ---------- movement lean (forward tilt)
        float tiltPitchTarget = 0f, tiltOffsetTarget = 0f;
        bool tiltAllowed = enableMoveTilt && !isMantling
                           && (grounded || moveTiltWhileAirborne)
                           && (!isSliding || moveTiltWhileSliding)
                           && actualHorizontalSpeed > 0.1f;
        if (tiltAllowed)
        {
            float refSpeed = moveTiltReferenceSpeed > 0f ? moveTiltReferenceSpeed : Mathf.Max(sprintSpeed, 0.01f);
            float localZ = Mathf.Clamp(Vector3.Dot(horizontalVelocity, transform.forward) / refSpeed, -1f, 1f);
            if (localZ >= 0f)
            {
                tiltPitchTarget = localZ * moveTiltForwardAngle;
                tiltOffsetTarget = localZ * moveTiltForwardOffset;
            }
            else
            {
                tiltPitchTarget = localZ * moveTiltBackwardAngle;   // negative = look up
                tiltOffsetTarget = localZ * moveTiltBackwardOffset;  // negative = camera back
            }
        }
        float tiltK = ExpLerp(moveTiltSmoothSpeed, dt);
        moveTiltPitch = Mathf.Lerp(moveTiltPitch, tiltPitchTarget, tiltK);
        moveTiltOffsetZ = Mathf.Lerp(moveTiltOffsetZ, tiltOffsetTarget, tiltK);


        // ---------- landing spring
        landingVelocity += (-landingSpringStiffness * landingOffset - landingSpringDamping * landingVelocity) * sdt;
        landingOffset += landingVelocity * sdt;
        landingOffset = Mathf.Clamp(landingOffset, -landingDipMax, landingDipMax * 0.5f);

        // ---------- recoil
        recoilTarget = Vector2.Lerp(recoilTarget, Vector2.zero, ExpLerp(recoilRecoverySpeed, dt));
        recoilCurrent = Vector2.Lerp(recoilCurrent, recoilTarget, ExpLerp(recoilSnappiness, dt));

        // ---------- trauma shake
        trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
        float shake = trauma * trauma;
        float st = Time.time * shakeFrequency;
        Vector3 shakeRot = new Vector3(Noise(st, 0f), Noise(st, 1f), Noise(st, 2f)) * (maxShakeAngle * shake);
        Vector3 shakePos = new Vector3(Noise(st, 3f), Noise(st, 4f), Noise(st, 5f)) * (maxShakeOffset * shake);

        // Mantle-only procedural noise, independently tweakable in the Inspector.
        mantleNoiseTimer = Mathf.Max(0f, mantleNoiseTimer - dt);
        float mantleNoiseBlend = mantleNoiseDuration > 0.001f
            ? Mathf.Clamp01(mantleNoiseTimer / mantleNoiseDuration)
            : 0f;
        float mantleNoiseTime = Time.time * mantleNoiseFrequency;
        Vector3 mantleNoiseRot = new Vector3(
            Noise(mantleNoiseTime + mantleNoiseSeed, 10f),
            Noise(mantleNoiseTime + mantleNoiseSeed, 11f),
            Noise(mantleNoiseTime + mantleNoiseSeed, 12f))
            * (mantleNoiseRotation * mantleNoiseBlend);
        Vector3 mantleNoisePos = new Vector3(
            Noise(mantleNoiseTime + mantleNoiseSeed, 13f),
            Noise(mantleNoiseTime + mantleNoiseSeed, 14f),
            Noise(mantleNoiseTime + mantleNoiseSeed, 15f))
            * (mantleNoisePosition * mantleNoiseBlend);

        // ---------- apply
        playerCamera.localPosition = new Vector3(
            baseCamX + leanOffset + bobX + shakePos.x + mantleNoisePos.x,
            eyeY + bobY + breathY + landingOffset + shakePos.y + mantleNoisePos.y,
            baseCamZ + moveTiltOffsetZ + shakePos.z + mantleNoisePos.z);        // <--

        playerCamera.localRotation = Quaternion.Euler(
            pitch + moveTiltPitch + recoilCurrent.x + breathPitch + shakeRot.x + mantleNoiseRot.x,  // <--
            recoilCurrent.y + shakeRot.y + mantleNoiseRot.y,
            -leanAmount + bobRoll + strafeTiltCurrent + shakeRot.z + mantleNoiseRot.z);
    }

    // ================================================================ DEBUG ===

    void OnGUI()
    {
        if (!showDebugOverlay || cc == null) return;

        string text =
            "State:    " + State + "\n" +
            "Stance:   " + stance + "\n" +
            "Speed:    " + actualHorizontalSpeed.ToString("F2") + " m/s\n" +
            "Vertical: " + verticalVelocity.ToString("F2") + " m/s\n" +
            "Grounded: " + grounded + "  (slope " + slopeAngle.ToString("F1") + " deg)\n" +
            "Ground:   " + GroundTag + "\n" +
            "Stamina:  " + stamina.ToString("F0") + " / " + maxStamina.ToString("F0") + (exhausted ? "  EXHAUSTED" : "") + "\n" +
            "Height:   " + currentHeight.ToString("F2");

        GUI.Box(new Rect(8, 8, 280, 130), GUIContent.none);
        GUI.Label(new Rect(16, 12, 270, 125), text);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        var c = cc != null ? cc : GetComponent<CharacterController>();
        if (c == null) return;

        float r = c.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * 0.95f;
        Vector3 feet = transform.TransformPoint(new Vector3(c.center.x, c.center.y - c.height * 0.5f, c.center.z));

        // ground check
        Gizmos.color = grounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(feet + Vector3.up * (r + 0.05f) + Vector3.down * (0.05f + groundCheckDistance), r);

        // mantle volume
        if (enableMantle)
        {
            Gizmos.color = Color.cyan;
            Vector3 a = feet + Vector3.up * mantleMinHeight;
            Vector3 b = feet + Vector3.up * mantleMaxHeight;
            float reach = r + mantleReach;
            Gizmos.DrawLine(a, a + transform.forward * reach);
            Gizmos.DrawLine(b, b + transform.forward * reach);
            Gizmos.DrawLine(a + transform.forward * reach, b + transform.forward * reach);
        }

        // lean
        if (canLean && playerCamera != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(playerCamera.position - transform.right * leanDistance,
                            playerCamera.position + transform.right * leanDistance);
        }
    }
}
