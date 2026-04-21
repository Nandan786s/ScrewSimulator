////using UnityEngine;

////[RequireComponent(typeof(SpineScrewSimulation))]
////[RequireComponent(typeof(LeapPhysicalInputManager))]
////public class SimulationUI : MonoBehaviour
////{
////    [Header("-- Layout --")]
////    [Range(0.4f, 0.7f)] public float progressY = 0.55f;
////    [Range(0.5f, 2f)] public float uiScale = 0.75f; // ?? smaller UI

////    [Header("-- Smart Drill UI --")]
////    public Transform drillFocusTransform;
////    public Transform boneFocusTransform;
////    [Range(0.05f, 0.25f)] public float sidePaddingNormalized = 0.08f;
////    [Range(0.05f, 0.35f)] public float centerDeadZone = 0.14f;
////    [Range(3f, 18f)] public float drillUiLerpSpeed = 10f;

////    [Header("-- Colors --")]
////    public Color accentDrill = new Color(1f, 0.45f, 0.1f);
////    public Color accentPlace = new Color(0.25f, 0.95f, 0.45f);
////    public Color accentDrive = new Color(0.25f, 0.75f, 1f);
////    public Color accentDone = new Color(0.3f, 1f, 0.5f);
////    public Color barBgColor = new Color(0.08f, 0.06f, 0.04f, 0.7f);
////    public Color panelBg = new Color(0.02f, 0.03f, 0.06f, 0.82f);
////    public Color resistLow = new Color(0.15f, 0.75f, 0.25f);
////    public Color resistHigh = new Color(1f, 0.18f, 0.1f);

////    SpineScrewSimulation _sim;
////    LeapPhysicalInputManager _input;
////    Texture2D _w;
////    float _t;
////    float _drillPanelCenterX = -1f;
////    LeapPhysicalInputManager.Phase _lastPhase;
////    float _phaseBannerUntil;
////    string _phaseBannerText = string.Empty;
////    Color _phaseBannerColor = Color.white;

////    void Start()
////    {
////        _sim = GetComponent<SpineScrewSimulation>();
////        _input = GetComponent<LeapPhysicalInputManager>();

////        _w = new Texture2D(1, 1);
////        _w.SetPixel(0, 0, Color.white);
////        _w.Apply();
////    }

////    void Update()
////    {
////        _t += Time.deltaTime;
////        if (_input == null) return;

////        if (_input.currentPhase != _lastPhase)
////        {
////            _lastPhase = _input.currentPhase;
////            switch (_lastPhase)
////            {
////                case LeapPhysicalInputManager.Phase.Positioning:
////                    SetPhaseBanner("POSITIONING MODE", accentDrill);
////                    break;
////                case LeapPhysicalInputManager.Phase.Drilling:
////                    SetPhaseBanner("DRILLING MODE", accentDrill);
////                    break;
////                case LeapPhysicalInputManager.Phase.Placing:
////                    SetPhaseBanner("SCREW PLACEMENT MODE", accentPlace);
////                    break;
////                case LeapPhysicalInputManager.Phase.Driving:
////                    SetPhaseBanner("SCREWDRIVER MODE", accentDrive);
////                    break;
////                case LeapPhysicalInputManager.Phase.Done:
////                    SetPhaseBanner("SIMULATION COMPLETE", accentDone);
////                    break;
////            }
////        }
////    }

////    int S(float v) => Mathf.RoundToInt(v * uiScale);

////    void R(Rect r, Color c)
////    {
////        GUI.color = c;
////        GUI.DrawTexture(r, _w);
////        GUI.color = Color.white;
////    }

////    void Panel(Rect r) => R(r, panelBg);

////    GUIStyle St(int size, FontStyle style, TextAnchor align, Color color)
////    {
////        var s = new GUIStyle(GUI.skin.label);
////        s.fontSize = S(size);
////        s.fontStyle = style;
////        s.alignment = align;
////        s.normal.textColor = color;
////        return s;
////    }

////    void Bar(Rect r, float f, Color c1, Color c2, string lbl, string val)
////    {
////        f = Mathf.Clamp01(f);
////        R(r, barBgColor);
////        R(new Rect(r.x, r.y, r.width * f, r.height), Color.Lerp(c1, c2, f));

////        GUI.Label(r, lbl, St(11, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
////        GUI.Label(r, val, St(11, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));
////    }

////    void OnGUI()
////    {
////        float sw = Screen.width, sh = Screen.height;

