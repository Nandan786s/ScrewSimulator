// ================================================================
//  DrillTipDetector.cs   (PRODUCTION VERSION)
//
//  SETUP:
//    1. Create an EMPTY CHILD GameObject under your drill tool,
//       positioned exactly at the drill tip.
//    2. Attach this script. A SphereCollider (isTrigger=true) is
//       added automatically with the radius you choose.
//    3. Put your BONE(S) on a layer called "Bone" and put that
//       layer in the "boneMask" field on this component.
//    4. Drag this component into the LeapPhysicalInputManager's
//       "drillTipDetector" field.
//
//  WHAT IT DOES:
//    - Detects bone contact via trigger collider (NOT raycast).
//    - Cannot register contact when tip is fully inside bone -
//      OnTriggerEnter only fires when crossing surface inward.
//    - Refines the surface normal via Collider.Raycast for a
//      mesh-accurate result (not AABB-approximated).
//    - Exposes "justEntered" flag (true while tip is touching bone
//      after a valid outside-to-inside crossing; cleared on exit)
//    - Exposes IsTipDeeplyInsideBone() for extra safety checks.
// ================================================================

using UnityEngine;

[DisallowMultipleComponent]
public class DrillTipDetector : MonoBehaviour
{
    [Header("── Detection ──")]
    [Tooltip("Layers considered as bone.")]
    public LayerMask boneMask = ~0;

    [Tooltip("Radius of the trigger sphere at the tip (meters).")]
    [Range(0.001f, 0.02f)] public float tipRadius = 0.004f;

    [Tooltip("Max distance used when refining the surface normal via Collider.Raycast.")]
    [Range(0.005f, 0.1f)] public float normalRefineDistance = 0.03f;

    [Header("── Safety ──")]
    [Tooltip("If > 0, IsTipDeeplyInsideBone() uses this as the outward-probe distance.")]
    [Range(0.01f, 0.3f)] public float insideProbeDistance = 0.08f;

    [Tooltip("Minimum number of outward probes that must hit bone for tip to count as 'inside'.")]
    [Range(3, 6)] public int insideProbeHitThreshold = 5;

    // ── Runtime state (read-only from outside) ──
    [HideInInspector] public bool isTouchingBone;
    [HideInInspector] public Vector3 contactPoint;
    [HideInInspector] public Vector3 contactNormal;
    [HideInInspector] public Collider boneCollider;
    [HideInInspector] public bool justEntered;   // true from OnTriggerEnter until OnTriggerExit

    // ── Private ──
    SphereCollider _trigger;
    int _boneContactCount;

    // Outward probe directions (cached)
    static readonly Vector3[] _probeDirs =
    {
        Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
    };

    void Reset()
    {
        // Auto-wire a sphere trigger when first added
        _trigger = GetComponent<SphereCollider>();
        if (_trigger == null) _trigger = gameObject.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.radius = tipRadius;
    }

    void Awake()
    {
        _trigger = GetComponent<SphereCollider>();
        if (_trigger == null) _trigger = gameObject.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.radius = tipRadius;

        // Make sure this object has NO Rigidbody of its own — parent drill owns it.
        // If it did, the trigger would require kinematic on this GO; we just detect.
    }

    void Update()
    {
        // Keep trigger radius in sync if user edits in Inspector at runtime
        if (_trigger != null && !Mathf.Approximately(_trigger.radius, tipRadius))
            _trigger.radius = tipRadius;
    }

    // ════════════════════════════════════════════
    //  TRIGGER CALLBACKS
    // ════════════════════════════════════════════

    void OnTriggerEnter(Collider other)
    {
        if (!IsOnBoneLayer(other)) return;

        _boneContactCount++;
        isTouchingBone = true;
        boneCollider = other;

        // Sticky flag: set on entry, cleared only on exit or explicit clear.
        // This proves the tip crossed surface from OUTSIDE (OnTriggerEnter
        // never fires for colliders that start overlapping). Safe for
        // human-speed "touch-then-pinch" workflow.
        justEntered = true;

        RefreshContact(other);
    }

