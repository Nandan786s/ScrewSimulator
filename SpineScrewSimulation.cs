// ================================================================
//  SPINE SCREW SIMULATION  v7  —  PHYSICS-BASED  —  ULTRALEAP + MOUSE/KB
//  Unity New Input System | 2021.3+ | Leap Motion SDK
//
//  DELETE DrillSpawner.cs from your project — it conflicts.
//  Only keep THIS file + SpineScrewSetupEditor.cs
//
//  PHYSICS MODEL:
//  • Drilling only advances while RIGHT PINCH is held.
//    Release pinch = drill stops immediately (no auto-drill).
//  • Resistance increases with depth (bone gets harder).
//  • Friction opposes drill velocity — must sustain pinch force.
//  • Dust particles & audio intensity scale with drill speed.
//  • Screw insertion also has depth-based resistance + twist friction.
//
//  STABILITY:
//  • Hand position is smoothed via exponential filter + ring buffer.
//  • Once drilling starts, drill position is LOCKED (no jitter drift).
//  • Minimum hole spacing enforced to prevent overlapping holes.
//  • All thresholds have hysteresis to prevent flicker.
//
//  WORKFLOW (Ultraleap):
//  1. FREE LOOK  : Right hand aims yellow disc over bone.
//  2. Right Pinch (HOLD): Drills at pointer while held. Release = stop.
//  3. Right Grab : Switch to Screw mode (after at least 1 hole).
//  4. SCREW MODE : Right hand carries screw. Near hole = snaps green.
//                  Pinch + twist = drives screw in. Auto-picks next.
//  5. All done   : Reset dialog appears.
//  6. Both Fists : Reset anytime.
//
//  FALLBACK (Mouse + Keyboard):
//  Mouse=aim, E(hold)=drill, S=screw mode, LMB=drive, RMB=orbit, Q=free.
// ================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Leap;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(AudioSource))]
public class SpineScrewSimulation : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("── Bone ──")]
    public GameObject boneRoot;
    public LayerMask boneLayerMask = ~0;

    [Header("── Drill & Screw Size (single value) ──")]
    [Tooltip("Radius of hole AND screw shaft. Auto-set on Play from bone size.")]
    public float holeRadius = 0.03f;
    [Tooltip("How deep the hole goes (target depth).")]
    public float drillDepth = 0.06f;
    [Tooltip("Max holes allowed.")]
    public int maxHoles = 10;

    [Header("── Drill Physics ──")]
    [Tooltip("Base drilling speed in depth-units/sec at zero resistance.")]
    public float baseDrillRate = 0.06f;
    [Tooltip("How much the bone resists drilling overall (0=butter, 1=very hard).")]
    [Range(0f, 1f)]
    public float boneResistance = 0.55f;
    [Tooltip("Exponent: how steeply resistance increases with depth (1=linear, 2+=exponential).")]
    public float resistanceCurve = 1.8f;
    [Tooltip("Friction coefficient opposing drill advance (0=none, 1=max).")]
    [Range(0f, 1f)]
    public float drillFriction = 0.25f;
    [Tooltip("How quickly the drill decelerates when pinch is released (depth/sec retract).")]
    public float drillRetractRate = 0.0f;
    [Tooltip("Vibration amplitude while drilling (visual feedback).")]
    public float drillVibrationAmp = 0.001f;
    [Tooltip("Minimum pinch strength to actually advance the drill.")]
    [Range(0f, 1f)]
    public float minDrillPinchStrength = 0.65f;

    [Header("── Stability (Anti-Jitter) ──")]
    [Tooltip("Smoothing factor for hand position (0.01=very smooth, 1=raw). Lower = less jitter.")]
    [Range(0.01f, 1f)]
    public float handSmoothingFactor = 0.12f;
    [Tooltip("Number of frames to average for position smoothing.")]
    [Range(1, 20)]
    public int smoothBufferSize = 8;
    [Tooltip("Ignore hand movements smaller than this (world units).")]
    public float handDeadzone = 0.003f;
    [Tooltip("Minimum distance between hole centres to prevent overlap.")]
    public float minHoleSpacing = 0.015f;
    [Tooltip("Hysteresis band for pinch detection (prevents flicker).")]
    [Range(0f, 0.2f)]
    public float pinchHysteresis = 0.08f;

    [Header("── Screw ──")]
    public GameObject screwPrefab;
    public float screwLength = 0.20f;
    public float screwInsertSpeed = 0.15f;   // m/s
    public float screwSpinSpeed = 300f;    // deg/s
    public float snapRadius = 0.15f;

    [Header("── Screw Physics ──")]
    [Tooltip("Resistance to screw insertion (0=easy, 1=very hard).")]
    [Range(0f, 1f)]
    public float screwResistance = 0.4f;
    [Tooltip("Exponent for screw depth-based resistance increase.")]
    public float screwResistanceCurve = 1.3f;
    [Tooltip("Friction that opposes screw twist (requires more force at depth).")]
    [Range(0f, 1f)]
    public float screwFriction = 0.2f;

    [Header("── Camera ──")]
    public Transform pivotOverride;
    public float orbitSpeed = 0.35f;
    public float zoomSpeed = 3f;
    public float minDist = 0.1f;
    public float maxDist = 8f;

    [Header("── Audio ──")]
    public AudioClip drillSFX;
    public AudioClip screwSFX;
    public AudioClip doneSFX;

    [Header("── Ultraleap Hand Tracking ──")]
    [Tooltip("Hand position scale: maps Leap mm to world units.")]
    public float handPositionScale = 0.001f;
    [Tooltip("World-space offset added to the mapped hand position (calibrate to your setup).")]
    public Vector3 handPositionOffset = new Vector3(0f, 0f, 0f);
    [Tooltip("Pinch strength threshold to count as pinching (0-1).")]
    [Range(0f, 1f)]
    public float pinchThreshold = 0.85f;
    [Tooltip("Grab/fist strength threshold (0-1).")]
    [Range(0f, 1f)]
    public float grabThreshold = 0.8f;
    [Tooltip("Show transparent ghost hand at tracked positions.")]
    public bool showGhostHand = true;
    [Tooltip("Twist sensitivity multiplier for screw driving.")]
    public float twistSensitivity = 1.15f;
    [Tooltip("Max twist degrees per frame.")]
    public float maxTwistDeltaPerFrame = 20f;

    // ─────────────────────────────────────────────────────────────
    //  PRIVATE
    // ─────────────────────────────────────────────────────────────

    enum Mode { Free, Drilling, WaitForScrew, Screwing, Done }
    Mode _mode = Mode.Free;

    // Camera
    Camera _cam;
    Transform _pivot;
    float _yaw = 10f, _pitch = 20f, _dist = 2f;
    bool _orbiting;
    Vector2 _orbitOrigin;

    // Bone
    GameObject _bone;
    Bounds _boneBounds;

    // Pointer
    GameObject _needleGO, _discGO;
    Material _needleMat, _discMat;
    bool _hasHit;
    RaycastHit _hit;

    // Drill — physics-based state
    float _drillDepthCurrent;        // actual depth drilled so far (0 → drillDepth)
    float _drillVelocity;            // current drill velocity (depth/sec)
    float _currentResistanceForce;   // resistance being applied this frame (for HUD)
    Vector3 _drillPos, _drillNormal; // locked drill contact point/normal
    ParticleSystem _dustPS;
    bool _isPinchHeldDrill;          // is pinch currently held during active drilling

    // Holes
    class HoleData
    {
        public Vector3 pos, normal;
        public bool filled;
        public GameObject outerRing, innerDisc;
        public Material ringMat;
    }
    readonly List<HoleData> _holes = new List<HoleData>();

    // Screw in hand
    GameObject _heldScrew;
    Material[] _heldMats;
    HoleData _snap;
    float _insertT;

    // Misc
    AudioSource _audio;
    Material _boneMat, _holeDarkMat;
    GUIStyle _sTitle, _sBody, _sHint, _sBig;
    Texture2D _white;
    float _pulse;

    // ── Ultraleap runtime state ──
    Controller _leapController;
    bool _handTrackingReady;
    bool _prevRightPinch, _prevLeftPinch;
    bool _prevRightGrab, _prevLeftGrab;
    float _rightPinchStrength;   // current frame's pinch strength (smoothed)

    // ── Hand position smoothing ──
    Vector3 _smoothedHandPos;
    Vector3[] _handPosBuffer;
    int _handPosBufferIdx;
    bool _handPosInitialized;
    bool _pinchStateWithHysteresis;  // debounced pinch state

    // ── Twist-screw tracking ──
    bool _twistActive;
    float _prevTwistAngle;
    float _twistAccumulated;
    float _twistDeadzone = 2f;
    float _twistGripDistance = 50f;

    // ── Ghost hand visualisation ──
    GameObject _ghostHandRoot;
    GameObject[] _ghostFingerTips;
    GameObject _ghostPalm;
    Material _ghostMat;

    // ── Embedded Mode (used when hosted inside FourViewMedicalSetup) ──
    [HideInInspector] public bool embeddedMode;
    [HideInInspector] public Camera externalCamera;
    [HideInInspector] public Rect viewportPixelRect;  // pixel rect of the 3D quadrant
    bool _initialized;

    /// <summary>True when the simulation is active and should receive input.</summary>
    [HideInInspector] public bool simulationActive = true;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.spatialBlend = 0f;
        InitLeapController();
    }

    void Start()
    {
        // In embedded mode, host calls InitEmbedded() instead
        if (!embeddedMode)
        {
            InitCamera();
            FullInit();
        }
    }

    /// <summary>Call from host (FourViewMedicalSetup) after setting externalCamera.</summary>
    public void InitEmbedded()
    {
        _cam = externalCamera;
        FullInit();
    }

    void FullInit()
    {
        InitBone();
        InitPointer();
        InitDust();
        InitStyles();
        if (!embeddedMode) ApplyCamera();
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized) return;
        if (embeddedMode && !simulationActive) return;

        _pulse += Time.deltaTime * 3f;

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        // ── In embedded mode, ignore mouse input outside 3D viewport ──
        bool mouseInViewport = true;
        if (embeddedMode && mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            mouseInViewport = IsMouseInViewport(mp);
        }

        // ── Get hand tracking data ──
        Hand rightHand = GetLeapHand(true);
        Hand leftHand = GetLeapHand(false);
        bool hasHands = rightHand != null;

        // ── Smooth hand position to reduce Leap Motion jitter ──
        if (hasHands)
        {
            Vector3 rawPos = LeapToWorld(rightHand.PalmPosition);
            _smoothedHandPos = SmoothHandPosition(rawPos);
            _rightPinchStrength = rightHand.PinchStrength;
        }
        else
        {
            _rightPinchStrength = 0f;
        }

        // ── Update pinch state with hysteresis (prevents flicker) ──
        UpdatePinchHysteresis(rightHand);

        UpdateGhostHand(rightHand);

        // Both hands fist = reset
        if (IsHandGrabbing(rightHand) && IsHandGrabbing(leftHand))
        {
            ResetAll();
            UpdateHandState(rightHand, leftHand);
            return;
        }

        // ── Camera orbit: left hand grab or mouse RMB ──
        OrbitZoom(mouse, leftHand);

        if (kb != null && kb.qKey.wasPressedThisFrame) GoFree();

        // ── Raycast: prefer smoothed hand ray, fall back to mouse ──
        if (hasHands)
            _hasHit = BoneRaycast(SmoothedHandRay(rightHand), out _hit);
        else if (mouse != null && mouseInViewport)
            _hasHit = BoneRaycast(MouseRay(mouse), out _hit);
        else
            _hasHit = false;

        switch (_mode)
        {
            case Mode.Free: DoFree(kb, mouse, rightHand); break;
            case Mode.Drilling: DoDrilling(kb, mouse, rightHand); break;
            case Mode.WaitForScrew: DoWait(kb, rightHand); break;
            case Mode.Screwing: DoScrewing(mouse, rightHand); break;
        }

        DrawPointer();
        PulseRings();
        ApplyCamera();

        UpdateHandState(rightHand, leftHand);
    }

    void OnGUI()
    {
        if (!_initialized) return;
        if (embeddedMode && !simulationActive) return;
        HUD();
    }

    // ─────────────────────────────────────────────────────────────
    //  CAMERA
    // ─────────────────────────────────────────────────────────────

    void InitCamera()
    {
        if (embeddedMode && externalCamera != null)
        {
            _cam = externalCamera;
            return;
        }
        _cam = Camera.main;
        if (_cam == null)
        {
            var g = new GameObject("MainCamera"); g.tag = "MainCamera";
            _cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
    }

    void OrbitZoom(Mouse mouse, Hand leftHand)
    {
        // ── Left hand grab = orbit camera ──
        if (IsHandGrabbing(leftHand))
        {
            Vector3 vel = leftHand.PalmVelocity;
            _yaw += vel.x * orbitSpeed * 0.05f;
            _pitch -= vel.y * orbitSpeed * 0.05f;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);

            float zVel = vel.z;
            if (Mathf.Abs(zVel) > 0.05f)
            {
                _dist -= Mathf.Sign(zVel) * zoomSpeed * Time.deltaTime * 3f;
                _dist = Mathf.Clamp(_dist, minDist, maxDist);
            }
            return;
        }

        // ── Mouse fallback ──
        if (mouse == null) return;
        float scroll = mouse.scroll.ReadValue().y;
        _dist -= scroll * 0.01f * zoomSpeed * _dist;
        _dist = Mathf.Clamp(_dist, minDist, maxDist);

        bool rmb = mouse.rightButton.isPressed;
        bool mmb = mouse.middleButton.isPressed;
        if (rmb || mmb)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * orbitSpeed;
            _pitch -= delta.y * orbitSpeed;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);
        }
    }

    void ApplyCamera()
    {
        if (embeddedMode) return; // host controls the camera in embedded mode
        if (_cam == null || _pivot == null) return;
        _cam.transform.position = _pivot.position + Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * _dist;
        _cam.transform.LookAt(_pivot.position);
    }

    /// <summary>In embedded mode, checks if a screen-space point is inside the 3D viewport.</summary>
    bool IsMouseInViewport(Vector2 screenPos)
    {
        if (!embeddedMode) return true;
        // viewportPixelRect is in GUI coords (top-left origin)
        // screenPos from InputSystem is bottom-left origin
        float guiY = Screen.height - screenPos.y;
        return viewportPixelRect.Contains(new Vector2(screenPos.x, guiY));
    }

    // ─────────────────────────────────────────────────────────────
    //  RAYCAST — walks up transform hierarchy to confirm bone hit
    // ─────────────────────────────────────────────────────────────

    Ray MouseRay(Mouse mouse)
    {
        Vector2 p = mouse.position.ReadValue();
        return _cam.ScreenPointToRay(new Vector3(p.x, p.y, 0f));
    }

    bool BoneRaycast(Ray ray, out RaycastHit best)
    {
        RaycastHit[] all = Physics.RaycastAll(ray, 200f);
        System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in all)
        {
            if (IsOnBone(h.collider?.transform))
            { best = h; return true; }
        }
        best = default;
        return false;
    }

    bool IsOnBone(Transform t)
    {
        while (t != null)
        {
            if (t.gameObject == _bone) return true;
            t = t.parent;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  FREE LOOK
    // ─────────────────────────────────────────────────────────────

    void DoFree(Keyboard kb, Mouse mouse, Hand rightHand)
    {
        // ── Leap: right pinch START = begin drilling at raycast hit ──
        bool pinchJust = RightPinchJustStarted(rightHand);
        bool kbDrill = kb != null && kb.eKey.wasPressedThisFrame;

        // Mouse E-key held also starts drill (for keyboard fallback)
        if (!pinchJust && !kbDrill) return;
        if (!_hasHit)
        {
            Debug.Log("[SpineSim] Aim at bone then pinch (or press E).");
            return;
        }
        if (_holes.Count >= maxHoles)
        {
            Debug.Log("[SpineSim] Max holes reached. Grab fist (or press S) to start screwing.");
            return;
        }

        // ── Enforce minimum hole spacing ──
        foreach (var existingHole in _holes)
        {
            if (Vector3.Distance(_hit.point, existingHole.pos) < minHoleSpacing)
            {
                Debug.Log("[SpineSim] Too close to existing hole. Move to a different spot.");
                return;
            }
        }

        StartDrill(_hit.point, _hit.normal);
    }

    // ─────────────────────────────────────────────────────────────
    //  DRILLING — PHYSICS-BASED, PINCH-HELD ONLY
    // ─────────────────────────────────────────────────────────────

    void StartDrill(Vector3 pos, Vector3 normal)
    {
        _drillPos = pos;
        _drillNormal = normal;
        _drillDepthCurrent = 0f;
        _drillVelocity = 0f;
        _currentResistanceForce = 0f;
        _isPinchHeldDrill = true;
        _mode = Mode.Drilling;

        if (_dustPS != null)
        {
            _dustPS.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-normal));
            _dustPS.Play();
        }
    }

    void DoDrilling(Keyboard kb, Mouse mouse, Hand rightHand)
    {
        // ── Determine if drill input is active this frame ──
        bool handPinching = false;
        float pinchForce = 0f;

        if (rightHand != null)
        {
            // Use hysteresis-debounced state + raw strength for force
            handPinching = _pinchStateWithHysteresis;
            pinchForce = Mathf.Clamp01((_rightPinchStrength - minDrillPinchStrength)
                                       / (1f - minDrillPinchStrength));
        }

        // Keyboard fallback: E held = drilling with full force
        bool kbHeld = kb != null && kb.eKey.isPressed;
        // Mouse fallback: LMB held during drilling
        bool mouseHeld = mouse != null && mouse.leftButton.isPressed;

        bool isDriving = handPinching || kbHeld || mouseHeld;
        float inputForce = handPinching ? pinchForce : (isDriving ? 1f : 0f);

        _isPinchHeldDrill = isDriving;

        // ── Cancel drill: Q key or grab gesture ──
        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            CancelDrill();
            return;
        }

        if (isDriving && inputForce > 0f)
        {
            // ── PHYSICS: Calculate drill advance ──
            float depthFraction = Mathf.Clamp01(_drillDepthCurrent / drillDepth);

            // Resistance increases exponentially with depth
            float resistance = boneResistance * Mathf.Pow(depthFraction, resistanceCurve);

            // Friction opposes movement
            float frictionMod = 1f - drillFriction;

            // Effective drill rate: base * input_force * (1 - resistance) * friction_modifier
            float effectiveRate = baseDrillRate * inputForce * (1f - resistance) * frictionMod;

            // Ensure minimum progress so it doesn't feel stuck
            effectiveRate = Mathf.Max(effectiveRate, baseDrillRate * 0.05f * inputForce);

            _drillVelocity = effectiveRate;
            _currentResistanceForce = resistance;

            // Advance depth
            _drillDepthCurrent += effectiveRate * Time.deltaTime;

            // Audio: play and modulate pitch by velocity
            LoopAudio(drillSFX);
            if (_audio.isPlaying)
            {
                // Pitch shifts with drill speed (slower = lower pitch = harder bone feel)
                _audio.pitch = Mathf.Lerp(0.7f, 1.3f, effectiveRate / baseDrillRate);
                _audio.volume = Mathf.Lerp(0.4f, 1f, inputForce);
            }

            // Dust intensity scales with velocity
            if (_dustPS != null)
            {
                var emission = _dustPS.emission;
                emission.rateOverTime = Mathf.Lerp(10f, 80f, effectiveRate / baseDrillRate);
                var mainMod = _dustPS.main;
                mainMod.startSpeed = Mathf.Lerp(0.04f, 0.15f, effectiveRate / baseDrillRate);
            }

            // Visual vibration feedback — subtle shake on the pointer
            if (_needleGO != null && _needleGO.activeSelf)
            {
                float vibAmt = drillVibrationAmp * (0.5f + 0.5f * depthFraction);
                Vector3 vibOffset = new Vector3(
                    Random.Range(-vibAmt, vibAmt),
                    Random.Range(-vibAmt, vibAmt),
                    Random.Range(-vibAmt, vibAmt));
                _needleGO.transform.position += vibOffset;
            }
        }
        else
        {
            // ── NOT DRIVING: drill is idle ──
            _drillVelocity = 0f;

            // Optional slight retraction (simulates elastic bone pushback)
            if (drillRetractRate > 0f && _drillDepthCurrent > 0f)
            {
                _drillDepthCurrent -= drillRetractRate * Time.deltaTime;
                _drillDepthCurrent = Mathf.Max(0f, _drillDepthCurrent);
            }

            StopAudio();
            if (_dustPS != null && _dustPS.isPlaying)
            {
                var emission = _dustPS.emission;
                emission.rateOverTime = 0f;
            }
        }

        // ── Check completion ──
        if (_drillDepthCurrent >= drillDepth)
        {
            _drillDepthCurrent = drillDepth;
            CompleteDrill();
            return;
        }
    }

    void CompleteDrill()
    {
        StopAudio();
        if (_audio != null) { _audio.pitch = 1f; _audio.volume = 1f; }
        OneShot(doneSFX);
        _dustPS?.Stop();

        var h = new HoleData { pos = _drillPos, normal = _drillNormal };
        SpawnHoleMarkers(h);
        _holes.Add(h);

        _drillDepthCurrent = 0f;
        _drillVelocity = 0f;
        _mode = Mode.WaitForScrew;
        Debug.Log($"[SpineSim] Hole {_holes.Count} drilled. Pinch/E for more, Grab/S to screw.");
    }

    void CancelDrill()
    {
        StopAudio();
        if (_audio != null) { _audio.pitch = 1f; _audio.volume = 1f; }
        _dustPS?.Stop();
        _drillDepthCurrent = 0f;
        _drillVelocity = 0f;
        _mode = Mode.Free;
        Debug.Log("[SpineSim] Drill cancelled.");
    }

    // ─────────────────────────────────────────────────────────────
    //  WAIT (between drills or before screwing)
    // ─────────────────────────────────────────────────────────────

    void DoWait(Keyboard kb, Hand rightHand)
    {
        bool pinchJust = RightPinchJustStarted(rightHand);
        bool grabJust = RightGrabJustStarted(rightHand);

        // Pinch or E = drill more
        if (pinchJust || (kb != null && kb.eKey.wasPressedThisFrame))
        {
            if (_hasHit && _holes.Count < maxHoles)
            {
                // Enforce minimum hole spacing
                bool tooClose = false;
                foreach (var existingHole in _holes)
                {
                    if (Vector3.Distance(_hit.point, existingHole.pos) < minHoleSpacing)
                    { tooClose = true; break; }
                }
                if (!tooClose)
                    StartDrill(_hit.point, _hit.normal);
                else
                    Debug.Log("[SpineSim] Too close to existing hole.");
            }
            else
                StartScrewing();
        }
        // Grab/fist or S = start screwing
        if (grabJust || (kb != null && kb.sKey.wasPressedThisFrame && _holes.Count > 0))
            StartScrewing();
    }

    // ─────────────────────────────────────────────────────────────
    //  HOLE MARKERS
    // ─────────────────────────────────────────────────────────────

    void SpawnHoleMarkers(HoleData h)
    {
        float r = holeRadius;

        // Outer gold ring
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "HoleRing";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.position = h.pos + h.normal * 0.002f;
        ring.transform.up = h.normal;
        ring.transform.localScale = new Vector3(r * 6f, 0.001f, r * 6f);
        var rm = new Material(Shader()) { color = new Color(1f, 0.85f, 0f) };
        ring.GetComponent<Renderer>().material = rm;
        h.outerRing = ring; h.ringMat = rm;

        // Middle darker ring
        var mid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mid.name = "HoleMid";
        Destroy(mid.GetComponent<Collider>());
        mid.transform.position = h.pos + h.normal * 0.003f;
        mid.transform.up = h.normal;
        mid.transform.localScale = new Vector3(r * 3.5f, 0.0012f, r * 3.5f);
        mid.GetComponent<Renderer>().material = new Material(Shader()) { color = new Color(0.55f, 0.4f, 0f) };

        // Centre black hole disc — exact hole size
        var cen = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cen.name = "HoleCentre";
        Destroy(cen.GetComponent<Collider>());
        cen.transform.position = h.pos + h.normal * 0.004f;
        cen.transform.up = h.normal;
        cen.transform.localScale = new Vector3(r * 2f, 0.0015f, r * 2f);
        if (_holeDarkMat == null) _holeDarkMat = new Material(Shader()) { color = new Color(0.05f, 0.02f, 0.02f) };
        cen.GetComponent<Renderer>().material = _holeDarkMat;
        h.innerDisc = cen;
    }

    void PulseRings()
    {
        float p = 0.82f + 0.18f * Mathf.Sin(_pulse * 2.5f);
        float sp = 0.70f + 0.30f * Mathf.Sin(_pulse * 5.0f);
        float r = holeRadius;

        foreach (var h in _holes)
        {
            if (h.ringMat == null) continue;
            if (h.filled)
            {
                h.ringMat.color = new Color(0.4f, 0.4f, 0.45f);
                if (h.outerRing) h.outerRing.transform.localScale = new Vector3(r * 6f, 0.001f, r * 6f);
                continue;
            }
            bool isSnap = h == _snap;
            Color c = isSnap ? new Color(0.2f, 1f, 0.35f) : new Color(1f, 0.85f, 0f);
            float f = isSnap ? sp : p;
            h.ringMat.color = c * f;
            if (h.outerRing)
            {
                float s = r * 6f * (isSnap ? 1f + 0.2f * Mathf.Sin(_pulse * 5f) : 1f + 0.07f * Mathf.Sin(_pulse * 2.5f));
                h.outerRing.transform.localScale = new Vector3(s, 0.001f, s);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SCREW MODE
    // ─────────────────────────────────────────────────────────────

    void StartScrewing()
    {
        _mode = Mode.Screwing;
        SpawnScrew();
        Debug.Log("[SpineSim] Screw mode. Move mouse over a gold ring.");
    }

    void SpawnScrew()
    {
        if (_heldScrew != null) Destroy(_heldScrew);
        _heldScrew = screwPrefab != null ? Instantiate(screwPrefab) : BuildScrew();
        _heldScrew.name = "HeldScrew";
        var rends = _heldScrew.GetComponentsInChildren<Renderer>();
        _heldMats = new Material[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            _heldMats[i] = new Material(rends[i].sharedMaterial ?? new Material(Shader()));
            rends[i].material = _heldMats[i];
        }
        _snap = null; _insertT = 0f;
    }

    void TintScrew(Color c) { if (_heldMats != null) foreach (var m in _heldMats) if (m) m.color = c; }

    void DoScrewing(Mouse mouse, Hand rightHand)
    {
        if (_heldScrew == null) return;

        bool hasHands = rightHand != null;

        // Find nearest unfilled hole to pointer position on bone
        _snap = null;
        float best = snapRadius;
        if (_hasHit)
        {
            foreach (var h in _holes)
            {
                if (h.filled) continue;
                float d = Vector3.Distance(_hit.point, h.pos);
                if (d < best) { best = d; _snap = h; }
            }
        }

        if (_snap != null)
        {
            // ── SNAPPED: orient screw perpendicular to bone surface ──
            TintScrew(new Color(0.25f, 1f, 0.4f));

            Vector3 n = _snap.normal;
            Vector3 right = Vector3.Cross(n, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(n, Vector3.forward);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, n);
            Quaternion targetRot = Quaternion.LookRotation(fwd, n);

            Vector3 above = _snap.pos + n * screwLength;
            Vector3 inPos = _snap.pos - n * (drillDepth * 0.75f);
            Vector3 targetPos = Vector3.Lerp(above, inPos, _insertT);

            _heldScrew.transform.position = Vector3.Lerp(_heldScrew.transform.position, targetPos, Time.deltaTime * 20f);
            _heldScrew.transform.rotation = Quaternion.Slerp(_heldScrew.transform.rotation, targetRot, Time.deltaTime * 20f);

            // ── Drive screw: Leap twist gesture OR mouse LMB ──
            bool driving = false;

            // ── Screw physics: resistance and friction ──
            float screwDepthFrac = Mathf.Clamp01(_insertT);
            float screwResist = screwResistance * Mathf.Pow(screwDepthFrac, screwResistanceCurve);
            float screwFricMod = 1f - screwFriction * screwDepthFrac;

            if (hasHands)
            {
                // Thumb+index twist gesture (from CubeDigSimulator)
                Finger thumb = rightHand.fingers[0];
                Finger index = rightHand.fingers[1];
                Vector3 thumbTip = thumb.TipPosition;
                Vector3 indexTip = index.TipPosition;
                float fingerDist = Vector3.Distance(thumbTip, indexTip);
                bool pinchAssist = rightHand.PinchStrength >= pinchThreshold * 0.55f;
                bool gripping = fingerDist < _twistGripDistance && pinchAssist;

                if (gripping)
                {
                    driving = true;
                    Vector3 thumbToIndex = indexTip - thumbTip;
                    Vector3 projected = Vector3.ProjectOnPlane(thumbToIndex, n);

                    if (projected.sqrMagnitude > 0.001f)
                    {
                        Vector3 refDir = Vector3.ProjectOnPlane(Vector3.right, n);
                        if (refDir.sqrMagnitude < 0.001f)
                            refDir = Vector3.ProjectOnPlane(Vector3.forward, n);
                        refDir.Normalize();
                        projected.Normalize();

                        float angle = Vector3.SignedAngle(refDir, projected, n);

                        if (!_twistActive)
                        {
                            _twistActive = true;
                            _prevTwistAngle = angle;
                            _twistAccumulated = 0f;
                        }
                        else
                        {
                            float delta = Mathf.DeltaAngle(_prevTwistAngle, angle);
                            _prevTwistAngle = angle;

                            if (Mathf.Abs(delta) < _twistDeadzone) delta = 0f;
                            delta = Mathf.Clamp(delta * twistSensitivity, -maxTwistDeltaPerFrame, maxTwistDeltaPerFrame);
                            _twistAccumulated += delta;

                            // Apply screw resistance to twist: harder to turn at depth
                            float effectiveDelta = delta * (1f - screwResist) * screwFricMod;

                            // Spin screw around insertion axis
                            _heldScrew.transform.Rotate(n, effectiveDelta, Space.World);

                            // Drive deeper only on clockwise twist
                            if (effectiveDelta > 0f)
                            {
                                float insertRate = (effectiveDelta / 360f) * (screwInsertSpeed / screwLength) * 60f;
                                insertRate *= (1f - screwResist) * screwFricMod;
                                _insertT += insertRate;
                                _insertT = Mathf.Clamp01(_insertT);
                            }
                        }
                    }
                }
                else
                {
                    if (_twistActive) _twistActive = false;
                }
            }

            // Mouse LMB fallback — also applies screw physics
            if (!driving && mouse != null && mouse.leftButton.isPressed)
            {
                driving = true;
                float effectiveSpinSpeed = screwSpinSpeed * (1f - screwResist) * screwFricMod;
                _heldScrew.transform.Rotate(n, effectiveSpinSpeed * Time.deltaTime, Space.World);
                float effectiveInsertRate = (screwInsertSpeed / screwLength) * (1f - screwResist) * screwFricMod;
                _insertT += effectiveInsertRate * Time.deltaTime;
                _insertT = Mathf.Clamp01(_insertT);
            }

            if (driving)
            {
                LoopAudio(screwSFX);
                // Modulate audio by depth resistance
                if (_audio.isPlaying)
                {
                    _audio.pitch = Mathf.Lerp(1.1f, 0.75f, screwDepthFrac);
                    _audio.volume = Mathf.Lerp(0.5f, 1f, screwDepthFrac);
                }
                if (_insertT >= 1f) FinishScrew(_snap);
            }
            else
            {
                StopAudio();
            }
        }
        else
        {
            // ── FREE: float screw near hand or mouse ──
            StopAudio();
            if (_twistActive) _twistActive = false;
            TintScrew(new Color(0.75f, 0.75f, 0.82f));

            if (hasHands)
            {
                if (_hasHit)
                {
                    Vector3 n = _hit.normal;
                    Vector3 r2 = Vector3.Cross(n, Vector3.up);
                    if (r2.sqrMagnitude < 0.001f) r2 = Vector3.Cross(n, Vector3.forward);
                    Vector3 f2 = Vector3.Cross(r2.normalized, n);
                    _heldScrew.transform.position = _hit.point + n * (screwLength * 0.55f);
                    _heldScrew.transform.rotation = Quaternion.Slerp(_heldScrew.transform.rotation,
                        Quaternion.LookRotation(f2, n), Time.deltaTime * 15f);
                }
                else
                {
                    _heldScrew.transform.position = Vector3.Lerp(
                        _heldScrew.transform.position, _smoothedHandPos, Time.deltaTime * 12f);
                    _heldScrew.transform.rotation = Quaternion.LookRotation(_cam.transform.forward);
                }
            }
            else if (_hasHit)
            {
                Vector3 n = _hit.normal;
                Vector3 r2 = Vector3.Cross(n, Vector3.up);
                if (r2.sqrMagnitude < 0.001f) r2 = Vector3.Cross(n, Vector3.forward);
                Vector3 f2 = Vector3.Cross(r2.normalized, n);
                _heldScrew.transform.position = _hit.point + n * (screwLength * 0.55f);
                _heldScrew.transform.rotation = Quaternion.Slerp(_heldScrew.transform.rotation,
                    Quaternion.LookRotation(f2, n), Time.deltaTime * 15f);
            }
            else if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                _heldScrew.transform.position = _cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, _dist * 0.5f));
                _heldScrew.transform.rotation = Quaternion.LookRotation(_cam.transform.forward);
            }
        }
    }

    void FinishScrew(HoleData h)
    {
        h.filled = true;
        StopAudio();
        if (_audio != null) { _audio.pitch = 1f; _audio.volume = 1f; }
        OneShot(doneSFX);
        _heldScrew.name = "Screw_" + _holes.IndexOf(h);
        _heldScrew = null;
        _snap = null;

        bool allDone = true;
        foreach (var hole in _holes) if (!hole.filled) { allDone = false; break; }

        if (allDone) { _mode = Mode.Done; Debug.Log("[SpineSim] All screws inserted!"); }
        else { SpawnScrew(); }
    }

    // ─────────────────────────────────────────────────────────────
    //  POINTER (needle + disc)
    // ─────────────────────────────────────────────────────────────

    void InitPointer()
    {
        _needleGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _needleGO.name = "Needle";
        Destroy(_needleGO.GetComponent<Collider>());
        _needleMat = new Material(Shader());
        _needleGO.GetComponent<Renderer>().material = _needleMat;
        _needleGO.SetActive(false);

        _discGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _discGO.name = "Disc";
        Destroy(_discGO.GetComponent<Collider>());
        _discMat = new Material(Shader());
        _discGO.GetComponent<Renderer>().material = _discMat;
        _discGO.SetActive(false);
    }

    void DrawPointer()
    {
        bool show = _mode == Mode.Free || _mode == Mode.WaitForScrew || _mode == Mode.Drilling;
        if (!show) { _needleGO.SetActive(false); _discGO.SetActive(false); return; }

        Vector3 pos; Vector3 nrm; Color col;
        if (_mode == Mode.Drilling)
        {
            pos = _drillPos; nrm = _drillNormal;
            // Colour shifts from orange to red as depth increases (visual resistance feedback)
            float depthFrac = Mathf.Clamp01(_drillDepthCurrent / drillDepth);
            col = Color.Lerp(new Color(1f, 0.6f, 0.05f), new Color(1f, 0.1f, 0.05f), depthFrac);
        }
        else if (_mode == Mode.WaitForScrew)
        { pos = _drillPos; nrm = _drillNormal; col = new Color(0.3f, 1f, 0.3f); }
        else if (_hasHit)
        { pos = _hit.point; nrm = _hit.normal; col = new Color(1f, 1f, 0.1f); }
        else { _needleGO.SetActive(false); _discGO.SetActive(false); return; }

        float nLen = _boneBounds.extents.magnitude * 0.4f;
        float nRad = holeRadius * 0.4f;
        float pls = 1f + 0.1f * Mathf.Sin(_pulse * 4f);

        _needleGO.SetActive(true);
        _needleGO.transform.position = pos + nrm * (nLen * 0.5f);
        _needleGO.transform.up = nrm;
        _needleGO.transform.localScale = new Vector3(nRad, nLen * 0.5f, nRad);
        _needleMat.color = col;

        _discGO.SetActive(true);
        _discGO.transform.position = pos + nrm * 0.001f;
        _discGO.transform.up = nrm;
        float dr = holeRadius * 4f * pls;
        _discGO.transform.localScale = new Vector3(dr, 0.0005f, dr);
        _discMat.color = new Color(col.r, col.g, col.b, 0.8f);
    }

    // ─────────────────────────────────────────────────────────────
    //  RESET
    // ─────────────────────────────────────────────────────────────

    void GoFree()
    {
        StopAudio();
        if (_audio != null) { _audio.pitch = 1f; _audio.volume = 1f; }
        _dustPS?.Stop();
        if (_heldScrew) { Destroy(_heldScrew); _heldScrew = null; }
        _snap = null;
        _twistActive = false;
        _twistAccumulated = 0f;
        _drillDepthCurrent = 0f;
        _drillVelocity = 0f;
        _isPinchHeldDrill = false;
        _mode = Mode.Free;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ResetAll()
    {
        GoFree();
        foreach (var h in _holes)
        {
            if (h.outerRing) Destroy(h.outerRing);
            if (h.innerDisc) Destroy(h.innerDisc);
        }
        _holes.Clear();
        foreach (var go in FindObjectsOfType<GameObject>())
            if (go && go.name.StartsWith("Screw_")) Destroy(go);
        foreach (var go in FindObjectsOfType<GameObject>())
            if (go && (go.name == "HoleMid" || go.name == "HoleCentre" || go.name == "HoleRing"))
                Destroy(go);
    }

    // ─────────────────────────────────────────────────────────────
    //  INIT — BONE
    // ─────────────────────────────────────────────────────────────

    void InitBone()
    {
        _bone = boneRoot != null ? boneRoot : BuildDummy();

        // Add MeshCollider to EVERY child mesh that has none
        int added = 0;
        foreach (var mf in _bone.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
            if (!mf.sharedMesh.isReadable)
            { mf.gameObject.AddComponent<BoxCollider>(); added++; continue; }
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            added++;
        }
        if (added == 0 && !_bone.GetComponentInChildren<Collider>())
            _bone.AddComponent<BoxCollider>();

        // Bounds
        var rs = _bone.GetComponentsInChildren<Renderer>(true);
        _boneBounds = rs.Length > 0 ? rs[0].bounds : new Bounds(_bone.transform.position, Vector3.one);
        foreach (var r in rs) _boneBounds.Encapsulate(r.bounds);

        // Auto-size everything to bone
        float sz = _boneBounds.extents.magnitude;
        holeRadius = Mathf.Clamp(sz * 0.065f, 0.005f, 0.2f);
        screwLength = Mathf.Clamp(sz * 0.45f, 0.02f, 1.5f);
        snapRadius = Mathf.Clamp(sz * 0.35f, 0.02f, 1.5f);
        drillDepth = Mathf.Clamp(sz * 0.20f, 0.01f, 0.5f);
        screwInsertSpeed = Mathf.Clamp(sz * 0.35f, 0.02f, 1f);
        baseDrillRate = Mathf.Clamp(drillDepth * 0.8f, 0.01f, 0.3f);
        minHoleSpacing = Mathf.Max(holeRadius * 3f, 0.01f);
        _dist = Mathf.Clamp(sz * 3.0f, minDist, maxDist);

        // Pivot for camera
        if (pivotOverride != null) _pivot = pivotOverride;
        else
        {
            var pg = new GameObject("_CamPivot");
            pg.transform.position = _boneBounds.center;
            _pivot = pg.transform;
        }

        Debug.Log($"[SpineSim] Ready. boneSize={sz:F3} holeR={holeRadius:F3} screwL={screwLength:F3} snap={snapRadius:F3}  Added {added} colliders.");
    }

    // ─────────────────────────────────────────────────────────────
    //  INIT — DUST
    // ─────────────────────────────────────────────────────────────

    void InitDust()
    {
        var g = new GameObject("_Dust"); g.transform.SetParent(transform);
        _dustPS = g.AddComponent<ParticleSystem>();
        var m = _dustPS.main; m.loop = false; m.playOnAwake = false;
        m.startLifetime = 0.5f; m.startSpeed = 0.12f; m.startSize = 0.006f; m.maxParticles = 120;
        m.startColor = new Color(0.85f, 0.78f, 0.60f);
        var e = _dustPS.emission; e.rateOverTime = 50;
        var sh = _dustPS.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 20f; sh.radius = 0.004f;
        _dustPS.GetComponent<ParticleSystemRenderer>().material = new Material(Shader()) { color = new Color(0.85f, 0.78f, 0.60f) };
    }

    // ─────────────────────────────────────────────────────────────
    //  INIT — DUMMY BONE
    // ─────────────────────────────────────────────────────────────

    GameObject BuildDummy()
    {
        var root = new GameObject("DummyBone");
        _boneMat = new Material(Shader()) { color = new Color(0.92f, 0.87f, 0.76f) };
        Prim(PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(0.07f, 0.04f, 0.07f));
        Prim(PrimitiveType.Cube, root, new Vector3(-0.09f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.04f));
        Prim(PrimitiveType.Cube, root, new Vector3(0.09f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.04f));
        Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.055f, -0.05f), new Vector3(0.015f, 0.035f, 0.015f));
        return root;
    }

    void Prim(PrimitiveType t, GameObject root, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(t);
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().material = _boneMat;
    }

    // ─────────────────────────────────────────────────────────────
    //  SCREW BUILDER
    // ─────────────────────────────────────────────────────────────

    GameObject BuildScrew()
    {
        float r = holeRadius;          // shaft = hole radius (exact fit)
        float rt = r * 1.42f;           // thread outer (grips wall)
        float rh = r * 2.4f;            // head (stops flush)
        var matShaft = new Material(Shader()) { color = new Color(0.72f, 0.75f, 0.82f) };
        var matThread = new Material(Shader()) { color = new Color(0.52f, 0.55f, 0.62f) };
        var matHead = new Material(Shader()) { color = new Color(0.85f, 0.85f, 0.90f) };
        var matSlot = new Material(Shader()) { color = new Color(0.28f, 0.28f, 0.32f) };
        var root = new GameObject("Screw");

        // Pointed tip
        SC(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(r * 2f, r * 2.5f, r * 2f), matShaft);
        // Shaft
        SC(PrimitiveType.Cylinder, root, new Vector3(0, screwLength * 0.5f, 0), new Vector3(r * 2f, screwLength * 0.5f, r * 2f), matShaft);
        // Head
        SC(PrimitiveType.Cylinder, root, new Vector3(0, screwLength + r * 1.8f, 0), new Vector3(rh * 2f, r * 1.6f, rh * 2f), matHead);
        // Cross slot
        SC(PrimitiveType.Cube, root, new Vector3(0, screwLength + r * 3.5f, 0), new Vector3(r * 0.35f, r * 0.45f, rh * 1.9f), matSlot);
        SC(PrimitiveType.Cube, root, new Vector3(0, screwLength + r * 3.5f, 0), new Vector3(rh * 1.9f, r * 0.45f, r * 0.35f), matSlot);
        // Threads
        int tc = Mathf.Max(4, Mathf.RoundToInt(screwLength / (r * 1.7f)));
        for (int i = 0; i < tc; i++)
        {
            float y = r + i * (screwLength * 0.88f / tc);
            SC(PrimitiveType.Cylinder, root, new Vector3(0, y, 0), new Vector3(rt * 2f, r * 0.3f, rt * 2f), matThread);
        }
        return root;
    }

    void SC(PrimitiveType t, GameObject root, Vector3 pos, Vector3 scale, Material mat)
    {
        var g = GameObject.CreatePrimitive(t);
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().material = mat;
        Destroy(g.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────
    //  AUDIO
    // ─────────────────────────────────────────────────────────────

    void LoopAudio(AudioClip c)
    {
        if (c == null) return;
        if (_audio.clip == c && _audio.isPlaying) return;
        _audio.loop = true; _audio.clip = c; _audio.Play();
    }
    void StopAudio() { if (_audio.loop && _audio.isPlaying) { _audio.Stop(); _audio.loop = false; } }
    void OneShot(AudioClip c) { if (c) _audio.PlayOneShot(c); }

    // ─────────────────────────────────────────────────────────────
    //  SHADER HELPER
    // ─────────────────────────────────────────────────────────────

    Shader Shader() => UnityEngine.Shader.Find("Universal Render Pipeline/Lit")
                    ?? UnityEngine.Shader.Find("Standard")
                    ?? UnityEngine.Shader.Find("Diffuse");

    // ─────────────────────────────────────────────────────────────
    //  HUD
    // ─────────────────────────────────────────────────────────────

    void InitStyles()
    {
        _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
        _sTitle = Sty(18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        _sBody = Sty(14, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
        _sHint = Sty(13, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f));
        _sBig = Sty(26, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.3f, 1f, 0.5f));
    }
    GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c)
    { var g = new GUIStyle { fontSize = sz, fontStyle = fs, alignment = a }; g.normal.textColor = c; return g; }

    void Rect(UnityEngine.Rect r, Color c)
    { GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = Color.white; }

    void Hint(float x, float y, string key, string desc)
    {
        var ks = new GUIStyle(_sHint) { fontStyle = FontStyle.Bold };
        ks.normal.textColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        GUI.Label(new UnityEngine.Rect(x, y, 90, 20), key, ks);
        GUI.Label(new UnityEngine.Rect(x + 92, y, 220, 20), desc, _sHint);
    }

    void HUD()
    {
        if (_sTitle == null) InitStyles();

        // In embedded mode, clip HUD to the 3D viewport region
        float ox = 0f, oy = 0f, sw, sh;
        if (embeddedMode)
        {
            ox = viewportPixelRect.x;
            oy = viewportPixelRect.y;
            sw = viewportPixelRect.width;
            sh = viewportPixelRect.height;
            GUI.BeginGroup(viewportPixelRect);
        }
        else
        {
            sw = Screen.width;
            sh = Screen.height;
        }

        // Top bar
        Rect(new UnityEngine.Rect(0, 0, sw, 46), new Color(0, 0, 0, 0.62f));

        string label; Color lc;
        int filled = 0; foreach (var h in _holes) if (h.filled) filled++;

        switch (_mode)
        {
            case Mode.Drilling:
                label = _isPinchHeldDrill ? "⬤ DRILLING — Hold pinch!" : "⏸ DRILL PAUSED — Pinch to resume";
                lc = _isPinchHeldDrill ? new Color(1f, 0.3f, 0.05f) : new Color(1f, 0.7f, 0.2f);
                break;
            case Mode.WaitForScrew: label = "✔ HOLE READY — Pinch/E=more  Grab/S=start screwing"; lc = new Color(0.3f, 1f, 0.3f); break;
            case Mode.Screwing: label = "⬤ SCREWING — move to gold ring, Pinch+Twist"; lc = new Color(0.2f, 0.85f, 1f); break;
            case Mode.Done: label = "✔ ALL DONE"; lc = new Color(0.3f, 1f, 0.5f); break;
            default: label = "◈ FREE LOOK — move mouse, E to drill"; lc = new Color(0.9f, 0.9f, 0.9f); break;
        }
        var ls = new GUIStyle(_sTitle); ls.normal.textColor = lc;
        GUI.Label(new UnityEngine.Rect(14, 12, sw - 200, 26), label, ls);

        var rs = new GUIStyle(_sHint) { alignment = TextAnchor.MiddleRight };
        GUI.Label(new UnityEngine.Rect(sw - 240, 12, 228, 26), $"Holes: {_holes.Count}/{maxHoles}  Screws: {filled}/{_holes.Count}", rs);

        // Drill progress bar — shows depth and resistance
        if (_mode == Mode.Drilling)
        {
            float depthFrac = Mathf.Clamp01(_drillDepthCurrent / drillDepth);
            float bw = 320, bh = 24, bx = sw / 2f - 160, by = sh * 0.58f;

            // Depth bar
            Rect(new UnityEngine.Rect(bx - 2, by - 2, bw + 4, bh + 4), new Color(0, 0, 0, 0.8f));
            Color barCol = _isPinchHeldDrill
                ? Color.Lerp(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.15f, 0.05f), depthFrac)
                : new Color(0.5f, 0.5f, 0.3f);
            Rect(new UnityEngine.Rect(bx, by, bw * depthFrac, bh), barCol);
            Rect(new UnityEngine.Rect(bx + bw * depthFrac, by, bw * (1f - depthFrac), bh), new Color(0.15f, 0.08f, 0.04f, 0.5f));
            var ps = new GUIStyle(_sBody) { fontSize = 13 };
            string drillLabel = _isPinchHeldDrill
                ? $"Depth: {Mathf.RoundToInt(depthFrac * 100)}%  Resistance: {Mathf.RoundToInt(_currentResistanceForce * 100)}%"
                : $"PAUSED — Depth: {Mathf.RoundToInt(depthFrac * 100)}%  (Pinch to drill)";
            GUI.Label(new UnityEngine.Rect(bx, by, bw, bh), drillLabel, ps);

            // Resistance indicator bar below
            float rby = by + bh + 6;
            float rbh = 10;
            Rect(new UnityEngine.Rect(bx - 2, rby - 1, bw + 4, rbh + 2), new Color(0, 0, 0, 0.6f));
            Color resistCol = Color.Lerp(new Color(0.2f, 0.8f, 0.2f), new Color(1f, 0.2f, 0.1f), _currentResistanceForce);
            Rect(new UnityEngine.Rect(bx, rby, bw * _currentResistanceForce, rbh), resistCol);
            var rs2 = new GUIStyle(_sHint) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new UnityEngine.Rect(bx, rby, bw, rbh), "BONE RESISTANCE", rs2);
        }

        // Snap indicator with screw depth
        if (_mode == Mode.Screwing)
        {
            var ss = new GUIStyle(_sBody) { fontSize = 14 };
            if (_snap != null)
            {
                ss.normal.textColor = new Color(0.25f, 1f, 0.4f);
                Rect(new UnityEngine.Rect(sw / 2f - 220, sh * 0.60f, 440, 30), new Color(0, 0, 0, 0.65f));
                GUI.Label(new UnityEngine.Rect(sw / 2f - 220, sh * 0.60f, 440, 30),
                    $"✔ ALIGNED — Pinch+Twist to drive  ({Mathf.RoundToInt(_insertT * 100)}%)", ss);

                // Screw insertion progress bar
                float sbw = 300, sbh = 14, sbx = sw / 2f - 150, sby = sh * 0.60f + 34;
                Rect(new UnityEngine.Rect(sbx - 1, sby - 1, sbw + 2, sbh + 2), new Color(0, 0, 0, 0.7f));
                float screwFrac = Mathf.Clamp01(_insertT);
                Color screwBarCol = Color.Lerp(new Color(0.2f, 0.7f, 1f), new Color(0.1f, 0.4f, 0.9f), screwFrac);
                Rect(new UnityEngine.Rect(sbx, sby, sbw * screwFrac, sbh), screwBarCol);
            }
            else
            {
                ss.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
                GUI.Label(new UnityEngine.Rect(sw / 2f - 200, sh * 0.62f, 400, 26), "Move hand (or mouse) over a gold ring to snap", ss);
            }
        }

        // Wait prompt
        if (_mode == Mode.WaitForScrew)
        {
            Rect(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f, 400, 50), new Color(0, 0.08f, 0, 0.82f));
            var a1 = new GUIStyle(_sBody) { fontSize = 15 }; a1.normal.textColor = new Color(0.4f, 1f, 0.4f);
            GUI.Label(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f + 2, 400, 22), "Hole drilled!", a1);
            var a2 = new GUIStyle(_sBody) { fontSize = 13 }; a2.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Label(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f + 24, 400, 22), "Pinch/E = more holes   |   Grab/S = start screwing", a2);
        }

        // Done overlay
        if (_mode == Mode.Done)
        {
            Rect(new UnityEngine.Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.5f));
            float pw = 460, ph = 190, px = sw / 2f - 230, py = sh / 2f - 95;
            Rect(new UnityEngine.Rect(px, py, pw, ph), new Color(0.04f, 0.1f, 0.06f, 0.97f));
            Rect(new UnityEngine.Rect(px, py, pw, 4), new Color(0.3f, 1f, 0.5f));
            GUI.Label(new UnityEngine.Rect(px, py + 16, pw, 38), "SIMULATION COMPLETE", _sBig);
            var sb = new GUIStyle(_sBody); sb.normal.textColor = new Color(0.75f, 0.95f, 0.8f);
            GUI.Label(new UnityEngine.Rect(px, py + 62, pw, 24), $"All {_holes.Count} screws inserted.", sb);
            if (GUI.Button(new UnityEngine.Rect(px + pw / 2f - 90, py + 128, 180, 42), "↺  Reset Simulation"))
                ResetAll();
            if (embeddedMode) GUI.EndGroup();
            return;
        }

        // Hint strip
        Rect(new UnityEngine.Rect(0, sh - 160, 340, 160), new Color(0, 0, 0, 0.5f));
        float hy = sh - 154f;
        if (_mode == Mode.Free || _mode == Mode.WaitForScrew)
        {
            Hint(10, hy, "R-Hand", "Aim at bone"); hy += 22;
            Hint(10, hy, "R-Pinch(hold)", "Drill while held"); hy += 22;
            Hint(10, hy, "R-Grab/S", "Start screwing"); hy += 22;
            Hint(10, hy, "L-Grab", "Orbit camera"); hy += 22;
            Hint(10, hy, "Both Fists", "Reset"); hy += 22;
            Hint(10, hy, "Q", "Back to Free Look");
        }
        else if (_mode == Mode.Drilling)
        {
            Hint(10, hy, "Hold Pinch/E", "Keep drilling"); hy += 22;
            Hint(10, hy, "Release", "Pause drill"); hy += 22;
            Hint(10, hy, "Q", "Cancel drill");
        }
        else if (_mode == Mode.Screwing)
        {
            Hint(10, hy, "R-Hand", "Carry screw to ring"); hy += 22;
            Hint(10, hy, "Pinch+Twist", "Drive screw in"); hy += 22;
            Hint(10, hy, "LMB hold", "Drive (mouse)"); hy += 22;
            Hint(10, hy, "L-Grab", "Orbit camera"); hy += 22;
            Hint(10, hy, "Both Fists", "Reset"); hy += 22;
            Hint(10, hy, "Q", "Free Look");
        }

        // Hole labels (world→screen)
        foreach (var h in _holes)
        {
            Vector3 sp = _cam.WorldToScreenPoint(h.pos + h.normal * (holeRadius * 2f));
            if (sp.z < 0) continue;
            // Convert screen coords to local GUI coords in embedded mode
            float lx = sp.x - (embeddedMode ? ox : 0f);
            float ly = (embeddedMode ? (Screen.height - sp.y) - oy : sh - sp.y);
            var hs = new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold };
            hs.normal.textColor = h.filled ? new Color(0.5f, 0.5f, 0.55f) : (h == _snap ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.85f, 0f));
            GUI.Label(new UnityEngine.Rect(lx - 20, ly - 20, 40, 20), h.filled ? "✔" : "○", hs);
        }

        if (embeddedMode) GUI.EndGroup();
    }

    // ─────────────────────────────────────────────────────────────
    //  HAND POSITION SMOOTHING & STABILITY
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies exponential smoothing + ring buffer averaging to reduce
    /// Leap Motion jitter. Returns a stable world position.
    /// </summary>
    Vector3 SmoothHandPosition(Vector3 rawWorldPos)
    {
        // Initialise buffer on first call
        if (_handPosBuffer == null || _handPosBuffer.Length != smoothBufferSize)
        {
            _handPosBuffer = new Vector3[smoothBufferSize];
            _handPosBufferIdx = 0;
            _handPosInitialized = false;
        }

        if (!_handPosInitialized)
        {
            for (int i = 0; i < _handPosBuffer.Length; i++)
                _handPosBuffer[i] = rawWorldPos;
            _smoothedHandPos = rawWorldPos;
            _handPosInitialized = true;
            return rawWorldPos;
        }

        // Dead zone: ignore tiny movements
        float moveDist = Vector3.Distance(rawWorldPos, _smoothedHandPos);
        if (moveDist < handDeadzone)
            return _smoothedHandPos;

        // Store in ring buffer
        _handPosBuffer[_handPosBufferIdx] = rawWorldPos;
        _handPosBufferIdx = (_handPosBufferIdx + 1) % _handPosBuffer.Length;

        // Compute weighted average of buffer (more recent = higher weight)
        Vector3 avg = Vector3.zero;
        float totalWeight = 0f;
        for (int i = 0; i < _handPosBuffer.Length; i++)
        {
            // Most recent sample has highest weight
            int age = (_handPosBufferIdx - 1 - i + _handPosBuffer.Length) % _handPosBuffer.Length;
            float weight = 1f / (1f + age * 0.5f);
            avg += _handPosBuffer[i] * weight;
            totalWeight += weight;
        }
        avg /= totalWeight;

        // Exponential smoothing on top
        float alpha = Mathf.Clamp01(handSmoothingFactor);
        _smoothedHandPos = Vector3.Lerp(_smoothedHandPos, avg, alpha);

        return _smoothedHandPos;
    }

    /// <summary>
    /// Creates a ray from the camera through the SMOOTHED hand position.
    /// This eliminates jitter in the raycast target on the bone.
    /// </summary>
    Ray SmoothedHandRay(Hand hand)
    {
        Vector3 screenPos = _cam.WorldToScreenPoint(_smoothedHandPos);
        if (screenPos.z < 0)
            return new Ray(_cam.transform.position, _cam.transform.forward);
        return _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
    }

    /// <summary>
    /// Updates pinch detection with hysteresis to prevent rapid on/off flicker.
    /// Once pinch is detected, it stays "on" until strength drops below (threshold - hysteresis).
    /// </summary>
    void UpdatePinchHysteresis(Hand rightHand)
    {
        if (rightHand == null)
        {
            _pinchStateWithHysteresis = false;
            return;
        }

        float strength = rightHand.PinchStrength;
        if (_pinchStateWithHysteresis)
        {
            // Currently pinching: release only when below threshold - hysteresis
            if (strength < pinchThreshold - pinchHysteresis)
                _pinchStateWithHysteresis = false;
        }
        else
        {
            // Not pinching: engage only when above threshold
            if (strength >= pinchThreshold)
                _pinchStateWithHysteresis = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ULTRALEAP HAND-TRACKING HELPERS
    // ─────────────────────────────────────────────────────────────

    void InitLeapController()
    {
        _leapController = new Controller();
        _handTrackingReady = true;
        Debug.Log("[SpineSim] Ultraleap Controller initialized. Make sure Ultraleap service is running.");
    }

    Hand GetLeapHand(bool isRight)
    {
        if (!_handTrackingReady || _leapController == null) return null;
        Frame frame = _leapController.Frame();
        if (frame == null) return null;
        foreach (var hand in frame.Hands)
        {
            if (isRight && hand.IsRight) return hand;
            if (!isRight && hand.IsLeft) return hand;
        }
        return null;
    }

    bool IsHandPinching(Hand hand)
    {
        if (hand == null) return false;
        return hand.PinchStrength >= pinchThreshold;
    }

    bool IsHandGrabbing(Hand hand)
    {
        if (hand == null) return false;
        return hand.GrabStrength >= grabThreshold;
    }

    bool RightPinchJustStarted(Hand rightHand)
    {
        bool pinching = IsHandPinching(rightHand);
        return pinching && !_prevRightPinch;
    }

    bool RightGrabJustStarted(Hand rightHand)
    {
        bool grabbing = IsHandGrabbing(rightHand);
        return grabbing && !_prevRightGrab;
    }

    void UpdateHandState(Hand rightHand, Hand leftHand)
    {
        _prevRightPinch = IsHandPinching(rightHand);
        _prevLeftPinch = IsHandPinching(leftHand);
        _prevRightGrab = IsHandGrabbing(rightHand);
        _prevLeftGrab = IsHandGrabbing(leftHand);
    }

    /// <summary>
    /// Converts a Leap Motion position (in mm) to Unity world-space coordinates.
    /// </summary>
    Vector3 LeapToWorld(Vector3 leapPos)
    {
        return new Vector3(
            leapPos.x * handPositionScale,
            leapPos.y * handPositionScale,
            leapPos.z * handPositionScale
        ) + handPositionOffset;
    }

    /// <summary>
    /// Creates a ray from the camera through the hand's projected screen position.
    /// This allows the hand to act as a 3D pointer onto the bone surface.
    /// </summary>
    Ray HandRay(Hand hand)
    {
        Vector3 palmWorld = LeapToWorld(hand.PalmPosition);
        Vector3 screenPos = _cam.WorldToScreenPoint(palmWorld);
        // If palm is behind camera, project forward
        if (screenPos.z < 0)
            return new Ray(_cam.transform.position, _cam.transform.forward);
        return _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
    }

    // ─────────────────────────────────────────────────────────────
    //  GHOST HAND VISUALISATION
    // ─────────────────────────────────────────────────────────────

    void EnsureGhostHand()
    {
        if (_ghostHandRoot != null) return;

        _ghostMat = new Material(Shader());
        _ghostMat.color = new Color(0.4f, 0.7f, 1f, 0.3f);
        _ghostMat.SetFloat("_Mode", 3);
        _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _ghostMat.SetInt("_ZWrite", 0);
        _ghostMat.DisableKeyword("_ALPHATEST_ON");
        _ghostMat.EnableKeyword("_ALPHABLEND_ON");
        _ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _ghostMat.renderQueue = 3000;

        _ghostHandRoot = new GameObject("GhostHand");

        _ghostFingerTips = new GameObject[5];
        string[] names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        float[] sizes = { 0.018f, 0.015f, 0.015f, 0.013f, 0.012f };

        for (int i = 0; i < 5; i++)
        {
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = $"Ghost_{names[i]}";
            tip.transform.SetParent(_ghostHandRoot.transform);
            tip.transform.localScale = Vector3.one * sizes[i];
            Destroy(tip.GetComponent<Collider>());
            tip.GetComponent<Renderer>().material = _ghostMat;
            _ghostFingerTips[i] = tip;
        }

        _ghostPalm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _ghostPalm.name = "Ghost_Palm";
        _ghostPalm.transform.SetParent(_ghostHandRoot.transform);
        _ghostPalm.transform.localScale = new Vector3(0.06f, 0.004f, 0.08f);
        Destroy(_ghostPalm.GetComponent<Collider>());
        _ghostPalm.GetComponent<Renderer>().material = _ghostMat;
    }

    void UpdateGhostHand(Hand hand)
    {
        if (!showGhostHand)
        {
            if (_ghostHandRoot != null) _ghostHandRoot.SetActive(false);
            return;
        }

        if (hand == null)
        {
            if (_ghostHandRoot != null) _ghostHandRoot.SetActive(false);
            return;
        }

        EnsureGhostHand();
        _ghostHandRoot.SetActive(true);

        for (int i = 0; i < 5; i++)
        {
            Vector3 tipWorld = LeapToWorld(hand.fingers[i].TipPosition);
            _ghostFingerTips[i].transform.position = tipWorld;
        }

        Vector3 palmWorld = LeapToWorld(hand.PalmPosition);
        _ghostPalm.transform.position = palmWorld;
        Vector3 fwd = hand.Direction;
        Vector3 up = -hand.PalmNormal;
        if (Vector3.Dot(fwd, up) > 0.99f) up = Vector3.up;
        _ghostPalm.transform.rotation = Quaternion.LookRotation(fwd, up);
    }

    void OnDestroy() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _cam == null) return;
        var mouse = Mouse.current; if (mouse == null) return;
        Vector2 mp = mouse.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0));
        Gizmos.color = _hasHit ? Color.green : Color.red;
        Gizmos.DrawRay(ray.origin, ray.direction * 20f);
        if (_hasHit) { Gizmos.color = Color.yellow; Gizmos.DrawSphere(_hit.point, holeRadius * 0.5f); }
        foreach (var h in _holes)
        {
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.8f); Gizmos.DrawWireSphere(h.pos, holeRadius);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f); Gizmos.DrawWireSphere(h.pos, snapRadius);
        }
    }
#endif
}