////        DrawContextHUD(sw, sh);

////        switch (_input.currentPhase)
////        {
////            case LeapPhysicalInputManager.Phase.Positioning:
////                DrawPositioningUI(sw, sh);
////                break;
////            case LeapPhysicalInputManager.Phase.Drilling:
////                DrawDrillUI(sw, sh);
////                break;
////            case LeapPhysicalInputManager.Phase.Placing:
////                DrawPlaceUI(sw, sh);
////                break;
////            case LeapPhysicalInputManager.Phase.Driving:
////                DrawDriveUI(sw, sh);
////                break;
////            case LeapPhysicalInputManager.Phase.Done:
////                DrawDoneUI(sw, sh);
////                break;
////        }

////        DrawPhaseBanner(sw, sh);
////    }

////    void DrawContextHUD(float sw, float sh)
////    {
////        float pw = S(360);
////        float ph = S(112);
////        float px = S(18);
////        float py = S(18);

////        Panel(new Rect(px, py, pw, ph));

////        Color phaseColor = PhaseColor(_input.currentPhase);
////        GUI.Label(new Rect(px + S(14), py + S(10), pw - S(28), S(18)),
////            PhaseTitle(_input.currentPhase),
////            St(13, FontStyle.Bold, TextAnchor.MiddleLeft, phaseColor));

////        GUI.Label(new Rect(px + S(14), py + S(32), pw - S(28), S(16)),
////            HandStatusText(),
////            St(10, FontStyle.Bold, TextAnchor.MiddleLeft, TrackingColor()));

////        GUI.Label(new Rect(px + S(14), py + S(52), pw - S(28), S(18)),
////            PrimaryInstruction(),
////            St(10, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));

////        GUI.Label(new Rect(px + S(14), py + S(72), pw - S(28), S(16)),
////            SecondaryInstruction(),
////            St(9, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.82f, 0.9f, 1f)));

////        string counts = $"Holes {_sim.GetHoleCount()}/{_sim.maxHoles}   Placed {_sim.GetPlacedCount()}   Driven {_sim.GetFilledCount()}";
////        GUI.Label(new Rect(px + S(14), py + S(90), pw - S(28), S(16)),
////            counts,
////            St(9, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.8f, 0.95f, 0.85f)));
////    }

////    Color PhaseColor(LeapPhysicalInputManager.Phase phase)
////    {
////        switch (phase)
////        {
////            case LeapPhysicalInputManager.Phase.Positioning: return accentDrill;
////            case LeapPhysicalInputManager.Phase.Drilling: return accentDrill;
////            case LeapPhysicalInputManager.Phase.Placing: return accentPlace;
////            case LeapPhysicalInputManager.Phase.Driving: return accentDrive;
////            case LeapPhysicalInputManager.Phase.Done: return accentDone;
////            default: return Color.white;
////        }
////    }

////    string PhaseTitle(LeapPhysicalInputManager.Phase phase)
////    {
////        switch (phase)
////        {
////            case LeapPhysicalInputManager.Phase.Positioning: return "Step 1: Position Drill";
////            case LeapPhysicalInputManager.Phase.Drilling: return "Step 2: Drill Pilot Hole";
////            case LeapPhysicalInputManager.Phase.Placing: return "Step 3: Place Screw";
////            case LeapPhysicalInputManager.Phase.Driving: return "Step 4: Drive Screw";
////            case LeapPhysicalInputManager.Phase.Done: return "Simulation Complete";
////            default: return "Simulation";
////        }
////    }

////    string HandStatusText()
////    {
////        if (_input.HasRightHand && _input.HasLeftHand) return "Tracking: both hands detected";
////        if (_input.HasRightHand) return "Tracking: right hand detected";
////        if (_input.HasLeftHand) return "Tracking: left hand detected";
////        return "Tracking lost: show at least one hand to continue";
////    }

////    Color TrackingColor()
////    {
////        return (_input.HasRightHand || _input.HasLeftHand)
////            ? new Color(0.55f, 0.95f, 0.75f)
////            : new Color(1f, 0.45f, 0.45f);
////    }

