// ================================================================
//  SPINE SCREW SIMULATION  v5  —  COMPLETE REWRITE
//  Unity New Input System | 2021.3+
//
//  DELETE DrillSpawner.cs from your project — it conflicts.
//  Only keep THIS file + SpineScrewSetupEditor.cs
//
//  WORKFLOW:
//  1. FREE LOOK  : Mouse moves yellow disc over bone. RMB=orbit.
//  2. Press E    : Drills at pointer. Repeat E for more holes.
//  3. Press S    : Switch to Screw mode.
//  4. SCREW MODE : Mouse carries screw. Near hole = snaps green.
//                  Hold LMB = drives screw in. Auto-picks next.
//  5. All done   : Reset dialog appears.
//  Q anytime     : back to Free Look.
// ================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
    [Tooltip("How deep the hole goes.")]
    public float drillDepth = 0.06f;
    [Tooltip("Seconds to drill one hole.")]
    public float drillDuration = 1.4f;
    [Tooltip("Max holes allowed.")]
    public int maxHoles = 10;

    [Header("── Screw ──")]
    public GameObject screwPrefab;
    public float screwLength = 0.20f;
    public float screwInsertSpeed = 0.15f;   // m/s
    public float screwSpinSpeed = 300f;      // deg/s
    public float snapRadius = 0.15f;

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

    [Header("── Hole Visual Scaling ──")]
    [Tooltip("Multiplier for outer hole ring radius relative to holeRadius.")]
    public float outerRingScale = 6f;
    [Tooltip("Multiplier for middle ring radius relative to holeRadius.")]
    public float midRingScale = 3.5f;
    [Tooltip("Multiplier for center hole disc radius relative to holeRadius.")]
    public float centerHoleScale = 2f;

    [Header("── Pointer Visual Scaling ──")]
    [Tooltip("Multiplier for pointer disc radius relative to holeRadius.")]
    public float pointerDiscScale = 4f;
    [Tooltip("Pointer pulse amplitude. Smaller values feel more precise.")]
    public float pointerPulseAmount = 0.1f;

    // ─────────────────────────────────────────────────────────────
    //  PRIVATE
    // ─────────────────────────────────────────────────────────────

    enum Mode { Free, Drilling, WaitForScrew, Screwing, Done }
    Mode _mode = Mode.Free;

    // Camera
    Camera _cam;
    Transform _pivot;
    float _yaw = 10f, _pitch = 20f, _dist = 2f;

    // Bone
    GameObject _bone;
    Bounds _boneBounds;

    // Pointer
    GameObject _needleGO, _discGO;
    Material _needleMat, _discMat;
    bool _hasHit;
    RaycastHit _hit;

    // Drill
    float _drillT;
    Vector3 _drillPos, _drillNormal;
    ParticleSystem _dustPS;

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

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.spatialBlend = 0f;
        InitCamera();
    }

    void Start()
    {
        InitBone();
        InitPointer();
        InitDust();
        InitStyles();
        ApplyCamera();
    }

    void Update()
    {
        _pulse += Time.deltaTime * 3f;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        OrbitZoom(mouse);
        if (kb.qKey.wasPressedThisFrame) GoFree();

        // Always raycast mouse for pointer/snapping
        _hasHit = BoneRaycast(MouseRay(mouse), out _hit);

        switch (_mode)
        {
            case Mode.Free: DoFree(kb); break;
            case Mode.Drilling: DoDrilling(); break;
            case Mode.WaitForScrew: DoWait(kb); break;
            case Mode.Screwing: DoScrewing(mouse); break;
        }

        DrawPointer();
        PulseRings();
        ApplyCamera();
    }

    void OnGUI() => HUD();

    // ─────────────────────────────────────────────────────────────
    //  CAMERA
    // ─────────────────────────────────────────────────────────────

    void InitCamera()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            var g = new GameObject("MainCamera");
            g.tag = "MainCamera";
            _cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
    }

    void OrbitZoom(Mouse mouse)
    {
        // Scroll = zoom
        float scroll = mouse.scroll.ReadValue().y;
        _dist -= scroll * 0.01f * zoomSpeed * _dist;
        _dist = Mathf.Clamp(_dist, minDist, maxDist);

        // RMB or MMB = orbit
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
        if (_cam == null || _pivot == null) return;
        _cam.transform.position = _pivot.position + Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * _dist;
        _cam.transform.LookAt(_pivot.position);
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
        RaycastHit[] all = Physics.RaycastAll(ray, 200f, boneLayerMask);
        System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in all)
        {
            if (IsOnBone(h.collider?.transform))
            {
                best = h;
                return true;
            }
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

    void DoFree(Keyboard kb)
    {
        if (!kb.eKey.wasPressedThisFrame) return;
        if (!_hasHit)
        {
            Debug.Log("[SpineSim] Aim mouse at bone then press E.");
            return;
        }
        if (_holes.Count >= maxHoles)
        {
            Debug.Log("[SpineSim] Max holes reached. Press S to start screwing.");
            return;
        }
        StartDrill(_hit.point, _hit.normal);
    }

    // ─────────────────────────────────────────────────────────────
    //  DRILLING
    // ─────────────────────────────────────────────────────────────

    void StartDrill(Vector3 pos, Vector3 normal)
    {
        _drillPos = pos;
        _drillNormal = normal;
        _drillT = 0f;
        _mode = Mode.Drilling;
        LoopAudio(drillSFX);
        if (_dustPS != null)
        {
            _dustPS.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-normal));
            _dustPS.Play();
        }
    }

    void DoDrilling()
    {
        _drillT += Time.deltaTime / drillDuration;
        if (_drillT < 1f) return;

        StopAudio();
        OneShot(doneSFX);
        _dustPS?.Stop();

        var h = new HoleData { pos = _drillPos, normal = _drillNormal };
        SpawnHoleMarkers(h);
        _holes.Add(h);
        _mode = Mode.WaitForScrew;
        Debug.Log($"[SpineSim] Hole {_holes.Count} drilled. Press E for more, S to screw.");
    }

    // ─────────────────────────────────────────────────────────────
    //  WAIT (between drills or before screwing)
    // ─────────────────────────────────────────────────────────────

    void DoWait(Keyboard kb)
    {
        if (kb.eKey.wasPressedThisFrame)
        {
            if (_hasHit && _holes.Count < maxHoles)
                StartDrill(_hit.point, _hit.normal);
            else
                StartScrewing();
        }
        if (kb.sKey.wasPressedThisFrame && _holes.Count > 0)
            StartScrewing();
    }

    // ─────────────────────────────────────────────────────────────
    //  HOLE MARKERS
    // ─────────────────────────────────────────────────────────────

    void SpawnHoleMarkers(HoleData h)
    {
        float r = holeRadius;
        float outerSize = r * outerRingScale;
        float midSize = r * midRingScale;
        float centerSize = r * centerHoleScale;

        // Outer gold ring
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "HoleRing";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.position = h.pos + h.normal * 0.002f;
        ring.transform.up = h.normal;
        ring.transform.localScale = new Vector3(outerSize, 0.001f, outerSize);
        var rm = new Material(Shader()) { color = new Color(1f, 0.85f, 0f) };
        ring.GetComponent<Renderer>().material = rm;
        h.outerRing = ring;
        h.ringMat = rm;

        // Middle darker ring
        var mid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mid.name = "HoleMid";
        Destroy(mid.GetComponent<Collider>());
        mid.transform.position = h.pos + h.normal * 0.003f;
        mid.transform.up = h.normal;
        mid.transform.localScale = new Vector3(midSize, 0.0012f, midSize);
        mid.GetComponent<Renderer>().material = new Material(Shader()) { color = new Color(0.55f, 0.4f, 0f) };

        // Centre black hole disc — exact hole size
        var cen = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cen.name = "HoleCentre";
        Destroy(cen.GetComponent<Collider>());
        cen.transform.position = h.pos + h.normal * 0.004f;
        cen.transform.up = h.normal;
        cen.transform.localScale = new Vector3(centerSize, 0.0015f, centerSize);
        if (_holeDarkMat == null) _holeDarkMat = new Material(Shader()) { color = new Color(0.05f, 0.02f, 0.02f) };
        cen.GetComponent<Renderer>().material = _holeDarkMat;
        h.innerDisc = cen;
    }

    void PulseRings()
    {
        float p = 0.82f + 0.18f * Mathf.Sin(_pulse * 2.5f);
        float sp = 0.70f + 0.30f * Mathf.Sin(_pulse * 5.0f);
        float r = holeRadius;
        float baseOuterSize = r * outerRingScale;

        foreach (var h in _holes)
        {
            if (h.ringMat == null) continue;
            if (h.filled)
            {
                h.ringMat.color = new Color(0.4f, 0.4f, 0.45f);
                if (h.outerRing) h.outerRing.transform.localScale = new Vector3(baseOuterSize, 0.001f, baseOuterSize);
                continue;
            }

            bool isSnap = h == _snap;
            Color c = isSnap ? new Color(0.2f, 1f, 0.35f) : new Color(1f, 0.85f, 0f);
            float f = isSnap ? sp : p;
            h.ringMat.color = c * f;
            if (h.outerRing)
            {
                float pulseScale = isSnap
                    ? 1f + 0.2f * Mathf.Sin(_pulse * 5f)
                    : 1f + 0.07f * Mathf.Sin(_pulse * 2.5f);
                float scaledOuterSize = baseOuterSize * pulseScale;
                h.outerRing.transform.localScale = new Vector3(scaledOuterSize, 0.001f, scaledOuterSize);
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
        _snap = null;
        _insertT = 0f;
    }

    void TintScrew(Color c)
    {
        if (_heldMats != null)
            foreach (var m in _heldMats)
                if (m) m.color = c;
    }

    void DoScrewing(Mouse mouse)
    {
        if (_heldScrew == null) return;

        // Find nearest unfilled hole to mouse position on bone
        _snap = null;
        float best = snapRadius;
        if (_hasHit)
        {
            foreach (var h in _holes)
            {
                if (h.filled) continue;
                float d = Vector3.Distance(_hit.point, h.pos);
                if (d < best)
                {
                    best = d;
                    _snap = h;
                }
            }
        }

        if (_snap != null)
        {
            // ── SNAPPED: orient screw perpendicular to bone surface ──
            TintScrew(new Color(0.25f, 1f, 0.4f));

            // Build rotation: local +Y = hole normal (head out, tip into bone)
            Vector3 n = _snap.normal;
            Vector3 right = Vector3.Cross(n, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(n, Vector3.forward);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, n);
            Quaternion targetRot = Quaternion.LookRotation(fwd, n);

            // Position: descends from surface into bone as insertT increases
            Vector3 above = _snap.pos + n * screwLength;          // fully out
            Vector3 inPos = _snap.pos - n * (drillDepth * 0.75f); // fully in
            Vector3 targetPos = Vector3.Lerp(above, inPos, _insertT);

            // Smooth snap
            _heldScrew.transform.position = Vector3.Lerp(_heldScrew.transform.position, targetPos, Time.deltaTime * 20f);
            _heldScrew.transform.rotation = Quaternion.Slerp(_heldScrew.transform.rotation, targetRot, Time.deltaTime * 20f);

            // LMB held = drive in
            if (mouse.leftButton.isPressed)
            {
                // Spin around the insertion axis (hole normal)
                _heldScrew.transform.Rotate(n, screwSpinSpeed * Time.deltaTime, Space.World);
                _insertT += (screwInsertSpeed / screwLength) * Time.deltaTime;
                _insertT = Mathf.Clamp01(_insertT);
                LoopAudio(screwSFX);
                if (_insertT >= 1f) FinishScrew(_snap);
            }
            else StopAudio();
        }
        else
        {
            // ── FREE: float above bone at mouse ──
            StopAudio();
            TintScrew(new Color(0.75f, 0.75f, 0.82f));

            if (_hasHit)
            {
                // Align to bone surface normal at mouse pos (tip into bone)
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
                // Off bone: float in front of camera at mid distance
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
        OneShot(doneSFX);
        _heldScrew.name = "Screw_" + _holes.IndexOf(h);
        _heldScrew = null;
        _snap = null;

        bool allDone = true;
        foreach (var hole in _holes)
            if (!hole.filled)
            {
                allDone = false;
                break;
            }

        if (allDone)
        {
            _mode = Mode.Done;
            Debug.Log("[SpineSim] All screws inserted!");
        }
        else
        {
            SpawnScrew();
        }
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
        if (!show)
        {
            _needleGO.SetActive(false);
            _discGO.SetActive(false);
            return;
        }

        Vector3 pos;
        Vector3 nrm;
        Color col;
        if (_mode == Mode.Drilling)
        {
            pos = _drillPos;
            nrm = _drillNormal;
            col = new Color(1f, 0.3f, 0.05f);
        }
        else if (_mode == Mode.WaitForScrew)
        {
            pos = _drillPos;
            nrm = _drillNormal;
            col = new Color(0.3f, 1f, 0.3f);
        }
        else if (_hasHit)
        {
            pos = _hit.point;
            nrm = _hit.normal;
            col = new Color(1f, 1f, 0.1f);
        }
        else
        {
            _needleGO.SetActive(false);
            _discGO.SetActive(false);
            return;
        }

        float nLen = _boneBounds.extents.magnitude * 0.4f;
        float nRad = holeRadius * 0.4f;
        float pls = 1f + pointerPulseAmount * Mathf.Sin(_pulse * 4f);

        _needleGO.SetActive(true);
        _needleGO.transform.position = pos + nrm * (nLen * 0.5f);
        _needleGO.transform.up = nrm;
        _needleGO.transform.localScale = new Vector3(nRad, nLen * 0.5f, nRad);
        _needleMat.color = col;

        _discGO.SetActive(true);
        _discGO.transform.position = pos + nrm * 0.001f;
        _discGO.transform.up = nrm;
        float dr = holeRadius * pointerDiscScale * pls;
        _discGO.transform.localScale = new Vector3(dr, 0.0005f, dr);
        _discMat.color = new Color(col.r, col.g, col.b, 0.8f);
    }

    // ─────────────────────────────────────────────────────────────
    //  RESET
    // ─────────────────────────────────────────────────────────────

    void GoFree()
    {
        StopAudio();
        _dustPS?.Stop();
        if (_heldScrew)
        {
            Destroy(_heldScrew);
            _heldScrew = null;
        }
        _snap = null;
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
            {
                mf.gameObject.AddComponent<BoxCollider>();
                added++;
                continue;
            }
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
        var g = new GameObject("_Dust");
        g.transform.SetParent(transform);
        _dustPS = g.AddComponent<ParticleSystem>();
        var m = _dustPS.main;
        m.loop = false;
        m.playOnAwake = false;
        m.startLifetime = 0.5f;
        m.startSpeed = 0.12f;
        m.startSize = 0.006f;
        m.maxParticles = 120;
        m.startColor = new Color(0.85f, 0.78f, 0.60f);
        var e = _dustPS.emission;
        e.rateOverTime = 50;
        var sh = _dustPS.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 20f;
        sh.radius = 0.004f;
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
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().material = _boneMat;
    }

    // ─────────────────────────────────────────────────────────────
    //  SCREW BUILDER
    // ─────────────────────────────────────────────────────────────

    GameObject BuildScrew()
    {
        float r = holeRadius;         // shaft = hole radius (exact fit)
        float rt = r * 1.42f;         // thread outer (grips wall)
        float rh = r * 2.4f;          // head (stops flush)
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
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
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
        _audio.loop = true;
        _audio.clip = c;
        _audio.Play();
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
        _white = new Texture2D(1, 1);
        _white.SetPixel(0, 0, Color.white);
        _white.Apply();
        _sTitle = Sty(18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        _sBody = Sty(14, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
        _sHint = Sty(13, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f));
        _sBig = Sty(26, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.3f, 1f, 0.5f));
    }
    GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c)
    {
        var g = new GUIStyle { fontSize = sz, fontStyle = fs, alignment = a };
        g.normal.textColor = c;
        return g;
    }

    void Rect(UnityEngine.Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, _white);
        GUI.color = Color.white;
    }

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
        float sw = Screen.width, sh = Screen.height;

        // Top bar
        Rect(new UnityEngine.Rect(0, 0, sw, 46), new Color(0, 0, 0, 0.62f));

        string label;
        Color lc;
        int filled = 0;
        foreach (var h in _holes) if (h.filled) filled++;

        switch (_mode)
        {
            case Mode.Drilling: label = "⬤ DRILLING"; lc = new Color(1f, 0.3f, 0.05f); break;
            case Mode.WaitForScrew: label = "✔ HOLE READY — E=more  S=start screwing"; lc = new Color(0.3f, 1f, 0.3f); break;
            case Mode.Screwing: label = "⬤ SCREWING — move to gold ring, hold LMB"; lc = new Color(0.2f, 0.85f, 1f); break;
            case Mode.Done: label = "✔ ALL DONE"; lc = new Color(0.3f, 1f, 0.5f); break;
            default: label = "◈ FREE LOOK — move mouse, E to drill"; lc = new Color(0.9f, 0.9f, 0.9f); break;
        }
        var ls = new GUIStyle(_sTitle);
        ls.normal.textColor = lc;
        GUI.Label(new UnityEngine.Rect(14, 12, sw - 200, 26), label, ls);

        var rs = new GUIStyle(_sHint) { alignment = TextAnchor.MiddleRight };
        GUI.Label(new UnityEngine.Rect(sw - 240, 12, 228, 26), $"Holes: {_holes.Count}/{maxHoles}  Screws: {filled}/{_holes.Count}", rs);

        // Drill progress bar
        if (_mode == Mode.Drilling)
        {
            float bw = 280, bh = 24, bx = sw / 2f - 140, by = sh * 0.62f;
            Rect(new UnityEngine.Rect(bx - 2, by - 2, bw + 4, bh + 4), new Color(0, 0, 0, 0.8f));
            Rect(new UnityEngine.Rect(bx, by, bw * _drillT, bh), new Color(1f, 0.4f, 0.1f));
            Rect(new UnityEngine.Rect(bx + bw * _drillT, by, bw * (1f - _drillT), bh), new Color(0.15f, 0.08f, 0.04f, 0.5f));
            var ps = new GUIStyle(_sBody) { fontSize = 13 };
            GUI.Label(new UnityEngine.Rect(bx, by, bw, bh), $"Drilling... {Mathf.RoundToInt(_drillT * 100)}%", ps);
        }

        // Snap indicator
        if (_mode == Mode.Screwing)
        {
            var ss = new GUIStyle(_sBody) { fontSize = 14 };
            if (_snap != null)
            {
                ss.normal.textColor = new Color(0.25f, 1f, 0.4f);
                Rect(new UnityEngine.Rect(sw / 2f - 180, sh * 0.62f, 360, 30), new Color(0, 0, 0, 0.65f));
                GUI.Label(new UnityEngine.Rect(sw / 2f - 180, sh * 0.62f, 360, 30), "✔ ALIGNED — Hold LMB to drive screw", ss);
            }
            else
            {
                ss.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
                GUI.Label(new UnityEngine.Rect(sw / 2f - 160, sh * 0.62f, 320, 26), "Move mouse over a gold ring to snap", ss);
            }
        }

        // Wait prompt
        if (_mode == Mode.WaitForScrew)
        {
            Rect(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f, 400, 50), new Color(0, 0.08f, 0, 0.82f));
            var a1 = new GUIStyle(_sBody) { fontSize = 15 };
            a1.normal.textColor = new Color(0.4f, 1f, 0.4f);
            GUI.Label(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f + 2, 400, 22), "Hole drilled!", a1);
            var a2 = new GUIStyle(_sBody) { fontSize = 13 };
            a2.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Label(new UnityEngine.Rect(sw / 2f - 200, sh * 0.61f + 24, 400, 22), "E = drill more   |   S = start screwing", a2);
        }

        // Done overlay
        if (_mode == Mode.Done)
        {
            Rect(new UnityEngine.Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.5f));
            float pw = 460, ph = 190, px = sw / 2f - 230, py = sh / 2f - 95;
            Rect(new UnityEngine.Rect(px, py, pw, ph), new Color(0.04f, 0.1f, 0.06f, 0.97f));
            Rect(new UnityEngine.Rect(px, py, pw, 4), new Color(0.3f, 1f, 0.5f));
            GUI.Label(new UnityEngine.Rect(px, py + 16, pw, 38), "SIMULATION COMPLETE", _sBig);
            var sb = new GUIStyle(_sBody);
            sb.normal.textColor = new Color(0.75f, 0.95f, 0.8f);
            GUI.Label(new UnityEngine.Rect(px, py + 62, pw, 24), $"All {_holes.Count} screws inserted.", sb);
            if (GUI.Button(new UnityEngine.Rect(px + pw / 2f - 90, py + 128, 180, 42), "↺  Reset Simulation"))
                ResetAll();
            return;
        }

        // Hint strip
        Rect(new UnityEngine.Rect(0, sh - 138, 320, 138), new Color(0, 0, 0, 0.5f));
        float hy = sh - 132f;
        if (_mode == Mode.Free || _mode == Mode.WaitForScrew)
        {
            Hint(10, hy, "Mouse", "Move over bone"); hy += 22;
            Hint(10, hy, "E", "Drill at pointer"); hy += 22;
            Hint(10, hy, "S", "Start screwing holes"); hy += 22;
            Hint(10, hy, "RMB/MMB", "Orbit camera"); hy += 22;
            Hint(10, hy, "Scroll", "Zoom"); hy += 22;
            Hint(10, hy, "Q", "Back to Free Look");
        }
        else if (_mode == Mode.Drilling)
        {
            Hint(10, hy, "—", "Auto-drilling..."); hy += 22;
            Hint(10, hy, "Q", "Cancel");
        }
        else if (_mode == Mode.Screwing)
        {
            Hint(10, hy, "Mouse", "Carry screw to gold ring"); hy += 22;
            Hint(10, hy, "LMB hold", "Drive screw in"); hy += 22;
            Hint(10, hy, "RMB/MMB", "Orbit camera"); hy += 22;
            Hint(10, hy, "Q", "Free Look");
        }

        // Hole labels (world→screen)
        foreach (var h in _holes)
        {
            Vector3 sp = _cam.WorldToScreenPoint(h.pos + h.normal * (holeRadius * 2f));
            if (sp.z < 0) continue;
            var hs = new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold };
            hs.normal.textColor = h.filled ? new Color(0.5f, 0.5f, 0.55f) : (h == _snap ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.85f, 0f));
            GUI.Label(new UnityEngine.Rect(sp.x - 20, sh - sp.y - 20, 40, 20), h.filled ? "✔" : "○", hs);
        }
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _cam == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 mp = mouse.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0));
        Gizmos.color = _hasHit ? Color.green : Color.red;
        Gizmos.DrawRay(ray.origin, ray.direction * 20f);
        if (_hasHit)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_hit.point, holeRadius * 0.5f);
        }
        foreach (var h in _holes)
        {
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.8f);
            Gizmos.DrawWireSphere(h.pos, holeRadius);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(h.pos, snapRadius);
        }
    }
#endif
}
analsyse it and remeber it i will tell you to what to do
