// ================================================================
//  SurgicalToolPhysicsSetup.cs
//
//  Drop this ONE component on any grabbable tool (drill tool,
//  screwdriver, or screw prefab) to apply tuned Rigidbody
//  settings that work smoothly with Leap Physical Hands.
//
//  Presets:
//    - Drill / Screwdriver     → medium mass, moderate damping
//    - Screw (loose, pre-place)→ light mass, heavier damping
//    - Static / Locked         → kinematic (for snapped screws)
//
//  The values below are tuned specifically for Leap XR
//  Physical Hands interaction. They were determined empirically:
//     * too high drag  → hand feels laggy, tool floats behind
//     * too low drag   → tool flies off on release, jitters
//     * mass > 1.0     → Leap hand can't stabilize grip
//     * gravity on     → tool drops when not grabbed
//
//  Usage:
//     var setup = drillTool.AddComponent<SurgicalToolPhysicsSetup>();
//     setup.preset = ToolPreset.Drill;
//     setup.Apply();       // runs automatically in Awake too
// ================================================================

using UnityEngine;

public enum ToolPreset
{
    Drill,         // handheld drill
    Screwdriver,   // handheld screwdriver
    LooseScrew,    // screw that can be picked up before placement
    SnappedScrew   // screw after placement - kinematic, no physics
}

[DisallowMultipleComponent]
public class SurgicalToolPhysicsSetup : MonoBehaviour
{
    [Header("── Preset ──")]
    public ToolPreset preset = ToolPreset.Drill;

    [Header("── Apply Behavior ──")]
    [Tooltip("Run Apply() automatically on Awake.")]
    public bool applyOnAwake = true;
    [Tooltip("Don't overwrite fields already set by user.")]
    public bool respectExisting = false;

    [Header("── Overrides (optional) ──")]
    [Tooltip("If > 0, overrides the preset mass.")]
    public float massOverride = 0f;
    [Tooltip("If > 0, overrides the preset drag.")]
    public float dragOverride = 0f;
    [Tooltip("If > 0, overrides the preset angular drag.")]
    public float angularDragOverride = 0f;

    void Awake()
    {
        if (applyOnAwake) Apply();
    }

    public void Apply()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        // Shared settings for all tool presets (safe defaults)
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxDepenetrationVelocity = 0.3f;
        rb.maxAngularVelocity = 25f;            // clamps runaway spin
        rb.constraints = RigidbodyConstraints.None;

        switch (preset)
        {
            case ToolPreset.Drill:
                if (!respectExisting || rb.mass == 1f) rb.mass = 0.45f;
                if (!respectExisting) rb.drag = 4f;
                if (!respectExisting) rb.angularDrag = 5f;
                rb.useGravity = false;
                rb.isKinematic = false;
                break;

            case ToolPreset.Screwdriver:
                if (!respectExisting || rb.mass == 1f) rb.mass = 0.30f;
                if (!respectExisting) rb.drag = 4f;
                if (!respectExisting) rb.angularDrag = 5f;
                rb.useGravity = false;
                rb.isKinematic = false;
                break;

            case ToolPreset.LooseScrew:
                if (!respectExisting || rb.mass == 1f) rb.mass = 0.15f;
                if (!respectExisting) rb.drag = 6f;       // more damping — small objects jitter more
                if (!respectExisting) rb.angularDrag = 8f;
                rb.useGravity = false;
                rb.isKinematic = false;
                break;

            case ToolPreset.SnappedScrew:
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                break;
        }

        // Apply overrides if user set them
        if (massOverride > 0f) rb.mass = massOverride;
        if (dragOverride > 0f) rb.drag = dragOverride;
        if (angularDragOverride > 0f) rb.angularDrag = angularDragOverride;

        // Zero out residual velocity so the object starts calm
        if (!rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // Convenient static method you can call from other scripts
    // without attaching this component permanently.
    public static void ApplyPreset(GameObject go, ToolPreset preset)
    {
        if (go == null) return;
        var setup = go.GetComponent<SurgicalToolPhysicsSetup>();
        if (setup == null) setup = go.AddComponent<SurgicalToolPhysicsSetup>();
        setup.preset = preset;
        setup.applyOnAwake = false;
        setup.Apply();
    }
}