////    string PrimaryInstruction()
////    {
////        switch (_input.currentPhase)
////        {
////            case LeapPhysicalInputManager.Phase.Positioning:
////                return "Touch the skull with the drill tip, then pinch to enter drilling mode.";
////            case LeapPhysicalInputManager.Phase.Drilling:
////                return "Keep the drill aligned and maintain pinch to deepen the red pilot hole.";
////            case LeapPhysicalInputManager.Phase.Placing:
////                return "Pick up the screw and move it near a red hole. It will auto-seat when close.";
////            case LeapPhysicalInputManager.Phase.Driving:
////                return "Bring the screwdriver tip to the screw head. It will auto-align before locking on.";
////            case LeapPhysicalInputManager.Phase.Done:
////                return "Review the final construct, then restart or exit the simulation.";
////            default:
////                return string.Empty;
////        }
////    }

////    string SecondaryInstruction()
////    {
////        switch (_input.currentPhase)
////        {
////            case LeapPhysicalInputManager.Phase.Positioning:
////                return "Two-hand pinch zoom adjusts framing before drilling starts.";
////            case LeapPhysicalInputManager.Phase.Drilling:
////                return "Hold both fists briefly when you are done drilling to move to screw placement.";
////            case LeapPhysicalInputManager.Phase.Placing:
////                return "Dropped tools are recovered automatically near your hand for faster continuation.";
////            case LeapPhysicalInputManager.Phase.Driving:
////                return _input.DriverAttached
////                    ? "Pinch and twist with the same hand to drive the screw to depth."
////                    : "Move the tool close to the screw head to attach.";
////            case LeapPhysicalInputManager.Phase.Done:
////                return "Camera zoom resets for a final review of all placed and driven screws.";
////            default:
////                return string.Empty;
////        }
////    }

////    void SetPhaseBanner(string text, Color color)
////    {
////        _phaseBannerText = text;
////        _phaseBannerColor = color;
////        _phaseBannerUntil = Time.time + 1.5f;
////    }

////    void DrawPhaseBanner(float sw, float sh)
////    {
////        if (Time.time > _phaseBannerUntil || string.IsNullOrEmpty(_phaseBannerText)) return;

////        float remain = Mathf.Clamp01((_phaseBannerUntil - Time.time) / 1.5f);
////        Color c = _phaseBannerColor;
////        c.a = Mathf.Lerp(0f, 0.95f, remain);

////        float w = S(420);
////        float h = S(34);
////        float x = (sw - w) * 0.5f;
////        float y = sh * 0.08f;

////        R(new Rect(x, y, w, h), new Color(0.03f, 0.03f, 0.05f, 0.75f * remain));
////        GUI.Label(new Rect(x, y, w, h), _phaseBannerText, St(13, FontStyle.Bold, TextAnchor.MiddleCenter, c));
////    }

////    // ================= SMART POSITION =================
////    Vector2 GetFocusViewportPoint()
////    {
////        Transform t = drillFocusTransform != null ? drillFocusTransform :
////            (_input.drillTipPoint != null ? _input.drillTipPoint : boneFocusTransform);

////        if (!t || !Camera.main) return new Vector2(0.5f, 0.5f);

////        Vector3 vp = Camera.main.WorldToViewportPoint(t.position);
////        return new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
////    }

////    bool ShouldLeft(Vector2 vp)
////    {
////        if (vp.x > 0.6f) return true;
////        if (vp.x < 0.4f) return false;
////        return true;
////    }

////    float SmoothX(float target)
////    {
////        if (_drillPanelCenterX < 0) _drillPanelCenterX = target;
////        _drillPanelCenterX = Mathf.Lerp(_drillPanelCenterX, target, 0.1f);
////        return _drillPanelCenterX;
////    }

////    // ================= POSITIONING =================
////    void DrawPositioningUI(float sw, float sh)
////    {
////        float pw = S(480);
////        float ph = S(120);
////        float px = (sw - pw) * 0.5f;
////        float py = sh * 0.74f;

////        Panel(new Rect(px, py, pw, ph));

////        GUI.Label(new Rect(px, py + S(6), pw, S(24)),
////            "POSITION DRILL",
////            St(16, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrill));

////        GUI.Label(new Rect(px + S(16), py + S(35), pw - S(32), S(22)),
////            "Two-hand pinch: zoom in/out to set camera framing",
////            St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));

////        GUI.Label(new Rect(px + S(16), py + S(56), pw - S(32), S(22)),
////            "Touch bone with drill tip, then pinch to start drilling mode",
////            St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));

////        GUI.Label(new Rect(px + S(16), py + S(80), pw - S(32), S(20)),
////            "Tip: cyan ring indicates drill-ready pinch strength",
////            St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.7f, 0.95f, 1f)));
////    }