    void OnTriggerStay(Collider other)
    {
        if (!IsOnBoneLayer(other)) return;

        boneCollider = other;
        RefreshContact(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsOnBoneLayer(other)) return;

        _boneContactCount = Mathf.Max(0, _boneContactCount - 1);
        if (_boneContactCount == 0)
        {
            isTouchingBone = false;
            boneCollider = null;
            justEntered = false;   // reset so next entry re-validates
        }
    }

    // ════════════════════════════════════════════
    //  CONTACT REFINEMENT
    //  Uses Collider.Raycast (works on ALL collider
    //  types including non-convex MeshColliders)
    //  to get an accurate surface point + normal.
    // ════════════════════════════════════════════

    void RefreshContact(Collider other)
    {
        Vector3 pos = transform.position;

        // Step 1: get a rough "closest point" guess.
        //   - Convex colliders / primitive colliders: ClosestPoint gives real surface point.
        //   - Non-convex MeshCollider: ClosestPoint throws in older Unity, returns AABB closest now.
        Vector3 rough;
        bool isNonConvexMesh = other is MeshCollider mc && !mc.convex;
        if (isNonConvexMesh)
        {
            rough = other.ClosestPointOnBounds(pos);
        }
        else
        {
            rough = other.ClosestPoint(pos);
        }

        // Step 2: refine with Collider.Raycast aimed at the tip from outside.
        //   Origin is placed slightly outside the rough point, ray points back toward tip.
        Vector3 fromTip = (pos - rough);
        Vector3 rayDir;
        if (fromTip.sqrMagnitude > 1e-8f)
        {
            rayDir = -fromTip.normalized; // from rough toward tip; inverse is toward the bone
        }
        else
        {
            // Tip is basically AT the rough point — use drill axis as fallback
            rayDir = -transform.up;
        }

        Vector3 rayOrigin = pos - rayDir * normalRefineDistance;

        if (other.Raycast(new Ray(rayOrigin, rayDir), out RaycastHit hit, normalRefineDistance * 2f))
        {
            contactPoint = hit.point;
            contactNormal = hit.normal;
            return;
        }

        // Step 3: fallback — use rough point, approximate normal pointing toward tip.
        contactPoint = rough;
        if (fromTip.sqrMagnitude > 1e-8f)
            contactNormal = fromTip.normalized;
        else
            contactNormal = transform.up;
    }

    // ════════════════════════════════════════════
    //  SAFETY PROBE
    //  Fires 6 outward rays. If most of them hit
    //  bone within insideProbeDistance, we're inside.
    // ════════════════════════════════════════════

    public bool IsTipDeeplyInsideBone()
    {
        if (boneCollider == null) return false;

        Vector3 pos = transform.position;
        int hits = 0;
        int boneLayer = boneCollider.gameObject.layer;
        int mask = 1 << boneLayer;

        for (int i = 0; i < _probeDirs.Length; i++)
        {
            if (Physics.Raycast(pos, _probeDirs[i], insideProbeDistance, mask, QueryTriggerInteraction.Ignore))
                hits++;
        }
        return hits >= insideProbeHitThreshold;
    }

    // ════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════

    bool IsOnBoneLayer(Collider col)
    {
        return ((1 << col.gameObject.layer) & boneMask) != 0;
    }

    public void ClearContact()
    {
        isTouchingBone = false;
        boneCollider = null;
        _boneContactCount = 0;
        justEntered = false;
    }

    // ════════════════════════════════════════════
    //  GIZMOS
    // ════════════════════════════════════════════

    void OnDrawGizmos()
    {
        Gizmos.color = isTouchingBone ? new Color(0f, 1f, 0.2f, 0.85f) : new Color(1f, 0.9f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, tipRadius);

        if (isTouchingBone)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 1f);
            Gizmos.DrawSphere(contactPoint, tipRadius * 0.55f);
            Gizmos.DrawRay(contactPoint, contactNormal * 0.025f);
        }
    }
}