////    // ================= DRILL =================
////    void DrawDrillUI(float sw, float sh)
////    {
////        float bw = S(320), pw = bw + S(30);

////        Vector2 vp = GetFocusViewportPoint();
////        bool left = ShouldLeft(vp);

////        float targetX = left ? sw * 0.15f : sw * 0.85f;
////        float px = SmoothX(targetX) - pw * 0.5f;
////        float py = sh * 0.7f;

////        Panel(new Rect(px, py, pw, S(110)));

////        float pulse = 0.9f + 0.1f * Mathf.Sin(_t * 2);

////        GUI.Label(new Rect(px, py + S(5), pw, S(20)),
////            "DRILLING",
////            St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrill * pulse));

////        float df = _sim.GetDrillDepthFraction();
////        Bar(new Rect(px + S(10), py + S(30), bw, S(18)),
////            df, accentDrill, Color.red, "DEPTH", $"{Mathf.RoundToInt(df * 100)}%");

////        float rf = _sim.GetResistanceFraction();
////        Bar(new Rect(px + S(10), py + S(55), bw, S(12)),
////            rf, resistLow, resistHigh, "RESIST", $"{Mathf.RoundToInt(rf * 100)}%");
////    }

////    // ================= PLACE =================
////    void DrawPlaceUI(float sw, float sh)
////    {
////        float pw = S(420);
////        float px = (sw - pw) * 0.5f;
////        float py = sh * 0.82f;

////        Panel(new Rect(px, py, pw, S(90)));

////        GUI.Label(new Rect(px, py + S(6), pw, S(20)),
////            "PLACE SCREW",
////            St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentPlace));

////        GUI.Label(new Rect(px + S(16), py + S(34), pw - S(32), S(20)),
////            "Move the screw near a highlighted hole and it will magnetically seat",
////            St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));

////        GUI.Label(new Rect(px + S(16), py + S(56), pw - S(32), S(20)),
////            "If the screw is dropped, it respawns near your hand for quick recovery",
////            St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.9f, 1f, 0.9f)));
////    }

////    // ================= DRIVE =================
////    void DrawDriveUI(float sw, float sh)
////    {
////        float bw = S(320), pw = bw + S(30);

////        float px = sw - pw - S(20);
////        float py = sh * 0.7f;

////        Panel(new Rect(px, py, pw, S(140)));

////        GUI.Label(new Rect(px, py + S(5), pw, S(20)),
////            "DRIVING",
////            St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrive));

////        float progress = _sim.GetDriveProgress();

////        Bar(new Rect(px + S(10), py + S(30), bw, S(18)),
////            progress, accentDrive, accentDone,
////            "INSERT", $"{Mathf.RoundToInt(progress * 100)}%");

////        float cx = px + S(55);
////        float cy = py + S(92);
////        float radius = S(22);
////        R(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), new Color(0.08f, 0.2f, 0.35f, 0.5f));
////        R(new Rect(cx - radius, cy - radius, radius * 2 * progress, radius * 2), accentDrive);
////        GUI.Label(new Rect(cx - radius, cy - S(9), radius * 2, S(18)),
////            $"{Mathf.RoundToInt(progress * 100)}%",
////            St(10, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));

////        GUI.Label(new Rect(px + S(98), py + S(78), pw - S(108), S(20)),
////            _input.DriverAttached ? "Pinch + twist with the same hand" : "Move screwdriver tip close to auto-attach",
////            St(10, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));

////        GUI.Label(new Rect(px + S(98), py + S(98), pw - S(108), S(20)),
////            _input.DriverAttached ? "Stay aligned to keep insertion smooth" : "Magnetic assist will align the tool near the head",
////            St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.8f, 0.95f, 1f)));
////    }
////    void DrawDoneUI(float sw, float sh)
////    {
////        float pw = S(460);
////        float ph = S(210);
////        float px = (sw - pw) * 0.5f;
////        float py = (sh - ph) * 0.5f;

////        Panel(new Rect(px, py, pw, ph));

////        GUI.Label(new Rect(px, py + S(12), pw, S(28)),
////            "SCREW SIMULATION COMPLETE",
////            St(16, FontStyle.Bold, TextAnchor.MiddleCenter, accentDone));

////        GUI.Label(new Rect(px, py + S(52), pw, S(22)),
////            $"Holes Drilled: {_sim.GetHoleCount()}   Placed: {_sim.GetPlacedCount()}   Driven: {_sim.GetFilledCount()}",
////            St(11, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white));

////        GUI.Label(new Rect(px + S(28), py + S(82), pw - S(56), S(40)),
////            "Camera has automatically zoomed out for final review.",
////            St(10, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(0.9f, 1f, 0.9f)));

////        Rect restartRect = new Rect(px + S(80), py + S(142), S(130), S(42));
////        Rect exitRect = new Rect(px + pw - S(210), py + S(142), S(130), S(42));

////        GUI.color = new Color(0.2f, 0.65f, 0.35f);
////        if (GUI.Button(restartRect, "Restart"))
////            _input.RestartSimulation();

////        GUI.color = new Color(0.7f, 0.2f, 0.2f);
////        if (GUI.Button(exitRect, "Exit"))
////            _input.ExitSimulation();

////        GUI.color = Color.white;
////    }
////}




using UnityEngine;



[RequireComponent(typeof(SpineScrewSimulation))]
[RequireComponent(typeof(LeapPhysicalInputManager))]
public class SimulationUI : MonoBehaviour
{
    [Header("-- Layout --")]
    [Range(0.4f, 0.7f)] public float progressY = 0.55f;
    [Range(0.5f, 2f)] public float uiScale = 0.75f;



    [Header("-- Smart Drill UI --")]
    public Transform drillFocusTransform;
    public Transform boneFocusTransform;
    [Range(0.05f, 0.25f)] public float sidePaddingNormalized = 0.08f;
    [Range(0.05f, 0.35f)] public float centerDeadZone = 0.14f;
    [Range(3f, 18f)] public float drillUiLerpSpeed = 10f;



    [Header("-- Colors --")]
    public Color accentDrill = new Color(1f, 0.45f, 0.1f);
    public Color accentPlace = new Color(0.25f, 0.95f, 0.45f);
    public Color accentDrive = new Color(0.25f, 0.75f, 1f);
    public Color accentDone = new Color(0.3f, 1f, 0.5f);
    public Color barBgColor = new Color(0.08f, 0.06f, 0.04f, 0.7f);
    public Color panelBg = new Color(0.02f, 0.03f, 0.06f, 0.82f);
    public Color resistLow = new Color(0.15f, 0.75f, 0.25f);
    public Color resistHigh = new Color(1f, 0.18f, 0.1f);



    SpineScrewSimulation _sim;
    LeapPhysicalInputManager _input;
    Texture2D _w;
    float _t;
    float _drillPanelCenterX = -1f;
    LeapPhysicalInputManager.Phase _lastPhase;
    float _phaseBannerUntil;
    string _phaseBannerText = string.Empty;
    Color _phaseBannerColor = Color.white;



    void Start()
    {
        _sim = GetComponent<SpineScrewSimulation>();
        _input = GetComponent<LeapPhysicalInputManager>();



        _w = new Texture2D(1, 1);
        _w.SetPixel(0, 0, Color.white);
        _w.Apply();
    }



    void Update()
    {
        _t += Time.deltaTime;
        if (_input == null) return;



        if (_input.currentPhase != _lastPhase)
        {
            _lastPhase = _input.currentPhase;
            switch (_lastPhase)
            {
                case LeapPhysicalInputManager.Phase.Positioning: SetPhaseBanner("POSITIONING MODE", accentDrill); break;
                case LeapPhysicalInputManager.Phase.Drilling: SetPhaseBanner("DRILLING MODE", accentDrill); break;
                case LeapPhysicalInputManager.Phase.Placing: SetPhaseBanner("SCREW PLACEMENT MODE", accentPlace); break;
                case LeapPhysicalInputManager.Phase.Driving: SetPhaseBanner("SCREWDRIVER MODE", accentDrive); break;
                case LeapPhysicalInputManager.Phase.Done: SetPhaseBanner("SIMULATION COMPLETE", accentDone); break;
            }
        }
    }



    int S(float v) => Mathf.RoundToInt(v * uiScale);



    void R(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, _w);
        GUI.color = Color.white;
    }



    void Panel(Rect r) => R(r, panelBg);



    GUIStyle St(int size, FontStyle style, TextAnchor align, Color color)
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize = S(size);
        s.fontStyle = style;
        s.alignment = align;
        s.normal.textColor = color;
        return s;
    }



    void Bar(Rect r, float f, Color c1, Color c2, string lbl, string val)
    {
        f = Mathf.Clamp01(f);
        R(r, barBgColor);
        R(new Rect(r.x, r.y, r.width * f, r.height), Color.Lerp(c1, c2, f));



        GUI.Label(r, lbl, St(11, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(r, val, St(11, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));
    }



    void OnGUI()
    {
        float sw = Screen.width, sh = Screen.height;



        DrawContextHUD(sw, sh);



        switch (_input.currentPhase)
        {
            case LeapPhysicalInputManager.Phase.Positioning: DrawPositioningUI(sw, sh); break;
            case LeapPhysicalInputManager.Phase.Drilling: DrawDrillUI(sw, sh); break;
            case LeapPhysicalInputManager.Phase.Placing: DrawPlaceUI(sw, sh); break;
            case LeapPhysicalInputManager.Phase.Driving: DrawDriveUI(sw, sh); break;
            case LeapPhysicalInputManager.Phase.Done: DrawDoneUI(sw, sh); break;
        }



        DrawPhaseBanner(sw, sh);
    }



    void DrawContextHUD(float sw, float sh)
    {
        float pw = S(360);
        float ph = S(112);
        float px = S(18);
        float py = S(18);



        Panel(new Rect(px, py, pw, ph));



        Color phaseColor = PhaseColor(_input.currentPhase);
        GUI.Label(new Rect(px + S(14), py + S(10), pw - S(28), S(18)), PhaseTitle(_input.currentPhase), St(13, FontStyle.Bold, TextAnchor.MiddleLeft, phaseColor));
        GUI.Label(new Rect(px + S(14), py + S(32), pw - S(28), S(16)), HandStatusText(), St(10, FontStyle.Bold, TextAnchor.MiddleLeft, TrackingColor()));
        GUI.Label(new Rect(px + S(14), py + S(52), pw - S(28), S(18)), PrimaryInstruction(), St(10, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(new Rect(px + S(14), py + S(72), pw - S(28), S(16)), SecondaryInstruction(), St(9, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.82f, 0.9f, 1f)));



        string counts = $"Holes {_sim.GetHoleCount()}/{_sim.maxHoles}   Placed {_sim.GetPlacedCount()}   Driven {_sim.GetFilledCount()}";
        GUI.Label(new Rect(px + S(14), py + S(90), pw - S(28), S(16)), counts, St(9, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.8f, 0.95f, 0.85f)));
    }



    Color PhaseColor(LeapPhysicalInputManager.Phase phase)
    {
        switch (phase)
        {
            case LeapPhysicalInputManager.Phase.Positioning:
            case LeapPhysicalInputManager.Phase.Drilling: return accentDrill;
            case LeapPhysicalInputManager.Phase.Placing: return accentPlace;
            case LeapPhysicalInputManager.Phase.Driving: return accentDrive;
            case LeapPhysicalInputManager.Phase.Done: return accentDone;
            default: return Color.white;
        }
    }



    string PhaseTitle(LeapPhysicalInputManager.Phase phase)
    {
        switch (phase)
        {
            case LeapPhysicalInputManager.Phase.Positioning: return "Step 1: Position Drill";
            case LeapPhysicalInputManager.Phase.Drilling: return "Step 2: Drill Pilot Hole";
            case LeapPhysicalInputManager.Phase.Placing: return "Step 3: Place Screw";
            case LeapPhysicalInputManager.Phase.Driving: return "Step 4: Drive Screw";
            case LeapPhysicalInputManager.Phase.Done: return "Simulation Complete";
            default: return "Simulation";
        }
    }



    string HandStatusText()
    {
        if (_input.HasRightHand && _input.HasLeftHand) return "Tracking: both hands detected";
        if (_input.HasRightHand) return "Tracking: right hand detected";
        if (_input.HasLeftHand) return "Tracking: left hand detected";
        return "Tracking lost: show at least one hand to continue";
    }



    Color TrackingColor() => (_input.HasRightHand || _input.HasLeftHand) ? new Color(0.55f, 0.95f, 0.75f) : new Color(1f, 0.45f, 0.45f);



    string PrimaryInstruction()
    {
        switch (_input.currentPhase)
        {
            case LeapPhysicalInputManager.Phase.Positioning: return "Touch the skull with the drill tip, then pinch to enter drilling mode.";
            case LeapPhysicalInputManager.Phase.Drilling: return "Keep the drill aligned and maintain pinch to deepen the red pilot hole.";
            case LeapPhysicalInputManager.Phase.Placing: return "Pick up the screw and move it near a red hole. It will auto-seat when close.";
            case LeapPhysicalInputManager.Phase.Driving: return "Bring the screwdriver tip to the screw head. It will auto-align before locking on.";
            case LeapPhysicalInputManager.Phase.Done: return "Review the final construct, then restart or exit the simulation.";
            default: return string.Empty;
        }
    }



    string SecondaryInstruction()
    {
        switch (_input.currentPhase)
        {
            case LeapPhysicalInputManager.Phase.Positioning: return "Two-hand pinch zoom adjusts framing before drilling starts.";
            case LeapPhysicalInputManager.Phase.Drilling: return "Hold both fists briefly when you are done drilling to move to screw placement.";
            case LeapPhysicalInputManager.Phase.Placing: return "Dropped tools are recovered automatically near your hand for faster continuation.";
            case LeapPhysicalInputManager.Phase.Driving: return _input.DriverAttached ? "Pinch and twist with the same hand to drive the screw to depth." : "Move the tool close to the screw head to attach.";
            case LeapPhysicalInputManager.Phase.Done: return "Camera zoom resets for a final review of all placed and driven screws.";
            default: return string.Empty;
        }
    }



    void SetPhaseBanner(string text, Color color)
    {
        _phaseBannerText = text;
        _phaseBannerColor = color;
        _phaseBannerUntil = Time.time + 1.5f;
    }



    void DrawPhaseBanner(float sw, float sh)
    {
        if (Time.time > _phaseBannerUntil || string.IsNullOrEmpty(_phaseBannerText)) return;
        float remain = Mathf.Clamp01((_phaseBannerUntil - Time.time) / 1.5f);
        Color c = _phaseBannerColor;
        c.a = Mathf.Lerp(0f, 0.95f, remain);



        float w = S(420);
        float h = S(34);
        float x = (sw - w) * 0.5f;
        float y = sh * 0.08f;



        R(new Rect(x, y, w, h), new Color(0.03f, 0.03f, 0.05f, 0.75f * remain));
        GUI.Label(new Rect(x, y, w, h), _phaseBannerText, St(13, FontStyle.Bold, TextAnchor.MiddleCenter, c));
    }



    Vector2 GetFocusViewportPoint()
    {
        Transform t = drillFocusTransform != null ? drillFocusTransform : (_input.drillTipPoint != null ? _input.drillTipPoint : boneFocusTransform);
        if (!t || !Camera.main) return new Vector2(0.5f, 0.5f);
        Vector3 vp = Camera.main.WorldToViewportPoint(t.position);
        return new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
    }



    bool ShouldLeft(Vector2 vp)
    {
        if (vp.x > 0.6f) return true;
        if (vp.x < 0.4f) return false;
        return true;
    }



    float SmoothX(float target)
    {
        if (_drillPanelCenterX < 0) _drillPanelCenterX = target;
        _drillPanelCenterX = Mathf.Lerp(_drillPanelCenterX, target, 0.1f);
        return _drillPanelCenterX;
    }



    void DrawPositioningUI(float sw, float sh)
    {
        float pw = S(480);
        float ph = S(120);
        float px = (sw - pw) * 0.5f;
        float py = sh * 0.74f;



        Panel(new Rect(px, py, pw, ph));
        GUI.Label(new Rect(px, py + S(6), pw, S(24)), "POSITION DRILL", St(16, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrill));
        GUI.Label(new Rect(px + S(16), py + S(35), pw - S(32), S(22)), "Two-hand pinch: zoom in/out to set camera framing", St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(new Rect(px + S(16), py + S(56), pw - S(32), S(22)), "Touch bone with drill tip, then pinch to start drilling mode", St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(new Rect(px + S(16), py + S(80), pw - S(32), S(20)), "Tip: cyan ring indicates drill-ready pinch strength", St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.7f, 0.95f, 1f)));
    }



    void DrawDrillUI(float sw, float sh)
    {
        float bw = S(320), pw = bw + S(30);
        Vector2 vp = GetFocusViewportPoint();
        bool left = ShouldLeft(vp);



        float targetX = left ? sw * 0.15f : sw * 0.85f;
        float px = SmoothX(targetX) - pw * 0.5f;
        float py = sh * 0.7f;



        Panel(new Rect(px, py, pw, S(110)));



        float pulse = 0.9f + 0.1f * Mathf.Sin(_t * 2);
        GUI.Label(new Rect(px, py + S(5), pw, S(20)), "DRILLING", St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrill * pulse));



        float df = _sim.GetDrillDepthFraction();
        Bar(new Rect(px + S(10), py + S(30), bw, S(18)), df, accentDrill, Color.red, "DEPTH", $"{Mathf.RoundToInt(df * 100)}%");



        float rf = _sim.GetResistanceFraction();
        Bar(new Rect(px + S(10), py + S(55), bw, S(12)), rf, resistLow, resistHigh, "RESIST", $"{Mathf.RoundToInt(rf * 100)}%");
    }



    void DrawPlaceUI(float sw, float sh)
    {
        float pw = S(420);
        float px = (sw - pw) * 0.5f;
        float py = sh * 0.82f;



        Panel(new Rect(px, py, pw, S(90)));
        GUI.Label(new Rect(px, py + S(6), pw, S(20)), "PLACE SCREW", St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentPlace));
        GUI.Label(new Rect(px + S(16), py + S(34), pw - S(32), S(20)), "Move the screw near a highlighted hole and it will magnetically seat", St(11, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(new Rect(px + S(16), py + S(56), pw - S(32), S(20)), "If the screw is dropped, it respawns near your hand for quick recovery", St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.9f, 1f, 0.9f)));
    }



    void DrawDriveUI(float sw, float sh)
    {
        float bw = S(320), pw = bw + S(30);
        float px = sw - pw - S(20);
        float py = sh * 0.7f;



        Panel(new Rect(px, py, pw, S(140)));
        GUI.Label(new Rect(px, py + S(5), pw, S(20)), "DRIVING", St(14, FontStyle.Bold, TextAnchor.MiddleCenter, accentDrive));



        float progress = _sim.GetDriveProgress();
        Bar(new Rect(px + S(10), py + S(30), bw, S(18)), progress, accentDrive, accentDone, "INSERT", $"{Mathf.RoundToInt(progress * 100)}%");



        float cx = px + S(55);
        float cy = py + S(92);
        float radius = S(22);
        R(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), new Color(0.08f, 0.2f, 0.35f, 0.5f));
        R(new Rect(cx - radius, cy - radius, radius * 2 * progress, radius * 2), accentDrive);
        GUI.Label(new Rect(cx - radius, cy - S(9), radius * 2, S(18)), $"{Mathf.RoundToInt(progress * 100)}%", St(10, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));



        GUI.Label(new Rect(px + S(98), py + S(78), pw - S(108), S(20)), _input.DriverAttached ? "Pinch + twist with the same hand" : "Move screwdriver tip close to auto-attach", St(10, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
        GUI.Label(new Rect(px + S(98), py + S(98), pw - S(108), S(20)), _input.DriverAttached ? "Stay aligned to keep insertion smooth" : "Magnetic assist will align the tool near the head", St(10, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.8f, 0.95f, 1f)));
    }



    void DrawDoneUI(float sw, float sh)
    {
        float pw = S(460);
        float ph = S(210);
        float px = (sw - pw) * 0.5f;
        float py = (sh - ph) * 0.5f;



        Panel(new Rect(px, py, pw, ph));
        GUI.Label(new Rect(px, py + S(12), pw, S(28)), "SCREW SIMULATION COMPLETE", St(16, FontStyle.Bold, TextAnchor.MiddleCenter, accentDone));
        GUI.Label(new Rect(px, py + S(52), pw, S(22)), $"Holes Drilled: {_sim.GetHoleCount()}   Placed: {_sim.GetPlacedCount()}   Driven: {_sim.GetFilledCount()}", St(11, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white));
        GUI.Label(new Rect(px + S(28), py + S(82), pw - S(56), S(40)), "Camera has automatically zoomed out for final review.", St(10, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(0.9f, 1f, 0.9f)));



        Rect restartRect = new Rect(px + S(80), py + S(142), S(130), S(42));
        Rect exitRect = new Rect(px + pw - S(210), py + S(142), S(130), S(42));



        GUI.color = new Color(0.2f, 0.65f, 0.35f);
        if (GUI.Button(restartRect, "Restart")) _input.RestartSimulation();



        GUI.color = new Color(0.7f, 0.2f, 0.2f);
        if (GUI.Button(exitRect, "Exit")) _input.ExitSimulation();



        GUI.color = Color.white;
    }
}

