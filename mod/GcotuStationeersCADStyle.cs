// Gcotu Stationeers CAD Style: flat facets, no shine, black lines, solid terrain.
// F9 turns it on and off, F11 opens the settings panel. Writes cad_style_log.txt and look_settings.txt beside the DLL.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

[BepInPlugin("Gcotu.StationeersCADStyle", "Gcotu Stationeers CAD Style", "1.0.1")]
public class CadStylePlugin : BaseUnityPlugin
{
    void Awake()
    {
        var harmony = new Harmony("Gcotu.StationeersCADStyle");
        harmony.PatchAll(typeof(PanelCursorGuard));
        PanelCursorGuard.PatchGameInput(harmony);
        var go = new GameObject("CadStyleRunner");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<CadStyleRunner>();
    }
}

// Game code locks the cursor again on a click, which snaps it to screen center. While the panel is open that lock never happens.
[HarmonyPatch(typeof(Cursor), nameof(Cursor.lockState), MethodType.Setter)]
public static class PanelCursorGuard
{
    public static bool PanelOpen;
    public static bool PressStartedOnPanel;
    public static Rect PanelRect;
    public static float PanelScale = 1f;
    public static string PatchReport = "";

    static void Prefix(ref CursorLockMode value)
    {
        if (PanelOpen) value = CursorLockMode.None;
    }

    // The mouse belongs to the panel while it is over it, and for a whole press that began on it, like a slider dragged past the edge.
    static bool MouseIsOurs
    {
        get
        {
            if (!PanelOpen) return false;
            if (PressStartedOnPanel) return true;
            var m = Input.mousePosition;
            return PanelRect.Contains(new Vector2(m.x, Screen.height - m.y) / PanelScale);
        }
    }

    public static void PatchGameInput(Harmony harmony)
    {
        var report = new List<string>();
        var keys = AccessTools.TypeByName("KeyManager");
        foreach (var name in new[] { "GetButtonDown", "GetButtonUp", "GetButton" })
            Try(harmony, name, keys == null ? null : AccessTools.Method(keys, name, new[] { typeof(KeyCode) }), nameof(ButtonPrefix), report);
        var mouse = AccessTools.TypeByName("Assets.Scripts.UI.InputMouse");
        Try(harmony, "IsMouseOverUi", mouse == null ? null : AccessTools.PropertyGetter(mouse, "IsMouseOverUi"), nameof(OverUiPrefix), report);
        Try(harmony, "mouseScrollDelta", AccessTools.PropertyGetter(typeof(Input), "mouseScrollDelta"), nameof(ScrollPrefix), report);
        var ui = AccessTools.TypeByName("UnityEngine.EventSystems.BaseInput");
        Try(harmony, "ui mouse position", ui == null ? null : AccessTools.PropertyGetter(ui, "mousePosition"), nameof(UiPositionPrefix), report);
        Try(harmony, "ui mouse press", ui == null ? null : AccessTools.Method(ui, "GetMouseButtonDown", new[] { typeof(int) }), nameof(UiPressPrefix), report);
        Try(harmony, "ui mouse wheel", ui == null ? null : AccessTools.PropertyGetter(ui, "mouseScrollDelta"), nameof(ScrollPrefix), report);
        var cursor = AccessTools.AllTypes().FirstOrDefault(t => t.Name == "CursorManager" && t.Namespace != null && t.Namespace.StartsWith("Assets.Scripts"));
        _cursorInstance = cursor == null ? null : AccessTools.Field(cursor, "Instance");
        _cursorBlock = cursor == null ? null : AccessTools.Property(cursor, "BlockCursorRaycast");
        report.Add("cursor block " + (_cursorInstance != null && _cursorBlock != null ? "ok" : "NOT FOUND"));
        var inventory = AccessTools.TypeByName("Assets.Scripts.Inventory.InventoryManager");
        Try(harmony, "cancel key", inventory == null ? null : AccessTools.Method(inventory, "CancelKeyActions"), nameof(CancelPrefix), report);
        PatchReport = string.Join(", ", report);
    }

    static void Try(Harmony harmony, string name, System.Reflection.MethodBase target, string prefix, List<string> report)
    {
        try
        {
            if (target == null) { report.Add(name + " NOT FOUND"); return; }
            harmony.Patch(target, new HarmonyMethod(typeof(PanelCursorGuard), prefix));
            report.Add(name + " ok");
        }
        catch (Exception e) { report.Add(name + " FAILED " + e.Message); }
    }

    // The close key belongs to the window while it is open and until it is released after closing it, so the game doesn't open its menu as well.
    // The game opens its menu on the Cancel key's release (code, KeyManager.cs:128: InventoryManager.CancelKeyActions on InputPhase.Up), so that is held off too.
    public static KeyCode CloseKey = KeyCode.Escape;
    public static bool SwallowClose;

    static bool CancelPrefix()
    {
        return !(PanelOpen || SwallowClose);
    }

    static bool ButtonPrefix(KeyCode key, ref bool __result)
    {
        if (key == CloseKey && (PanelOpen || SwallowClose)) { __result = false; return false; }
        if (key < KeyCode.Mouse0 || key > KeyCode.Mouse6 || !MouseIsOurs) return true;
        __result = false;
        return false;
    }

    static bool OverUiPrefix(ref bool __result)
    {
        if (!MouseIsOurs) return true;
        __result = true;
        return false;
    }

    static bool UiPositionPrefix(ref Vector2 __result)
    {
        if (!MouseIsOurs) return true;
        __result = new Vector2(-100000f, -100000f);
        return false;
    }

    // Releases stay real, so a drag that began outside the panel can always end.
    static bool UiPressPrefix(ref bool __result)
    {
        if (!MouseIsOurs) return true;
        __result = false;
        return false;
    }

    static System.Reflection.FieldInfo _cursorInstance;
    static System.Reflection.PropertyInfo _cursorBlock;
    static bool _blocking, _blockBefore;

    // The game's own switch for looking at things in the world: no highlight and no tooltip. Other game code uses it too, so it gets its old value back.
    public static void UpdateCursorBlock()
    {
        if (_cursorInstance == null || _cursorBlock == null) return;
        var manager = _cursorInstance.GetValue(null);
        if (manager == null) return;
        bool ours = MouseIsOurs;
        if (ours && !_blocking) { _blockBefore = (bool)_cursorBlock.GetValue(manager, null); _blocking = true; }
        if (ours) _cursorBlock.SetValue(manager, true, null);
        else if (_blocking) { _cursorBlock.SetValue(manager, _blockBefore, null); _blocking = false; }
    }

    static bool ScrollPrefix(ref Vector2 __result)
    {
        if (!MouseIsOurs) return true;
        __result = Vector2.zero;
        return false;
    }
}

// Lines go on after opaque lighting and the sky, before glass and particles.
public class CadStyleLines : MonoBehaviour
{
    public Material Mat;
    public Mesh Quad;
    public Action<string> LogLine;
    public bool DrawLines = true;
    public bool DrawShade = true;
    public bool Stabilize;
    RenderTexture _history;     // R: line mask, G: its distance from the camera
    bool _logged;
    static readonly int MaskId = Shader.PropertyToID("_OLMask");
    static readonly int SrcId = Shader.PropertyToID("_OLSrc");
    static readonly int HistId = Shader.PropertyToID("_OLHist");
    static readonly int GlowAId = Shader.PropertyToID("_OLGlowA");
    static readonly int GlowBId = Shader.PropertyToID("_OLGlowB");

    void ReleaseHistory()
    {
        if (_history != null) RenderTexture.ReleaseTemporary(_history);
        _history = null;
    }

    void OnDestroy()
    {
        ReleaseHistory();
        ReleaseMeter();
    }

    // ---- Eye adaptation: measures the frame without waiting for the graphics card, so there is no stall. ----------------
    public bool Adapt;
    public float Gamma = 1f;
    public float MeasuredLight = -1f;     // -1 until the first result arrives
    const int MeterEveryFrames = 6;
    RenderTexture _meter;
    bool _meterPending;
    int _meterFrames;

    void ReleaseMeter()
    {
        if (_meter != null) RenderTexture.ReleaseTemporary(_meter);
        _meter = null;
    }

    static RenderTexture NewMeterLevel(int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear;
        return rt;
    }

    // The middle quarter of the view counts: what you look at blinds you, however dark the rest is. Halved down to a few pixels, then read back.
    void MeasureFrame(RenderTexture source)
    {
        if (_meterPending || ++_meterFrames % MeterEveryFrames != 0) return;
        int width = Mathf.Max(1, source.width / 2), height = Mathf.Max(1, source.height / 2);
        var level = NewMeterLevel(width, height);
        var keepFilter = source.filterMode;
        source.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, level, new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.25f));
        source.filterMode = keepFilter;
        while (width > 4 || height > 4)
        {
            width = Mathf.Max(1, width / 2);
            height = Mathf.Max(1, height / 2);
            var next = NewMeterLevel(width, height);
            Graphics.Blit(level, next);
            RenderTexture.ReleaseTemporary(level);
            level = next;
        }
        _meter = level;
        _meterPending = true;
        AsyncGPUReadback.Request(level, 0, TextureFormat.RGBAFloat, MeterDone);
    }

    void MeterDone(AsyncGPUReadbackRequest request)
    {
        _meterPending = false;
        if (this == null) return;
        ReleaseMeter();
        if (request.hasError) return;
        var pixels = request.GetData<Color>();
        if (pixels.Length == 0) return;
        float sum = 0f;
        for (int i = 0; i < pixels.Length; i++) sum += 0.2126f * pixels[i].r + 0.7152f * pixels[i].g + 0.0722f * pixels[i].b;
        float light = sum / pixels.Length;
        if (!float.IsNaN(light) && !float.IsInfinity(light)) MeasuredLight = light;
    }

    static RenderTexture NewHistory(int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Point;
        return rt;
    }

    // The next history is this frame's result. Empty history has distance 0, which no surface fits, so it counts as nothing.
    void StabilizeMask(RenderTexture mask)
    {
        if (_history != null && (_history.width != mask.width || _history.height != mask.height)) ReleaseHistory();
        if (_history == null)
        {
            _history = NewHistory(mask.width, mask.height);
            Graphics.SetRenderTarget(_history);
            GL.Clear(false, true, Color.clear);
        }
        var next = NewHistory(mask.width, mask.height);
        Shader.SetGlobalTexture(HistId, _history);
        Graphics.SetRenderTarget(next);
        Mat.SetPass(5);
        Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
        ReleaseHistory();
        _history = next;
        Shader.SetGlobalTexture(MaskId, next);
    }

    // ---- Heads keep no lines: the astronaut's face is the first thing a new player sees, and lines don't suit a face. -------------
    public List<Renderer> Faces;
    public Material FaceMat;
    CommandBuffer _faceBuffer;
    static readonly int NoLineId = Shader.PropertyToID("_OLNoLine");
    const float OwnHeadDistance = 0.25f;

    // Marks the heads' pixels in a texture of their own. No depth test: re-rendering a surface onto itself would flicker with the camera's jitter.
    // So anything standing in front of a head loses its lines there too, which only a helmet ever does in practice.
    RenderTexture DrawFaces(RenderTexture source)
    {
        if (FaceMat == null || Faces == null || Faces.Count == 0) return null;
        var cam = GetComponent<Camera>();
        RenderTexture marked = null;
        foreach (var face in Faces)
        {
            if (face == null || !face.enabled || !face.gameObject.activeInHierarchy || !face.isVisible) continue;
            if ((cam.cullingMask & (1 << face.gameObject.layer)) == 0) continue;
            if ((face.bounds.center - cam.transform.position).sqrMagnitude < OwnHeadDistance * OwnHeadDistance) continue;
            if (marked == null)
            {
                marked = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                marked.filterMode = FilterMode.Point;
                if (_faceBuffer == null) _faceBuffer = new CommandBuffer { name = "CadStyle faces" };
                _faceBuffer.Clear();
                _faceBuffer.SetRenderTarget(marked);
                _faceBuffer.ClearRenderTarget(false, true, Color.clear);
                _faceBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            }
            int parts = face.sharedMaterials.Length;
            for (int part = 0; part < parts; part++) _faceBuffer.DrawRenderer(face, FaceMat, part, 0);
        }
        if (marked != null) Graphics.ExecuteCommandBuffer(_faceBuffer);
        return marked;
    }

    public int GlowLevels = 5;
    public bool Glow;

    // The first level was made before lighting and is bound as _OLGlowA by name. Down in halves, then back up adding the smoother levels.
    void DrawGlow(RenderTexture target)
    {
        var levels = new RenderTexture[GlowLevels];
        for (int i = 0; i < GlowLevels; i++)
        {
            levels[i] = RenderTexture.GetTemporary(Mathf.Max(1, target.width >> (i + 2)), Mathf.Max(1, target.height >> (i + 2)), 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            levels[i].filterMode = FilterMode.Point;
            Graphics.SetRenderTarget(levels[i]);
            Mat.SetPass(7);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            Shader.SetGlobalTexture(GlowAId, levels[i]);
        }
        for (int i = GlowLevels - 2; i >= 0; i--)
        {
            var merged = RenderTexture.GetTemporary(levels[i].width, levels[i].height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            merged.filterMode = FilterMode.Point;
            Shader.SetGlobalTexture(GlowAId, levels[i + 1]);
            Shader.SetGlobalTexture(GlowBId, levels[i]);
            Graphics.SetRenderTarget(merged);
            Mat.SetPass(8);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            RenderTexture.ReleaseTemporary(levels[i + 1]);
            RenderTexture.ReleaseTemporary(levels[i]);
            levels[i] = merged;
        }
        Shader.SetGlobalTexture(GlowAId, levels[0]);
        Graphics.SetRenderTarget(target);
        Mat.SetPass(9);
        Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
        RenderTexture.ReleaseTemporary(levels[0]);
    }

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (DrawShade)
        {
            var copy = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
            Graphics.CopyTexture(source, copy);
            Shader.SetGlobalTexture(SrcId, copy);
            Graphics.SetRenderTarget(source);
            Mat.SetPass(4);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            RenderTexture.ReleaseTemporary(copy);
        }
        if (DrawLines)
        {
            var mask = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            mask.filterMode = FilterMode.Point;
            Graphics.SetRenderTarget(mask);
            Mat.SetPass(1);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            Shader.SetGlobalTexture(MaskId, mask);
            if (Stabilize) StabilizeMask(mask); else ReleaseHistory();
            var faces = DrawFaces(source);
            Shader.SetGlobalTexture(NoLineId, faces != null ? (Texture)faces : Texture2D.blackTexture);
            Graphics.SetRenderTarget(source);
            Mat.SetPass(2);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            if (faces != null) RenderTexture.ReleaseTemporary(faces);
            RenderTexture.ReleaseTemporary(mask);
        }
        if (Glow) DrawGlow(source);
        // Measured before the gamma goes on, so the result doesn't chase itself.
        if (Adapt) MeasureFrame(source);
        if (Adapt && Gamma > 1.001f)
        {
            var copy = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
            Graphics.CopyTexture(source, copy);
            Shader.SetGlobalTexture(SrcId, copy);
            Graphics.SetRenderTarget(source);
            Mat.SetPass(12);
            Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
            RenderTexture.ReleaseTemporary(copy);
        }
        Graphics.Blit(source, destination);
        if (!_logged)
        {
            _logged = true;
            LogLine?.Invoke("lines pass ran, source " + source.width + "x" + source.height + " " + source.format);
        }
    }
}

// Lives apart from the look, so F9 leaves it alone: the game's own shading has stair steps too. Added last, so it runs after the lines and the gamma.
public class CadStyleDither : MonoBehaviour
{
    public Material Mat;
    public Mesh Quad;
    public Texture2D Noise;
    static readonly int SrcId = Shader.PropertyToID("_OLSrc");
    static readonly int NoiseId = Shader.PropertyToID("_OLNoise");

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        var copy = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
        Graphics.CopyTexture(source, copy);
        Shader.SetGlobalTexture(SrcId, copy);
        Shader.SetGlobalTexture(NoiseId, Noise);
        Graphics.SetRenderTarget(source);
        Mat.SetPass(13);
        Graphics.DrawMeshNow(Quad, Matrix4x4.identity);
        RenderTexture.ReleaseTemporary(copy);
        Graphics.Blit(source, destination);
    }
}

public class CadStyleRunner : MonoBehaviour
{
    const string BuildStamp = "Gcotu Stationeers CAD Style 1.0.1 v[42, 2026-11-08 15!50 UTC]";

    // ---- Settings -------------------------------------------------------------------------------------------------
    enum S { LineWidth, LineOpacity, CreaseAngle, DepthStep, ColorStep, FacetShade, FacetAzimuth, FacetElevation, AmbientKeep, SmoothForms, Posterize, Bands, BandTop, BandFloor, LineStability, TextureFade, KeepColorLines, GlowStrength, GlowRadius, GlowPasses, AdaptGamma, AdaptSensitivity, AdaptDown, AdaptUp, Dither, GlowColor, ScreenPower, ScreenReach, FlashPower, FlashReach, BoostR, BoostG, BoostB, MakerR, MakerG, MakerB, MakerReach, BatteryPower, BatteryReach, BatteryBias, FurnacePower, FurnaceReach, ApcLed, ApcCell, ApcReach, FlamePower, FlameReach, GradeBlack, GradeWhite, GradeGamma, GradeSaturation, GradeKeep, GradeKeepG, GradeKeepB, FlameRing, FireMax, FirePower, FireGamma, RocketPower, RocketReach, Count }
    enum B { FlatTerrain, HoldBloom, HoldVolumetric, LampSpotsOff, AdaptOn, DitherOn, ScreenLights, Count }

    // Fixed values: these were sliders until v0.0.33, now his tuned values (his K/A/H sort, 2026-11-07). Old keys in saved files are ignored.
    const float LineFadeStart = 0f, LineFadeEnd = 1000f;   // lines never fade with distance
    const float GlowThreshold = 0.1f;
    // The lamp shadow fix ("gold" on his machine): lamp shadow offset, fix limit, cell test, sun offset, lamp shadow map size.
    const float LampOffset = 0.004f, LampFixLimit = 0.03f, LampCellTest = 0.001f, SunOffset = 0.05f;
    const int LampShadowSize = 1280;

    struct SliderDef
    {
        public string Key, Label, Format;
        public float Min, Max, Default, Step, Curve;
        // Curve: the value is Min + (Max - Min) * position ^ Curve, position 0 to 1. Above 1 the slider gets finer at the low end.
        public SliderDef(string key, string label, float min, float max, float def, float step, string format, float curve = 1f)
        { Key = key; Label = label; Min = min; Max = max; Default = def; Step = step; Format = format; Curve = curve; }
    }

    // Same order as S.
    static readonly SliderDef[] Sliders =
    {
        new SliderDef("line_width",      "Width (pixels)",   0f,      8f,    3f,     0.1f,   "0.0"),
        new SliderDef("line_opacity",    "Opacity",          0f,      1f,    0.9f,     0.01f,  "0.00"),
        new SliderDef("crease_angle",    "Crease angle",     5f,      90f,   23f,    1f,     "0"),
        new SliderDef("depth_step",      "Depth step",       0.001f,  0.05f, 0.013f, 0.001f, "0.000"),
        new SliderDef("color_step",      "Color step",       0.02f,   1f,    0.13f,  0.01f,  "0.00"),
        new SliderDef("facet_shade",     "Facet shading",    0f,      1f,    0.85f,     0.01f,  "0.00"),
        new SliderDef("facet_azimuth",   "Shading azimuth",  0f,      360f,  0f,   1f,     "0"),
        new SliderDef("facet_elevation", "Shading height",   0f,      90f,   64f,     1f,     "0"),
        new SliderDef("ambient_keep",    "Ambient light",    0f,      1f,    1f,  0.01f,  "0.00"),
        new SliderDef("smooth_forms",    "Smooth forms",     0f,      1f,    0.73f,     0.01f,  "0.00"),
        new SliderDef("posterize",       "Posterize",        0f,      1f,    0.42f,     0.01f,  "0.00"),
        new SliderDef("bands",           "Bands",            2f,      8f,    4f,     1f,     "0"),
        new SliderDef("band_top",        "Full-light level", 0.1f,    4f,    1.5f,     0.05f,  "0.00"),
        new SliderDef("band_floor",      "Darkest band",     0f,      1f,    0.13f,  0.01f,  "0.00"),
        new SliderDef("line_stability",  "Stability",        0f,      0.97f, 0.6f,   0.01f,  "0.00"),
        new SliderDef("texture_fade",    "Texture fade",     0f,      1f,    0f,     0.01f,  "0.00"),
        new SliderDef("keep_color_lines", "Keep color lines", 0f,     1f,    0.64f,  0.01f,  "0.00"),
        new SliderDef("glow_strength",   "Strength",         0f,      2f,    0.05f,  0.0001f, "0.0000", 4f),
        new SliderDef("glow_radius",     "Radius",           0f,      3f,    1.5f,     0.01f,  "0.00"),
        new SliderDef("glow_passes",     "Passes",           2f,      16f,   6f,     1f,     "0"),
        new SliderDef("adapt_gamma",     "Gamma at bright",  0f,      4f,    4f,     0.01f,  "0.00"),
        new SliderDef("adapt_sensitivity", "Sensitivity",    0.1f,    10f,   0.1f,   0.01f,  "0.00", 2f),
        new SliderDef("adapt_down",      "Fade down (s)",    0.01f,   3f,    1f,     0.01f,  "0.00"),
        new SliderDef("adapt_up",        "Fade up (s)",      0.1f,    20f,   3f,     0.1f,   "0.0"),
        new SliderDef("dither",          "Blue noise",       0f,      2f,    0.5f,   0.01f,  "0.00"),
        new SliderDef("glow_color",      "Colored glow",     0f,      1f,    1f,     0.01f,  "0.00"),
        new SliderDef("screen_power",    "Strength",         0f,      5f,    0.5f,   0.01f,  "0.00", 2f),
        new SliderDef("screen_reach",    "Reach (m)",        0.1f,    20f,   1.3f,     0.1f,   "0.0", 2f),
        new SliderDef("flash_power",     "Strength",         0f,      5f,    0.5f,   0.01f,  "0.00", 2f),
        new SliderDef("flash_reach",     "Reach (m)",        0.1f,    20f,   5f,     0.1f,   "0.0", 2f),
        new SliderDef("glow_boost_r",    "Color boost R",    0f,      20f,   1.5f,     0.01f,  "0.00", 2f),
        new SliderDef("glow_boost_g",    "Color boost G",    0f,      20f,   1.5f,     0.01f,  "0.00", 2f),
        new SliderDef("glow_boost_b",    "Color boost B",    0f,      20f,   1.5f,     0.01f,  "0.00", 2f),
        new SliderDef("maker_r",         "Red",              0f,      5f,    0.3f,   0.01f,  "0.00", 2f),
        new SliderDef("maker_g",         "Green",            0f,      5f,    0.5f,   0.01f,  "0.00", 2f),
        new SliderDef("maker_b",         "Blue",             0f,      5f,    0.3f,   0.01f,  "0.00", 2f),
        new SliderDef("maker_reach",     "Reach (m)",        0.1f,    20f,   4f,   0.1f,   "0.0", 2f),
        new SliderDef("battery_power",   "Strength",         0f,      5f,    1f,   0.01f,  "0.00", 2f),
        new SliderDef("battery_reach",   "Reach (m)",        0.1f,    20f,   1.5f,   0.1f,   "0.0", 2f),
        new SliderDef("battery_bias",    "Switch to bars",   0f,      1f,    0.7f,   0.01f,  "0.00"),
        new SliderDef("furnace_power",   "Strength",         0f,      10f,   3f,     0.01f,  "0.00", 2f),
        new SliderDef("furnace_reach",   "Reach (m)",        0.1f,    20f,   3f,     0.1f,   "0.0", 2f),
        new SliderDef("apc_led",         "LED strength",     0f,      5f,    0.5f,   0.01f,  "0.00", 2f),
        new SliderDef("apc_cell",        "Cell strength",    0f,      5f,    0.5f,   0.01f,  "0.00", 2f),
        new SliderDef("apc_reach",       "Reach (m)",        0.1f,    20f,   1.3f,   0.1f,   "0.0", 2f),
        new SliderDef("flame_power",     "Strength",         0f,      10f,   1.01f,     0.01f,  "0.00", 2f),
        new SliderDef("flame_reach",     "Reach (m)",        0.1f,    20f,   3.5f,     0.1f,   "0.0", 2f),
        new SliderDef("grade_black",     "Black point",      0f,      0.9f,  0f,     0.01f,  "0.00"),
        new SliderDef("grade_white",     "White point",      0f,      0.9f,  0.23f,  0.01f,  "0.00"),
        new SliderDef("grade_gamma",     "Gamma",            0.2f,    5f,    3f,     0.01f,  "0.00"),
        new SliderDef("grade_saturation", "Saturation",      0f,      2f,    0f,     0.01f,  "0.00"),
        new SliderDef("grade_keep_r",    "Keep vivid red",   0f,      1f,    0.85f,  0.01f,  "0.00"),
        new SliderDef("grade_keep_g",    "Keep vivid green", 0f,      1f,    0.23f,  0.01f,  "0.00"),
        new SliderDef("grade_keep_b",    "Keep vivid blue",  0f,      1f,    0f,     0.01f,  "0.00"),
        new SliderDef("flame_ring",      "Outside glow",     0f,      2f,    0.42f,  0.01f,  "0.00"),
        new SliderDef("fire_max",        "Most lights",      0f,      512f,  256f,    1f,     "0", 2f),
        new SliderDef("fire_power",      "Strength",         0f,      10f,   2.98f,     0.01f,  "0.00", 2f),
        new SliderDef("fire_gamma",      "Gamma",            0.2f,    5f,    0.5f,     0.01f,  "0.00"),
        new SliderDef("rocket_power",    "Strength",         0f,      50f,   1f,    0.1f,   "0.0", 2f),
        new SliderDef("rocket_reach",    "Reach (m)",        1f,      200f,  100f,    1f,     "0", 2f),
    };

    // Same order as B.
    static readonly string[] ToggleKeys = { "flat_terrain", "hold_bloom", "hold_volumetric", "lamp_spots_off", "adapt_on", "dither_on", "screen_lights" };
    static readonly bool[] ToggleDefaults = { true, false, true, false, true, true, true };
    static readonly string[] ToggleLabels = { "Solid-color terrain", "Bloom off", "Volumetric light off", "Lamp spots off", "Eye adaptation", "Blue noise", "Screen lights" };
    static readonly string[] OffEffects = { "UltimateBloom", "VolumetricLightRenderer" }; // HoldBloom, HoldVolumetric
    const string AlwaysOffEffect = "SSAOPro";

    readonly float[] _v = new float[(int)S.Count];
    readonly bool[] _b = new bool[(int)B.Count];
    float[] _vBeforeReset;
    bool[] _bBeforeReset;
    bool _settingsDirty;
    float _settingsChangedAt;

    float V(S s) { return _v[(int)s]; }

    void SetDefaults()
    {
        for (int i = 0; i < _v.Length; i++) _v[i] = Sliders[i].Default;
        for (int i = 0; i < _b.Length; i++) _b[i] = ToggleDefaults[i];
    }

    string SettingsPath { get { return Path.Combine(_dir, "look_settings.txt"); } }

    void LoadSettings()
    {
        SetDefaults();
        if (!File.Exists(SettingsPath)) { Log("no settings file yet, using defaults"); return; }
        try
        {
            ParseValues(File.ReadAllLines(SettingsPath), _v, _b, true);
            Log("settings loaded from " + SettingsPath);
        }
        catch (Exception e) { Log("settings load failed, using what was read so far: " + e.Message); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string from, string to, uint flags);

    static void Replace(string from, string to)
    {
        const uint ReplaceExisting = 1, WriteThrough = 8;
        if (!MoveFileExW(from, to, ReplaceExisting | WriteThrough))
            throw new IOException("MoveFileEx " + from + " -> " + to + " failed, error " + Marshal.GetLastWin32Error());
    }

    // Temp in the same folder, fsync, copy the old file to .bak, atomic rename. The file is whole at every instant.
    static void WriteSafely(string path, string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        if (File.Exists(path))
        {
            string bakTmp = path + ".bak.tmp";
            File.Copy(path, bakTmp, true);
            using (var fs = new FileStream(bakTmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) fs.Flush(true);
            Replace(bakTmp, path + ".bak");
        }
        Replace(tmp, path);
    }

    string ValuesText()
    {
        var text = new StringBuilder();
        for (int i = 0; i < _v.Length; i++) text.Append(Sliders[i].Key).Append('=').Append(_v[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        for (int i = 0; i < _b.Length; i++) text.Append(ToggleKeys[i]).Append('=').Append(_b[i] ? "1" : "0").Append('\n');
        return text.ToString();
    }

    void SaveSettings()
    {
        _settingsDirty = false;
        try
        {
            string active = _activeSlot >= 0 ? "active_preset=" + (_activeSlot + 1).ToString(CultureInfo.InvariantCulture) + "\n" : "";
            WriteSafely(SettingsPath, ValuesText() + active + "window_opens=" + _windowOpens.ToString(CultureInfo.InvariantCulture) + "\npause_on_open=" + (_pauseOnOpen ? "1" : "0") + "\nstatus_seen=" + _statusSeen + "\n");
        }
        catch (Exception e) { Log("settings save failed: " + e.Message); }
    }

    void MarkChanged()
    {
        _settingsDirty = true;
        _settingsChangedAt = Time.unscaledTime;
    }

    // ---- Look -----------------------------------------------------------------------------------------------------
    // The planet's terrain uses _MainTex1/2 and _MacroTex (TerrainShaderScript in the game); the rest is the older terrain shader.
    static readonly string[] TerrainColorKeys = { "_MainTex1", "_MainTex2", "_MacroTex", "_MainTex", "_MainTexTop", "_MainTexSide" };
    static readonly string[] TerrainNormalKeys = { "_BumpMap1", "_BumpMap2", "_MacroNormal" };
    static readonly string[] TerrainWhiteKeys = { "_DetailTex", "_DetailTexTop", "_DetailTexSide" };

    string _dir;
    Material _mat;
    Mesh _quad;
    Texture2D _params;
    Camera _cam;
    CommandBuffer _gbufferBuffer;
    CommandBuffer _releaseBuffer;
    CadStyleLines _lines;
    bool _active = true;
    bool _componentsLogged;
    int _frame;
    string _lastError;
    readonly Dictionary<Behaviour, bool> _effects = new Dictionary<Behaviour, bool>();
    readonly Dictionary<Material, Dictionary<string, Texture>> _terrain = new Dictionary<Material, Dictionary<string, Texture>>();
    readonly Dictionary<Texture, Texture2D> _solidCache = new Dictionary<Texture, Texture2D>();
    readonly HashSet<Texture2D> _ownSolids = new HashSet<Texture2D>();
    Texture2D _white;
    bool _hasPrevCam;
    Vector3 _prevCamPos;
    Matrix4x4 _prevW2V;
    string _taaSeen;

    void Log(string s)
    {
        try { File.AppendAllText(Path.Combine(_dir, "cad_style_log.txt"), DateTime.UtcNow.ToString("HH:mm:ss") + " " + s + Environment.NewLine); }
        catch { }
    }

    // His call: the log keeps its minute report but stops growing. Past about 1 MB it becomes cad_style_log.old.txt (replacing the older one) and a new log starts.
    const long LogMaxBytes = 1024 * 1024;

    void RollLog()
    {
        try
        {
            string path = Path.Combine(_dir, "cad_style_log.txt");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < LogMaxBytes) return;
            Replace(path, Path.Combine(_dir, "cad_style_log.old.txt"));
        }
        catch { }
    }

    void Start()
    {
        _dir = Path.GetDirectoryName(typeof(CadStyleRunner).Assembly.Location);
        if (string.IsNullOrEmpty(_dir))
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Stationeers", "mods", "GcotuStationeersCADStyle");
        RollLog();
        Log("start " + BuildStamp + ", Unity " + Application.unityVersion + ", api " + SystemInfo.graphicsDeviceType + ", reversedZ " + SystemInfo.usesReversedZBuffer + ", dir " + _dir);
        LoadSettings();
        for (int i = 0; i < PresetCount; i++) ReadPreset(i);
        LoadBenchmarks();
        LoadKeys();
        Log("game input patches: " + PanelCursorGuard.PatchReport);
        string shaderWhy = "the shader files were not found in Content";
        // DirectX 11 only. Under Vulkan or OpenGL the fallback bundle would load with the game's placeholder programs for those
        // modes (build_look.py keeps them unchanged), so the shaders are not even tried there.
        var api = SystemInfo.graphicsDeviceType;
        bool dx11 = api == GraphicsDeviceType.Direct3D11;
        if (!dx11) shaderWhy = "the game runs with " + api + ", the look is made for DirectX 11 only (remove a launch option such as -force-vulkan)";
        foreach (var name in dx11 ? new[] { "cadstyle_dx11.bundle", "cadstyle_full.bundle" } : new string[0])
        {
            try
            {
                var b = AssetBundle.LoadFromFile(Path.Combine(_dir, "Content", name));
                if (b == null) { Log(name + ": LoadFromFile returned null"); shaderWhy = "the shader file " + name + " did not load"; continue; }
                var s = b.LoadAllAssets<Shader>().FirstOrDefault();
                Log(name + ": shader " + (s == null ? "missing" : "'" + s.name + "' supported=" + s.isSupported + " passes=" + s.passCount));
                if (s != null && s.isSupported && s.passCount == 14)
                {
                    _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                    break;
                }
                shaderWhy = s == null ? "the shader file " + name + " holds no shader" : !s.isSupported ? "this graphics card or graphics mode can't run the shader (it is made for DirectX 11)" : "the shader has " + s.passCount + " parts instead of 14";
                b.Unload(true);
            }
            catch (Exception e) { Log(name + ": " + e); shaderWhy = "the shader file " + name + " failed to load: " + e.Message; }
        }
        // Without the shader the window still opens, so the player can read why in the Status tab.
        if (_mat == null)
        {
            Log("NO USABLE SHADER, the look stays off: " + shaderWhy);
            _active = false;
            _lookWhy = shaderWhy + (dx11 ? ". Made for engine " + BuiltForEngine + ", the game runs " + Application.unityVersion + "." : ".");
        }
        else LoadLampShader();

        _quad = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
        _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        _quad.UploadMeshData(false);

        _params = new Texture2D(19, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        _white = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
        _white.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
        _white.Apply(false, false);
        Camera.onPreRender += PreRender;
        RunChecks();
        Log("ready; " + _keyLook + " toggles the look, " + _keyWindow + " opens the window");
    }

    void OnDestroy()
    {
        Camera.onPreRender -= PreRender;
        SetLampFix(false);
        ShowHand(false);
        if (_settingsDirty) SaveSettings();
    }

    void OnApplicationQuit()
    {
        if (_settingsDirty) SaveSettings();
    }

    void Update()
    {
        try
        {
            _frame++;
            if (Input.GetKeyDown(_keyLook)) SetActive(!_active);
            if (Input.GetKeyDown(_keyWindow)) SetPanelOpen(!_panelOpen);
            else if (_panelOpen && Input.GetKeyDown(_keyClose))
            {
                PanelCursorGuard.SwallowClose = true;
                _closeReleasedFrame = -1;
                SetPanelOpen(false);
            }
            // Held until the frame after the key is up, so the game's release handler finds it swallowed whichever runs first.
            if (PanelCursorGuard.SwallowClose && !Input.GetKey(_keyClose))
            {
                if (_closeReleasedFrame < 0) _closeReleasedFrame = Time.frameCount;
                else if (Time.frameCount > _closeReleasedFrame) PanelCursorGuard.SwallowClose = false;
            }
            UpdateGameState();
            var cam = Camera.main;
            if (cam != _cam)
            {
                Uninstall();
                _cam = cam;
                _componentsLogged = false;
                if (_cam != null && _active) Install();
            }
            if (_active && _cam != null && _frame % 60 == 0) ApplyEffectHolds();
            if (_active && _b[(int)B.FlatTerrain] && _frame % 300 == 0) { long started = Clock(); ScanTerrain(); Note("terrain scan", started); }
            ReportJobs();
            PanelCursorGuard.UpdateCursorBlock();
            UpdateMeasure();
            SetLampFix(_active && _cam != null);
            UpdateDither();
            if (_active && _cam != null)
            {
                if ((V(S.GlowStrength) > 0f) != _builtGlow) BuildBuffers();
                if (_lines != null) _lines.Glow = _builtGlow;
                UpdateFade();
                if (_frame % 120 == 0 || !_bleedTouched) { long started = Clock(); ApplyLightBleed(true); Note("lamp shadow values", started); }
                if (_frame % 120 == 0) ApplyLampSpots(_b[(int)B.LampSpotsOff]);
                { long started = Clock(); UpdateBulbs(); Note("round bulbs", started); }
                UpdateAdaptation();
                if (_frame % 120 == 0) { long started = Clock(); FindFaces(); Note("faces search", started); }
            }
            if (_settingsDirty && Time.unscaledTime - _settingsChangedAt > 1f) SaveSettings();
        }
        catch (Exception e)
        {
            var text = e.ToString();
            if (text != _lastError) { _lastError = text; Log("ERROR " + text); }
        }
    }

    // After every Update, so a switch flipped this frame shows in this frame's light, and the flashlight's lamp stands where the camera now is.
    void UpdateLampsLate()
    {
        try
        {
            if (!_active || _cam == null) return;
            UpdateDeviceLights();
            long flashStarted = Clock();
            UpdateFlashlight();
            Note("flashlight", flashStarted);
            long fireStarted = Clock();
            UpdateFireLights();
            Note("fire lights", fireStarted);
        }
        catch (Exception e)
        {
            var text = e.ToString();
            if (text != _lastLampError) { _lastLampError = text; Log("ERROR " + text); }
        }
    }

    string _lastLampError, _lastUiError;

    void SetActive(bool on)
    {
        if (on && _mat == null) return;
        _active = on;
        Log("look active=" + _active);
        if (_active) Install(); else Uninstall();
    }

    bool _depthAdded;

    void Install()
    {
        if (_cam == null || _gbufferBuffer != null) return;
        _depthAdded = (_cam.depthTextureMode & DepthTextureMode.Depth) == 0;
        _cam.depthTextureMode |= DepthTextureMode.Depth;
        Log("installing on camera '" + _cam.name + "', path " + _cam.actualRenderingPath + ", hdr " + _cam.allowHDR + ", size " + _cam.pixelWidth + "x" + _cam.pixelHeight);
        if (!_componentsLogged)
        {
            _componentsLogged = true;
            Log("camera components: " + string.Join(", ", _cam.GetComponents<MonoBehaviour>().Select(c => c.GetType().Name + (c.enabled ? "" : "(off)"))));
        }
        BuildBuffers();
        _lines = _cam.gameObject.AddComponent<CadStyleLines>();
        _lines.Mat = _mat;
        _lines.Quad = _quad;
        _lines.LogLine = Log;
        FindFaces();
        _lines.Faces = _faces;
        _lines.FaceMat = _faceMat;
        ApplyEffectHolds();
        if (_b[(int)B.FlatTerrain]) ScanTerrain();
        // The next UpdateDither adds it again, behind the lines and the gamma.
        if (_ditherFx != null) { Destroy(_ditherFx); _ditherFx = null; }
    }

    void Uninstall()
    {
        if (_cam != null)
        {
            if (_gbufferBuffer != null) _cam.RemoveCommandBuffer(CameraEvent.BeforeReflections, _gbufferBuffer);
            if (_releaseBuffer != null) _cam.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _releaseBuffer);
            if (_depthAdded) _cam.depthTextureMode &= ~DepthTextureMode.Depth;
        }
        _depthAdded = false;
        if (_lines != null) Destroy(_lines);
        _lines = null;
        _gbufferBuffer = null;
        _releaseBuffer = null;
        ApplyLightBleed(false);
        ApplyLampSpots(false);
        RestoreBulbs();
        ClearDeviceLights();
        ClearFlashlight();
        ClearFireLights();
        _adaptGamma = 1f;
        if (_fadeOn) RestoreFade();
        foreach (var kv in _effects)
            if (kv.Key != null) kv.Key.enabled = kv.Value;
        _effects.Clear();
        RestoreTerrain();
    }

    void ApplyEffectHolds()
    {
        if (_cam == null) return;
        foreach (var c in _cam.GetComponents<MonoBehaviour>())
        {
            string effect = c.GetType().Name;
            _effectsSeen.Add(effect);
            if (effect == "TAASimple")
            {
                string taa = c.enabled ? "on" : "off";
                if (taa != _taaSeen) { _taaSeen = taa; Log("game TAA (camera jitter) is " + taa); }
            }
            int i = Array.IndexOf(OffEffects, effect);
            if (i < 0 && effect != AlwaysOffEffect) continue;
            bool hold = i < 0 || _b[(int)B.HoldBloom + i];
            if (hold)
            {
                if (!_effects.ContainsKey(c)) { _effects[c] = c.enabled; Log("effect held off: " + c.GetType().Name + ", was enabled=" + c.enabled); }
                if (c.enabled) c.enabled = false;
            }
            else
            {
                bool was;
                if (_effects.TryGetValue(c, out was)) { c.enabled = was; _effects.Remove(c); Log("effect released: " + c.GetType().Name + ", enabled=" + was); }
            }
        }
    }

    static bool IsTerrain(Material m)
    {
        return m.HasProperty("_MainTex1") && m.HasProperty("_MacroTex") || m.shader.name.Contains("WorldspaceOverlayTerrain");
    }

    // The game can set terrain textures again on world load, so this looks at the current textures every time.
    void ScanTerrain()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || m.shader == null || !IsTerrain(m)) continue;
            Dictionary<string, Texture> saved;
            bool known = _terrain.TryGetValue(m, out saved);
            if (!known) saved = new Dictionary<string, Texture>();
            foreach (var key in TerrainColorKeys)
            {
                if (!m.HasProperty(key)) continue;
                var tex = m.GetTexture(key);
                if (tex == null || tex is Texture2D && _ownSolids.Contains((Texture2D)tex)) continue;
                saved[key] = tex;
                m.SetTexture(key, Solid(tex));
            }
            foreach (var key in TerrainNormalKeys) SwapIfOther(m, key, Texture2D.normalTexture, saved);
            foreach (var key in TerrainWhiteKeys) SwapIfOther(m, key, _white, saved);
            _terrain[m] = saved;
            if (!known) Log("terrain material flattened: " + m.name + " (" + m.shader.name + "), textures " + saved.Count);
        }
        if (clock.ElapsedMilliseconds > 20) Log("terrain scan took " + clock.ElapsedMilliseconds + " ms");
    }

    static void SwapIfOther(Material m, string key, Texture replacement, Dictionary<string, Texture> saved)
    {
        if (!m.HasProperty(key)) return;
        var tex = m.GetTexture(key);
        if (tex == replacement) return;
        saved[key] = tex;
        m.SetTexture(key, replacement);
    }

    // ---- White glow --------------------------------------------------------------------------------------------------
    bool _builtGlow;

    // ---- Lamp shadow values: the fixed offsets and lamp shadow map size above while the look is on; F9 off gives the game's own back. ----
    readonly Dictionary<Light, float[]> _lightBias = new Dictionary<Light, float[]>();
    bool _biasLogged;
    bool _bleedTouched;

    // The game keeps its prefabs as copies under one inactive root object, in a real scene (Prefab.cs). Lamps are cloned from those,
    // so anything changed on them comes back in every new lamp and then passes for the game's own value. Hands off.
    static bool IsGameTemplate(Component c)
    {
        return !c.transform.root.gameObject.activeSelf;
    }

    // Normal offsets stay the game's own. The sun keeps its own shadow map size.
    void ApplyLightBleed(bool on)
    {
        if (!on && !_bleedTouched) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var seen = _biasLogged ? null : new Dictionary<string, int>();
        // Real lamps only. A prefab changed here would hand our values to every lamp made from it, and those would then pass for the game's own.
        foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (l == null || !l.gameObject.scene.IsValid() || IsGameTemplate(l)) continue;
            float[] was;
            bool known = _lightBias.TryGetValue(l, out was);
            if (!on)
            {
                if (known)
                {
                    l.shadowBias = was[0];
                    l.shadowCustomResolution = (int)was[1];
                }
                continue;
            }
            bool isSun = l.type == LightType.Directional;
            if (!known)
            {
                was = new[] { l.shadowBias, (float)l.shadowCustomResolution };
                _lightBias[l] = was;
                if (seen != null)
                {
                    string key = (isSun ? "sun offset " : "lamp offset ") + was[0].ToString("0.###", CultureInfo.InvariantCulture) + " size " + was[1].ToString("0", CultureInfo.InvariantCulture);
                    int n;
                    seen.TryGetValue(key, out n);
                    seen[key] = n + 1;
                }
            }
            float bias = isSun ? SunOffset : LampOffset;
            if (l.shadowBias != bias) l.shadowBias = bias;
            int size = isSun ? (int)was[1] : LampShadowSize;
            if (l.shadowCustomResolution != size) l.shadowCustomResolution = size;
        }
        if (!on) _lightBias.Clear();
        if (seen != null && seen.Count > 0)
        {
            if (seen.Keys.Any(k => k.StartsWith("sun"))) _biasLogged = true;
            Log("game's own shadow values: " + string.Join(", ", seen.Select(kv => kv.Key + " x" + kv.Value)));
        }
        _bleedTouched = on;
        if (clock.ElapsedMilliseconds > 20) Log("lamp shadow values took " + clock.ElapsedMilliseconds + " ms");
    }

    // ---- Round lamp bulbs: the game makes emissive only the parts in the lamp's paint material. On round lamps that is the body, not the bulb. ----
    // So a bulb here follows its body: emissive when the body is, its own material otherwise. Long lamps' bodies are never emissive, so this leaves them alone.
    class LampBulb
    {
        public Renderer Body, Bulb;
        public Material Own, BodySeen;
        public bool Lit;
    }

    readonly List<LampBulb> _bulbs = new List<LampBulb>();
    readonly HashSet<Renderer> _bulbsKnown = new HashSet<Renderer>();

    static void SetFirstMaterial(Renderer r, Material m)
    {
        var all = r.sharedMaterials;
        if (all.Length == 0) return;
        all[0] = m;
        r.sharedMaterials = all;
    }

    void FindBulbs()
    {
        foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (l == null || !l.gameObject.scene.IsValid() || IsGameTemplate(l) || l.transform.parent == null) continue;
            var root = l.transform.parent;
            var bulbObject = root.Find("Bulb");
            if (bulbObject == null) continue;
            Renderer body = root.GetComponent<MeshRenderer>(), bulb = bulbObject.GetComponent<MeshRenderer>();
            if (body == null || bulb == null || !_bulbsKnown.Add(bulb)) continue;
            _bulbs.Add(new LampBulb { Body = body, Bulb = bulb, Own = bulb.sharedMaterial });
        }
    }

    void UpdateBulbs()
    {
        if (_frame % 120 == 0) FindBulbs();
        for (int i = _bulbs.Count - 1; i >= 0; i--)
        {
            var b = _bulbs[i];
            if (b.Body == null || b.Bulb == null)
            {
                if (b.Bulb != null) _bulbsKnown.Remove(b.Bulb);
                _bulbs.RemoveAt(i);
                continue;
            }
            var seen = b.Body.sharedMaterial;
            if (seen == b.BodySeen) continue;
            b.BodySeen = seen;
            bool lit = seen != null && seen.name.Contains("Emissive");
            if (lit) { SetFirstMaterial(b.Bulb, seen); b.Lit = true; }
            else if (b.Lit) { SetFirstMaterial(b.Bulb, b.Own); b.Lit = false; }
        }
    }

    void RestoreBulbs()
    {
        foreach (var b in _bulbs)
            if (b.Lit && b.Bulb != null) SetFirstMaterial(b.Bulb, b.Own);
        _bulbs.Clear();
        _bulbsKnown.Clear();
    }

    // ---- Eye adaptation: too much light raises the gamma fast, and it comes back slowly. What is brighter than white stays white. ---
    // Measured light on a log scale gives 0 to 1, as in his SEGI fork's eye adaptation; here it only ever darkens.
    const float AdaptLogMin = -6f, AdaptLogMax = 2f;
    float _adaptGamma = 1f;
    Texture2D _noise;
    bool _noiseTried;

    // Christoph Peters' free blue noise tile, CC0. Read the first time dither is wanted.
    void LoadNoise()
    {
        _noiseTried = true;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            if (!tex.LoadImage(File.ReadAllBytes(Path.Combine(_dir, "Content", "bluenoise.png")), true)) { Log("dither unavailable: image didn't load"); return; }
            _noise = tex;
            Log("dither: blue noise " + tex.width + "x" + tex.height);
        }
        catch (Exception e) { Log("dither unavailable: " + e.Message); }
    }

    CadStyleDither _ditherFx;

    bool DitherWanted { get { return _b[(int)B.DitherOn] && V(S.Dither) > 0f; } }

    // Independent of the look: only its own checkbox and slider count.
    void UpdateDither()
    {
        if (_ditherFx != null && (_cam == null || _ditherFx.gameObject != _cam.gameObject)) { Destroy(_ditherFx); _ditherFx = null; }
        if (DitherWanted && !_noiseTried) LoadNoise();
        bool want = DitherWanted && _noise != null && _cam != null && _mat != null;
        if (want && _ditherFx == null)
        {
            _ditherFx = _cam.gameObject.AddComponent<CadStyleDither>();
            _ditherFx.Mat = _mat;
            _ditherFx.Quad = _quad;
            _ditherFx.Noise = _noise;
        }
        else if (!want && _ditherFx != null) { Destroy(_ditherFx); _ditherFx = null; }
    }

    Color AdaptRow()
    {
        return new Color(_adaptGamma, _noise != null ? V(S.Dither) / 255f : 0f, _noise != null ? _noise.width : 1f, 0f);
    }

    // ---- Fill lights: plain lamps that cast no shadow. Per vertex, so glass, which answers a lamp mostly with its per pixel shine, doesn't show them (from his SEGI fork). ----
    static Light NewFillLight(string name)
    {
        var o = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        var l = o.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForceVertex;
        l.color = Color.white;
        return l;
    }

    // Which heads go without lines. Found by the game's object names, hair by its own name; the shadow copies draw nothing visible.
    readonly List<Renderer> _faces = new List<Renderer>();
    Material _faceMat;
    bool _faceMatLooked;
    int _facesLogged = -1;

    void FindFaces()
    {
        if (!_faceMatLooked)
        {
            _faceMatLooked = true;
            var plain = Shader.Find("Unlit/Texture") ?? Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name == "Unlit/Texture");
            if (plain == null) Log("faces: Unlit/Texture not found, heads keep their lines");
            else _faceMat = new Material(plain) { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
        }
        _faces.Clear();
        if (_faceMat == null) return;
        foreach (var r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            string n = r.name;
            if (n.EndsWith("_SHADOW", StringComparison.Ordinal) || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
            if (n == "HEAD_RENDERER" || n.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) _faces.Add(r);
        }
        if (_faces.Count != _facesLogged)
        {
            _facesLogged = _faces.Count;
            Log("faces found: " + _faces.Count + " " + string.Join(", ", _faces.Select(r => r.name)));
        }
    }

    // Lamps on devices: each a child of its device, so it goes when the device goes. States are read every frame, so they follow a switch at once.
    // Finding devices: the game keeps every placed device in one list (Device.AllDevices, code Device.cs:24, 1531). A few are looked at each frame, each
    // device is sorted only once, so there is no moment that searches the whole world. Reads go through compiled getters, which make no garbage.
    // Computers: exact class, their Console subclass has screens of its own. On means switched on and powered. The game hides a screen's picture
    // whenever nobody stands in front of it, so following the picture itself would pop the light while walking around.
    // Machines: the listed names, at the end of the front where screen and buttons are. Batteries: the two station batteries, inside, on means switched on.
    // Arc furnace: one lamp inside, weak green when on, fading to strong orange while smelting at the pace of the game's own glow.
    // Area power control: the LED sits on the sliding door (second part of its mesh), so its lamp is a child of the door and slides along.
    // With the door open a second lamp before the opening shows the color of the cell inside.
    // Consoles and vending machines share the machines' sliders. Vents, ice crusher, heaters, trader waypoint and LED displays share the power control's.
    // Furnace and advanced furnace read the game's own flame; four weak lamps around the case stand for the light the lit room throws back.
    // Any other device with a screen part (ScreenNoShadow) lights the room in that screen's own glow, with the computer screens' sliders.
    // Doors (airlock, wide airlock, blast door, composite door): the game's own status lights (Door.StatusLights) reach only 0.6 to 1 m,
    // so one lamp at their middle shows their mixed color into the room: green active, red locked, white unlocked (code, Door.cs:69-73).
    // Transformers: their green light, as a switch. Heaters: 3 x the small device strength and 2 x its reach; one lamp at a pipe heater was too weak to see (his test).
    // Rocket engines: the game's own engine light (FireLight, RocketPlume) is on while it fires (tested, prefabs: off at rest); one orange-white lamp, own sliders.
    // It casts shadows, so its light stays outside the hull instead of shining through the rocket.
    enum LampKind { Screen, Machine, Battery, Furnace, PowerControl, Console, Switch, Heater, Beacon, Led, Flame, ScreenGlow, Door, Rocket }
    const float HeaterBoost = 3f, HeaterReachBoost = 2f;
    // Wide doors only (his test on the wide airlock: right). On 1-wide doors the boost floods the small airlock rooms red (his test), so they keep 1 x.
    const float DoorBoost = 4f, DoorReachBoost = 3f;
    static bool IsWideDoor(Component door)
    {
        string name = PlainName(door);
        return name.Contains("Wide") || name.Contains("Blast") || name.Contains("Hanger");
    }
    static readonly Color RocketWhite = new Color(1f, 0.8f, 0.55f);   // his call: bright orange-white, not blue-white
    const float LampInFront = 0.15f;
    static readonly HashSet<string> MachineNames = new HashSet<string> { "StructureAdvancedPackagingMachine", "StructureAutolathe", "StructureAutomatedOven", "StructureElectronicsPrinter", "StructureHydraulicPipeBender", "StructureRocketManufactory", "StructureSecurityPrinter", "StructureToolManufactory" };
    static readonly Vector3 MachineFallback = new Vector3(-0.4f, 0.45f, 0.25f);
    static readonly HashSet<string> BatteryNames = new HashSet<string> { "StructureBattery", "StructureBatteryLarge" };
    static readonly HashSet<string> FlameFurnaceNames = new HashSet<string> { "StructureFurnace", "StructureAdvancedFurnace" };
    const int DevicesPerFrame = 100;

    class DeviceLamp
    {
        public Component Device;
        public Light Lamp;
        public LampKind Kind;
        public float Heat;
        public Light Cell;
        public Color Tint;
        public bool Heats;
        public Renderer Glow;
        public Light[] Ring;
        public float RingReach;
        public float Scale = 1f;
        public Light[] Status;
    }

    readonly List<DeviceLamp> _deviceLamps = new List<DeviceLamp>();
    readonly HashSet<Component> _deviceKnown = new HashSet<Component>();
    readonly HashSet<Component> _deviceChecked = new HashSet<Component>();
    Type _doorType, _transformerType;
    System.Reflection.FieldInfo _doorLights;
    Type _computerType, _machineType, _batteryType, _furnaceType, _controlType, _consoleType, _vendingType, _activeVentType, _poweredVentType, _iceCrusherType, _wallHeaterType, _pipeHeaterType, _displayType, _flameType;
    System.Reflection.FieldInfo _flameRenderer, _displayText, _displayDigit, _computerScreen, _poolEntries, _poolActive, _poolCount;
    System.Reflection.PropertyInfo _controlCell;
    Func<object, bool> _isOn, _isPowered, _isOpen;
    Func<object, int> _modeOf, _activateOf, _errorOf;
    object _devicePool;
    int _poolCursor, _wantedMask = -1;
    bool _devicesLooked;

    bool LampWanted(LampKind kind)
    {
        switch (kind)
        {
            case LampKind.Screen:
            case LampKind.ScreenGlow: return _b[(int)B.ScreenLights] && V(S.ScreenPower) > 0f;
            case LampKind.Machine:
            case LampKind.Console: return V(S.MakerR) > 0f || V(S.MakerG) > 0f || V(S.MakerB) > 0f;
            case LampKind.Furnace: return V(S.FurnacePower) > 0f;
            case LampKind.Flame: return V(S.FlamePower) > 0f;
            case LampKind.PowerControl: return V(S.ApcLed) > 0f || V(S.ApcCell) > 0f;
            case LampKind.Switch:
            case LampKind.Heater:
            case LampKind.Beacon:
            case LampKind.Led:
            case LampKind.Door: return V(S.ApcLed) > 0f;
            case LampKind.Rocket: return V(S.RocketPower) > 0f;
            default: return V(S.BatteryPower) > 0f;
        }
    }

    int WantedMask()
    {
        int mask = 0;
        for (int k = 0; k <= (int)LampKind.Rocket; k++)
            if (LampWanted((LampKind)k)) mask |= 1 << k;
        return mask;
    }

    static string PlainName(Component c)
    {
        return c.gameObject.name.Replace("(Clone)", "").Trim();
    }

    // A getter compiled once: reading a game value then costs a call and makes no garbage, unlike reflection, which boxes every value it returns.
    static Func<object, T> Getter<T>(System.Reflection.PropertyInfo p)
    {
        if (p == null) return null;
        try
        {
            var o = System.Linq.Expressions.Expression.Parameter(typeof(object), "o");
            var body = System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(o, p.DeclaringType), p);
            return System.Linq.Expressions.Expression.Lambda<Func<object, T>>(body, o).Compile();
        }
        catch { return x => (T)p.GetValue(x, null); }
    }

    Type FindGameType(string name)
    {
        var found = AccessTools.TypeByName(name);
        if (found == null) Log("device lights: " + name + " not found, its light stays off");
        return found;
    }

    static string Short(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName.Substring(dot + 1);
    }

    // A game class and, optionally, one of its fields or properties. Returns the type, or null with the reason why the lamps stay off.
    Type FindKind(string typeName, out string why)
    {
        var type = FindGameType(typeName);
        why = type == null ? "The game's class " + Short(typeName) + " was not found." : null;
        return type;
    }

    bool NeedMember(Type type, object member, string memberName, ref string why)
    {
        if (type == null || member != null) return member != null;
        Log("device lights: " + type.Name + "." + memberName + " not found, its light stays off");
        why = "The game's " + type.Name + " no longer has " + memberName + ".";
        return false;
    }

    // The device states each kind of lamp reads; a missing one darkens only the lamps that read it.
    readonly HashSet<string> _states = new HashSet<string>();
    string _deviceListWhy;
    System.Reflection.FieldInfo _devicePoolField;

    void Kind(string feature, ref Type type, string why, string needs)
    {
        if (why == null) why = _deviceListWhy;
        if (why == null && needs.Length > 0)
        {
            var missing = needs.Split(',').Where(n => !_states.Contains(n)).ToArray();
            if (missing.Length > 0) why = "The game's device state " + string.Join(", ", missing) + " was not found.";
        }
        if (why != null) type = null;
        SetFeature(feature, why == null, why);
    }

    // Only names are looked up here, so it runs at the main menu; the device list itself is read once a world exists.
    void LookForDevices()
    {
        _devicesLooked = true;
        var thing = AccessTools.TypeByName("Assets.Scripts.Objects.Thing");
        _isOn = thing == null ? null : Getter<bool>(AccessTools.Property(thing, "OnOff"));
        _isPowered = thing == null ? null : Getter<bool>(AccessTools.Property(thing, "Powered"));
        _isOpen = thing == null ? null : Getter<bool>(AccessTools.Property(thing, "IsOpen"));
        _modeOf = thing == null ? null : Getter<int>(AccessTools.Property(thing, "Mode"));
        _activateOf = thing == null ? null : Getter<int>(AccessTools.Property(thing, "Activate"));
        _errorOf = thing == null ? null : Getter<int>(AccessTools.Property(thing, "Error"));
        _states.Clear();
        if (_isPowered != null) _states.Add("Powered");
        if (_isOpen != null) _states.Add("IsOpen");
        if (_modeOf != null) _states.Add("Mode");
        if (_activateOf != null) _states.Add("Activate");
        if (_errorOf != null) _states.Add("Error");
        foreach (var n in new[] { "Powered", "IsOpen", "Mode", "Activate", "Error" })
            if (!_states.Contains(n)) Log("device lights: the game's device state " + n + " not found, lamps that read it stay off");
        var device = AccessTools.TypeByName("Assets.Scripts.Objects.Pipes.Device");
        _devicePoolField = device == null ? null : AccessTools.Field(device, "AllDevices");
        if (_devicePoolField != null)
        {
            var poolType = _devicePoolField.FieldType;
            _poolEntries = AccessTools.Field(poolType, "_entries");
            _poolActive = AccessTools.Field(poolType, "_activeList");
            _poolCount = AccessTools.Field(poolType, "_activeCount");
        }
        _deviceListWhy = thing == null ? "The game's class Thing was not found."
            : _isOn == null ? "The game's device state OnOff was not found."
            : device == null ? "The game's class Device was not found."
            : _devicePoolField == null ? "The game's device list (Device.AllDevices) was not found."
            : _poolEntries == null || _poolActive == null || _poolCount == null ? "The game's device list has changed its form."
            : null;
        if (_deviceListWhy != null) { Log("device lights: " + _deviceListWhy + " They stay off."); _isOn = null; }

        string why;
        _computerType = FindKind("Assets.Scripts.Objects.Electrical.Computer", out why);
        _computerScreen = _computerType == null ? null : AccessTools.Field(_computerType, "ComputerScreen");
        NeedMember(_computerType, _computerScreen, "ComputerScreen", ref why);
        Kind("screen", ref _computerType, why, "Powered");

        _machineType = FindKind("Assets.Scripts.Objects.Electrical.FabricatorBase", out why);
        string consoleWhy, vendingWhy;
        _consoleType = FindKind("Assets.Scripts.Objects.Electrical.Console", out consoleWhy);
        _vendingType = FindKind("Assets.Scripts.Objects.Electrical.VendingMachine", out vendingWhy);
        Kind("machine", ref _machineType, why, "Powered");
        Kind("console", ref _consoleType, consoleWhy, "Powered");
        Kind("vending", ref _vendingType, vendingWhy, "Powered");

        _batteryType = FindKind("Assets.Scripts.Objects.Electrical.Battery", out why);
        Kind("battery", ref _batteryType, why, "Mode");

        _furnaceType = FindKind("Assets.Scripts.Objects.Pipes.ArcFurnace", out why);
        Kind("arcfurnace", ref _furnaceType, why, "Powered,Activate");

        _flameType = FindKind("Assets.Scripts.Objects.Pipes.FurnaceBase", out why);
        _flameRenderer = _flameType == null ? null : AccessTools.Field(_flameType, "FlameRenderer");
        NeedMember(_flameType, _flameRenderer, "FlameRenderer", ref why);
        Kind("furnace", ref _flameType, why, "");

        _controlType = FindKind("Assets.Scripts.Objects.Electrical.AreaPowerControl", out why);
        _controlCell = _controlType == null ? null : AccessTools.Property(_controlType, "Battery");
        NeedMember(_controlType, _controlCell, "Battery", ref why);
        Kind("apc", ref _controlType, why, "Mode,Error,IsOpen");

        string poweredWhy, crusherWhy;
        _activeVentType = FindKind("Assets.Scripts.Objects.Pipes.ActiveVent", out why);
        _poweredVentType = FindKind("Assets.Scripts.Objects.Pipes.PoweredVent", out poweredWhy);
        _iceCrusherType = FindKind("Assets.Scripts.Objects.Pipes.IceCrusher", out crusherWhy);
        Kind("vents", ref _activeVentType, why, "Powered,Error");
        Kind("poweredvents", ref _poweredVentType, poweredWhy, "Powered,Error");
        Kind("crusher", ref _iceCrusherType, crusherWhy, "Powered,Error,Activate");

        string pipeWhy;
        _wallHeaterType = FindKind("Assets.Scripts.Objects.Electrical.WallHeater", out why);
        _pipeHeaterType = FindKind("Assets.Scripts.Objects.Pipes.PipeHeater", out pipeWhy);
        Kind("heaters", ref _wallHeaterType, why, "Powered,Error");
        Kind("pipeheaters", ref _pipeHeaterType, pipeWhy, "Powered,Error");

        _transformerType = FindKind("Assets.Scripts.Objects.Electrical.Transformer", out why);
        Kind("transformers", ref _transformerType, why, "Powered,Error");

        _doorType = FindKind("Assets.Scripts.Objects.Structures.Door", out why);
        _doorLights = _doorType == null ? null : AccessTools.Field(_doorType, "StatusLights");
        NeedMember(_doorType, _doorLights, "StatusLights", ref why);
        Kind("doors", ref _doorType, why, "");

        _displayType = FindKind("Assets.Scripts.Objects.Electrical.LogicDisplay", out why);
        _displayText = _displayType == null ? null : AccessTools.Field(_displayType, "_displayString");
        _displayDigit = _displayType == null ? null : AccessTools.Field(_displayType, "DigitOn");
        if (NeedMember(_displayType, _displayText, "_displayString", ref why)) NeedMember(_displayType, _displayDigit, "DigitOn", ref why);
        Kind("displays", ref _displayType, why, "Powered");

        // Found by object names, not classes: the trader waypoint, rocket engines, other screens.
        Type byName = typeof(object);
        Kind("waypoint", ref byName, null, "Powered");
        byName = typeof(object);
        Kind("rocket", ref byName, null, "");
        byName = typeof(object);
        Kind("screenglow", ref byName, null, "");
        _waypointOk = Ok("waypoint");
    }

    bool _waypointOk;

    static bool Is(Type type, Component c)
    {
        return type != null && type.IsInstanceOfType(c);
    }

    // Looks at the next few devices in the game's list. A device is sorted once; when a light group is switched on, everything is looked at again.
    void SweepDevices()
    {
        int mask = WantedMask();
        if (mask != _wantedMask) { _wantedMask = mask; _deviceChecked.Clear(); _poolCursor = 0; }
        if (mask == 0) return;
        var entries = _poolEntries.GetValue(_devicePool) as Array;
        var active = _poolActive.GetValue(_devicePool) as int[];
        int count = (int)_poolCount.GetValue(_devicePool);
        if (entries == null || active == null) return;
        for (int n = 0; n < DevicesPerFrame; n++)
        {
            if (_poolCursor >= count || _poolCursor >= active.Length)
            {
                _poolCursor = 0;
                _deviceChecked.RemoveWhere(x => x == null);
                return;
            }
            int slot = active[_poolCursor++];
            if (slot < 0 || slot >= entries.Length) continue;
            var d = entries.GetValue(slot) as Component;
            if (d == null || _deviceKnown.Contains(d) || !_deviceChecked.Add(d) || IsGameTemplate(d)) continue;
            Sort(d);
        }
    }

    void Sort(Component d)
    {
        var type = d.GetType();
        if (type == _computerType) { if (LampWanted(LampKind.Screen)) AddLamp(d, LampKind.Screen, ScreenLampPlace(d)); return; }
        if (type == _consoleType) { if (LampWanted(LampKind.Console)) AddLamp(d, LampKind.Console, ScreenLampPlace(d)); return; }
        string name = PlainName(d);
        if (Is(_machineType, d) && MachineNames.Contains(name)) { if (LampWanted(LampKind.Machine)) AddLamp(d, LampKind.Machine, MachineLampPlace(d)); return; }
        if (Is(_vendingType, d)) { if (LampWanted(LampKind.Machine)) AddLamp(d, LampKind.Machine, MachineLampPlace(d)); return; }
        if (Is(_batteryType, d) && BatteryNames.Contains(name)) { if (LampWanted(LampKind.Battery)) AddLamp(d, LampKind.Battery, CenterOf(d)); return; }
        if (Is(_furnaceType, d)) { if (LampWanted(LampKind.Furnace)) AddLamp(d, LampKind.Furnace, FurnaceLampPlace(d)); return; }
        if (Is(_controlType, d)) { if (LampWanted(LampKind.PowerControl)) AddControlLamps(d); return; }
        if (Is(_flameType, d) && FlameFurnaceNames.Contains(name)) { if (LampWanted(LampKind.Flame)) AddFlameLamps(d); return; }
        bool vent = Is(_activeVentType, d) || Is(_poweredVentType, d), crusher = Is(_iceCrusherType, d);
        if (Is(_transformerType, d)) { if (LampWanted(LampKind.Switch)) AddTinted(d, LampKind.Switch, PartLampPlace(d, "SwitchOnOff", SwitchLampOut), Color.white, false); return; }
        if (vent || crusher) { if (LampWanted(LampKind.Switch)) AddTinted(d, LampKind.Switch, PartLampPlace(d, "SwitchOnOff", SwitchLampOut), Color.white, crusher); return; }
        if (Is(_wallHeaterType, d)) { if (LampWanted(LampKind.Heater)) AddTinted(d, LampKind.Heater, PartLampPlace(d, "HeatingElement", 0f), HeatingRed, false); return; }
        if (Is(_pipeHeaterType, d)) { if (LampWanted(LampKind.Heater)) AddTinted(d, LampKind.Heater, CenterOf(d), PipeHeaterOrange, false); return; }
        if (Is(_displayType, d)) { if (LampWanted(LampKind.Led)) AddLamp(d, LampKind.Led, PartLampPlace(d, "Screen", SwitchLampOut)); return; }
        if (Is(_doorType, d)) { if (LampWanted(LampKind.Door)) AddDoorLamp(d); return; }
        if (name == "StructureTraderWaypoint") { if (_waypointOk && LampWanted(LampKind.Beacon)) AddTinted(d, LampKind.Beacon, PartLampPlace(d, "RedBulb", 0f), WaypointBulbs, false); return; }
        if (AddRocketLamp(d)) return;
        if (!LampWanted(LampKind.ScreenGlow)) return;
        foreach (var r in d.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("ScreenNoShadow", StringComparison.Ordinal))
            {
                AddLamp(d, LampKind.ScreenGlow, r.bounds.center + (r.bounds.center - CenterOf(d)).normalized * LampInFront);
                _deviceLamps[_deviceLamps.Count - 1].Glow = r;
                return;
            }
    }

    // A part the game no longer has: the lamp still works, at a fallback place; said once per kind of device and part.
    readonly HashSet<string> _partsMissingLogged = new HashSet<string>();

    void PartMissing(Component device, string part)
    {
        string key = PlainName(device) + "/" + part;
        if (_partsMissingLogged.Add(key)) Log("device lights: " + PlainName(device) + " has no part " + part + ", its lamp sits at a fallback place");
    }

    // Upgrading a machine swaps its parts, so its lamp is placed again every 120 frames.
    Vector3 MachineLampPlace(Component machine)
    {
        foreach (var r in machine.GetComponentsInChildren<Renderer>(false))
            if (r.name.StartsWith("ScreenNoShadow", StringComparison.Ordinal))
                return r.bounds.center + machine.transform.forward * LampInFront;
        if (!machine.GetComponentsInChildren<Renderer>(true).Any(r => r.name.StartsWith("ScreenNoShadow", StringComparison.Ordinal))) PartMissing(machine, "ScreenNoShadow");
        return machine.transform.TransformPoint(MachineFallback) + machine.transform.forward * LampInFront;
    }

    static Transform FindDeep(Component root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Vector3 CenterOf(Component device)
    {
        var r = device.GetComponent<Renderer>();
        return r != null ? r.bounds.center : device.transform.position;
    }

    // A named part's center, moved a little away from the device's middle, so the lamp stands in the room.
    Vector3 PartLampPlace(Component device, string part, float out_)
    {
        var t = FindDeep(device, part);
        if (t == null) { PartMissing(device, part); return CenterOf(device); }
        var r = t.GetComponent<Renderer>();
        var at = r != null ? r.bounds.center : t.position;
        return at + (at - CenterOf(device)).normalized * out_;
    }

    Vector3 FurnaceLampPlace(Component furnace)
    {
        var glow = furnace.transform.Find("InteriorGlow");
        var r = glow == null ? null : glow.GetComponent<Renderer>();
        if (r == null) PartMissing(furnace, "InteriorGlow");
        return r != null ? r.bounds.center : furnace.transform.TransformPoint(0f, 0.86f, 0f);
    }

    Vector3 ScreenLampPlace(Component computer)
    {
        var screen = _computerScreen == null ? null : _computerScreen.GetValue(computer) as GameObject;
        return screen != null ? screen.transform.position - screen.transform.forward * LampInFront : CenterOf(computer);
    }

    void AddLamp(Component device, LampKind kind, Vector3 place)
    {
        var lamp = NewFillLight("CadStyleDeviceLight");
        lamp.transform.SetParent(device.transform, false);
        lamp.transform.position = place;
        lamp.enabled = false;
        _deviceKnown.Add(device);
        _deviceLamps.Add(new DeviceLamp { Device = device, Lamp = lamp, Kind = kind });
    }

    void AddTinted(Component device, LampKind kind, Vector3 place, Color tint, bool heats)
    {
        AddLamp(device, kind, place);
        var d = _deviceLamps[_deviceLamps.Count - 1];
        d.Tint = tint;
        d.Heats = heats;
    }

    // Four lamps just outside the case, one per side, at the middle of its height: the light the bright room throws back onto the furnace,
    // which the lamp inside can't reach. Placed from the furnace's own parts, so it fits the small furnace and the big advanced one alike.
    const float FlameRingGap = 0.3f;
    const float AdvancedFurnaceScale = 1.2f;   // his call: the big one 1.2 x in strength and reach, inside and outside

    static Bounds BodyOf(Component device)
    {
        var all = device.GetComponentsInChildren<Renderer>(false);
        var body = new Bounds(device.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in all)
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (first) { body = r.bounds; first = false; }
            else body.Encapsulate(r.bounds);
        }
        return body;
    }

    void AddFlameLamps(Component furnace)
    {
        var center = FurnaceLampPlace(furnace);
        AddLamp(furnace, LampKind.Flame, center);
        var d = _deviceLamps[_deviceLamps.Count - 1];
        d.Ring = new Light[4];
        var body = BodyOf(furnace);
        var e = body.extents;
        var places = new[] { new Vector3(e.x + FlameRingGap, 0f, 0f), new Vector3(-e.x - FlameRingGap, 0f, 0f), new Vector3(0f, 0f, e.z + FlameRingGap), new Vector3(0f, 0f, -e.z - FlameRingGap) };
        d.RingReach = Mathf.Max(e.y, Mathf.Max(e.x, e.z)) + 2f * FlameRingGap + 0.5f;
        if (PlainName(furnace) == "StructureAdvancedFurnace") d.Scale = AdvancedFurnaceScale;
        for (int i = 0; i < 4; i++)
        {
            var lamp = NewFillLight("CadStyleDeviceLight");
            lamp.transform.SetParent(furnace.transform, false);
            lamp.transform.position = body.center + places[i];
            lamp.enabled = false;
            d.Ring[i] = lamp;
        }
    }

    void AddDoorLamp(Component door)
    {
        var lights = _doorLights.GetValue(door) as Light[];
        if (lights == null || lights.Length == 0) return;
        var middle = Vector3.zero;
        int count = 0;
        foreach (var l in lights)
            if (l != null) { middle += l.transform.position; count++; }
        if (count == 0) return;
        AddLamp(door, LampKind.Door, middle / count);
        _deviceLamps[_deviceLamps.Count - 1].Status = lights;
        _deviceLamps[_deviceLamps.Count - 1].Scale = IsWideDoor(door) ? 1f : 0f;
    }

    bool AddRocketLamp(Component engine)
    {
        Light[] plume = null;
        int count = 0;
        var middle = Vector3.zero;
        foreach (var l in engine.GetComponentsInChildren<Light>(true))
        {
            if (l.name != "FireLight" && !l.name.StartsWith("RocketPlume", StringComparison.Ordinal)) continue;
            if (plume == null) plume = new Light[4];
            if (count == plume.Length) Array.Resize(ref plume, count * 2);
            plume[count++] = l;
            middle += l.transform.position;
        }
        if (count == 0) return false;
        if (!LampWanted(LampKind.Rocket)) return true;
        Array.Resize(ref plume, count);
        AddLamp(engine, LampKind.Rocket, middle / count);
        var d = _deviceLamps[_deviceLamps.Count - 1];
        d.Status = plume;
        d.Lamp.shadows = LightShadows.Soft;
        return true;
    }

    static readonly Vector3 ControlLedOnDoor = new Vector3(-0.1f, -0.175f, 0.0135f);

    void AddControlLamps(Component control)
    {
        var inFront = control.transform.TransformPoint(0f, 0f, 0.235f + LampInFront);
        var door = control.transform.Find("Door");
        var led = door == null ? null : door.GetComponent<MeshFilter>();
        if (door == null)
        {
            PartMissing(control, "Door");
            AddLamp(control, LampKind.PowerControl, inFront);
        }
        else
        {
            var onDoor = led != null && led.sharedMesh != null && led.sharedMesh.subMeshCount > 1 ? led.sharedMesh.GetSubMesh(1).bounds.center : ControlLedOnDoor;
            AddLamp(control, LampKind.PowerControl, door.TransformPoint(onDoor + Vector3.forward * LampInFront));
            _deviceLamps[_deviceLamps.Count - 1].Lamp.transform.SetParent(door, true);
        }
        var cell = NewFillLight("CadStyleDeviceLight");
        cell.transform.SetParent(control.transform, false);
        cell.transform.position = inFront;
        cell.enabled = false;
        _deviceLamps[_deviceLamps.Count - 1].Cell = cell;
    }

    const float SwitchLampOut = 0.1f;
    static readonly Color SwitchGreen = new Color(0.2f, 1f, 0f);
    static readonly Color SwitchError = new Color(1f, 0.4f, 0f);
    static readonly Color HeatingRed = new Color(1f, 0.05f, 0.02f);
    static readonly Color PipeHeaterOrange = new Color(1f, 0.25f, 0f);
    static readonly Color WaypointBulbs = new Color(1f, 0.55f, 0f);
    const float ErrorBlinkHalf = 0.25f;   // the game's flashing LED takes 250 ms per step (code, ApcMaterialChanger)
    const int DisplayFullText = 6;
    // The game sets the flame's light to color x intensity x 5 (code, FurnaceBase.cs), so this brings its top to Strength.
    const float FlameFull = 0.2f;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    static Color EmissionOf(Material m)
    {
        return m != null && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black;
    }

    // True when this function has taken care of the lamp.
    bool UpdateSharedLamp(DeviceLamp d, bool on)
    {
        switch (d.Kind)
        {
            case LampKind.Switch:
            {
                var light = Color.black;
                if (on && _errorOf(d.Device) == 1)
                {
                    if (Mathf.Repeat(Time.unscaledTime, ErrorBlinkHalf * 2f) < ErrorBlinkHalf) light = SwitchError;
                }
                else if (on && _isPowered(d.Device))
                {
                    light = SwitchGreen;
                    if (d.Heats && _activateOf(d.Device) == 1) light = SwitchGreen * 0.3f + HeatingRed;
                }
                SetColoredLamp(d.Lamp, light * V(S.ApcLed));
                return true;
            }
            case LampKind.Heater:
                SetColoredLamp(d.Lamp, on && _isPowered(d.Device) && _errorOf(d.Device) == 0 ? d.Tint * (V(S.ApcLed) * HeaterBoost) : Color.black, V(S.ApcReach) * HeaterReachBoost);
                return true;
            case LampKind.Rocket:
            {
                bool firing = false;
                if (d.Status != null)
                    foreach (var l in d.Status)
                        if (l != null && l.enabled && l.gameObject.activeInHierarchy) { firing = true; break; }
                SetColoredLamp(d.Lamp, firing ? RocketWhite * V(S.RocketPower) : Color.black, V(S.RocketReach));
                return true;
            }
            case LampKind.Beacon:
                SetColoredLamp(d.Lamp, on && _isPowered(d.Device) ? d.Tint * V(S.ApcLed) : Color.black);
                return true;
            case LampKind.Led:
            {
                var shown = Color.black;
                if (on && _isPowered(d.Device))
                {
                    int visible = 0;
                    var text = _displayText.GetValue(d.Device) as char[];
                    if (text != null)
                        foreach (char ch in text)
                            if (ch != ' ' && ch != '\0') visible++;
                    if (visible > 0)
                        shown = Normalized(EmissionOf(_displayDigit.GetValue(d.Device) as Material)) * (Mathf.Min(visible, DisplayFullText) / (float)DisplayFullText) * V(S.ApcLed);
                }
                SetColoredLamp(d.Lamp, shown);
                return true;
            }
            case LampKind.Flame:
            {
                var flame = _flameRenderer.GetValue(d.Device) as Renderer;
                var burning = flame != null ? EmissionOf(flame.sharedMaterial) * (FlameFull * V(S.FlamePower) * d.Scale) : Color.black;
                SetColoredLamp(d.Lamp, burning, V(S.FlameReach) * d.Scale);
                if (d.Ring != null)
                    foreach (var r in d.Ring)
                        if (r != null) SetColoredLamp(r, burning * V(S.FlameRing), d.RingReach * d.Scale);
                return true;
            }
            case LampKind.Door:
            {
                var mixed = Color.black;
                if (d.Status != null)
                    foreach (var l in d.Status)
                        if (l != null && l.enabled && l.gameObject.activeInHierarchy) mixed += l.color;
                bool wide = d.Scale > 0.5f;
                SetColoredLamp(d.Lamp, Normalized(mixed) * (V(S.ApcLed) * (wide ? DoorBoost : 1f)), V(S.ApcReach) * (wide ? DoorReachBoost : 1f));
                return true;
            }
            case LampKind.ScreenGlow:
            {
                float top = 0f;
                var glow = d.Glow != null && d.Glow.gameObject.activeInHierarchy ? EmissionOf(d.Glow.sharedMaterial) : Color.black;
                top = Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b));
                SetColoredLamp(d.Lamp, top > 0f ? Normalized(glow) * (Mathf.Min(top, 1f) * V(S.ScreenPower)) : Color.black, V(S.ScreenReach));
                return true;
            }
            default:
                return false;
        }
    }

    // What a battery really shows: its green switch mixed with the bars' color. The slider decides between them. No bars lit: the switch alone.
    // Mode 0 to 6 means no bars, then red, red, orange, yellow, green, blue (code, Battery.cs). The colors are estimates: the look comes from the palette.
    static readonly Color BatterySwitch = new Color(0.2f, 1f, 0f);
    static readonly Color[] BatteryBarColors = { Color.black, new Color(1f, 0f, 0f), new Color(1f, 0f, 0f), new Color(1f, 0.4f, 0f), new Color(1f, 0.9f, 0f), new Color(0.2f, 1f, 0f), new Color(0f, 0.1f, 1f) };

    static Color Normalized(Color mixed)
    {
        float top = Mathf.Max(mixed.r, Mathf.Max(mixed.g, mixed.b));
        return top > 0f ? new Color(mixed.r / top, mixed.g / top, mixed.b / top) : Color.black;
    }

    static Color BatteryColor(int mode, float bias)
    {
        mode = Mathf.Clamp(mode, 0, 6);
        return Normalized(mode == 0 ? BatterySwitch : BatterySwitch * (1f - bias) + BatteryBarColors[mode] * bias);
    }

    // The LED by the game's states (code, ApcMaterialChanger): charging blue, charged green, empty red, errors orange, idle dark. It flashes in some of them, the lamp doesn't.
    // Discharging's color is an estimate.
    static Color ControlLed(int mode, int error)
    {
        if (error == 1) return new Color(1f, 0.4f, 0f);
        switch (mode)
        {
            case 1: return new Color(1f, 0f, 0f);
            case 2: return new Color(1f, 0.6f, 0f);
            case 3: return new Color(0f, 0.1f, 1f);
            case 4: return new Color(0.2f, 1f, 0f);
            default: return Color.black;
        }
    }

    static void DestroyLamps(DeviceLamp d)
    {
        if (d.Lamp != null) Destroy(d.Lamp.gameObject);
        if (d.Cell != null) Destroy(d.Cell.gameObject);
        if (d.Ring != null)
            foreach (var r in d.Ring)
                if (r != null) Destroy(r.gameObject);
    }

    void UpdateDeviceLights()
    {
        if (!_devicesLooked) LookForDevices();
        if (_isOn == null) return;
        if (_devicePool == null)
        {
            _devicePool = _devicePoolField.GetValue(null);
            if (_devicePool == null) return;
        }
        long started = Clock();
        SweepDevices();
        if (_frame % 120 == 0)
            foreach (var d in _deviceLamps)
                if (d.Kind == LampKind.Machine && d.Device != null && d.Lamp != null) d.Lamp.transform.position = MachineLampPlace(d.Device);
        float makerTop = Mathf.Max(V(S.MakerR), Mathf.Max(V(S.MakerG), V(S.MakerB)));
        for (int i = _deviceLamps.Count - 1; i >= 0; i--)
        {
            var d = _deviceLamps[i];
            if (d.Device == null || d.Lamp == null || !LampWanted(d.Kind))
            {
                DestroyLamps(d);
                _deviceKnown.Remove(d.Device);
                _deviceLamps.RemoveAt(i);
                continue;
            }
            // One device the game has changed under us costs only its own lamp, not every lamp after it in this frame.
            try { UpdateOneDeviceLamp(d, makerTop); }
            catch (Exception e)
            {
                if (_deviceFailLogged.Add(d.Kind)) Log("device lights: a " + d.Kind + " lamp failed and is dropped: " + e.Message);
                DestroyLamps(d);
                _deviceKnown.Remove(d.Device);
                _deviceLamps.RemoveAt(i);
            }
        }
        Note("device lights", started);
    }

    readonly HashSet<LampKind> _deviceFailLogged = new HashSet<LampKind>();

    void UpdateOneDeviceLamp(DeviceLamp d, float makerTop)
    {
        {
            bool on = _isOn(d.Device);
            if (d.Kind == LampKind.PowerControl)
            {
                UpdateControlLamp(d, on);
                return;
            }
            if (UpdateSharedLamp(d, on)) return;
            if (d.Kind != LampKind.Battery) on = on && _isPowered(d.Device);
            if (d.Lamp.enabled != on) d.Lamp.enabled = on;
            if (!on)
            {
                d.Heat = 0f;
                return;
            }
            switch (d.Kind)
            {
                case LampKind.Furnace:
                    d.Heat = Mathf.MoveTowards(d.Heat, _activateOf(d.Device) == 1 ? 1f : 0f, Time.deltaTime * FurnaceWarmPerSecond);
                    var heat = Color.Lerp(FurnaceOn, FurnaceSmelting, d.Heat);
                    float heatTop = Mathf.Max(heat.r, Mathf.Max(heat.g, heat.b));
                    d.Lamp.color = Normalized(heat);
                    d.Lamp.intensity = V(S.FurnacePower) * heatTop;
                    d.Lamp.range = V(S.FurnaceReach);
                    break;
                case LampKind.Screen:
                    d.Lamp.intensity = V(S.ScreenPower);
                    d.Lamp.range = V(S.ScreenReach);
                    break;
                case LampKind.Machine:
                case LampKind.Console:
                    d.Lamp.color = new Color(V(S.MakerR) / makerTop, V(S.MakerG) / makerTop, V(S.MakerB) / makerTop);
                    d.Lamp.intensity = makerTop;
                    d.Lamp.range = V(S.MakerReach);
                    break;
                default:
                    d.Lamp.color = BatteryColor(_modeOf(d.Device), V(S.BatteryBias));
                    d.Lamp.intensity = V(S.BatteryPower);
                    d.Lamp.range = V(S.BatteryReach);
                    break;
            }
        }
    }

    // Same pace as the game's own glow in the furnace (code, ArcFurnace.cs).
    const float FurnaceWarmPerSecond = 0.5f;
    static readonly Color FurnaceOn = new Color(0.024f, 0.12f, 0f);
    static readonly Color FurnaceSmelting = new Color(1f, 0.45f, 0.05f);

    void UpdateControlLamp(DeviceLamp d, bool switchedOn)
    {
        var led = switchedOn ? ControlLed(_modeOf(d.Device), _errorOf(d.Device)) : Color.black;
        SetColoredLamp(d.Lamp, led * V(S.ApcLed));
        var shown = Color.black;
        if (_isOpen(d.Device))
        {
            var cell = _controlCell.GetValue(d.Device, null) as Component;
            if (cell != null) shown = BatteryBarColors[Mathf.Clamp(_modeOf(cell), 0, 6)] * V(S.ApcCell);
        }
        if (d.Cell != null) SetColoredLamp(d.Cell, shown);
    }

    void SetColoredLamp(Light lamp, Color light)
    {
        SetColoredLamp(lamp, light, V(S.ApcReach));
    }

    void SetColoredLamp(Light lamp, Color light, float reach)
    {
        float top = Mathf.Max(light.r, Mathf.Max(light.g, light.b));
        bool on = top > 0f;
        if (lamp.enabled != on) lamp.enabled = on;
        if (!on) return;
        lamp.color = Normalized(light);
        lamp.intensity = top;
        lamp.range = reach;
    }

    // ---- Fire lights: burning gas lights the world. The game keeps every burning atmosphere in a list (AtmosphericFire, code AtmosphericFire.cs:13);
    // its flame strength (0.01 to 1) and temperature color are what its particles show. The strongest and nearest get a lamp, up to Most lights.
    // Gamma bends strength before Strength scales it, so weak fires can be pushed down or lifted.
    const float FireReach = 3.5f;
    const int FireEveryFrames = 3;
    static readonly Color FireOrange = new Color(1f, 0.45f, 0.08f);
    readonly List<Light> _fireLamps = new List<Light>();
    System.Reflection.FieldInfo _fireList;
    Func<object, Vector3> _firePlace;
    Func<object, bool> _fireBurning;
    Func<object, float> _fireSize;
    Func<object, Color> _fireColor;
    bool _firesLooked;
    System.Reflection.FieldInfo _thingFireList;
    Func<object, Vector3> _thingFirePlace;
    Func<object, bool> _thingFireBurning;
    float[] _fireScore = new float[64];
    int[] _fireIndex = new int[64];
    // The game changes both fire lists from other threads, under its own locks (code, AtmosphericFire.cs:96,130 lock AtmosphericFireLookup;
    // ThingFire.cs:81,89 lock the list itself). Reading them unlocked threw "index out of range" now and then (his log). So: copy under the same lock, then read the copy.
    System.Reflection.FieldInfo _fireGate;
    readonly List<object> _fireCopy = new List<object>(), _thingCopy = new List<object>();

    static void CopyLocked(System.Collections.IList list, object gate, List<object> into)
    {
        into.Clear();
        if (list == null) return;
        lock (gate ?? list)
            for (int i = 0; i < list.Count; i++) into.Add(list[i]);
    }

    static Func<object, T> Compiled<T>(Type type, Func<System.Linq.Expressions.Expression, System.Linq.Expressions.Expression> body, Func<object, T> slow)
    {
        try
        {
            var o = System.Linq.Expressions.Expression.Parameter(typeof(object), "o");
            return System.Linq.Expressions.Expression.Lambda<Func<object, T>>(body(System.Linq.Expressions.Expression.Convert(o, type)), o).Compile();
        }
        catch { return slow; }
    }

    void LookForFires()
    {
        _firesLooked = true;
        // Burning gas and burning things are independent: one missing leaves the other working.
        var type = AccessTools.TypeByName("Assets.Scripts.Objects.AtmosphericFire");
        var data = type == null ? null : AccessTools.Field(type, "_particleData");
        var place = type == null ? null : AccessTools.Property(type, "Position");
        var emitting = type == null ? null : AccessTools.Method(type, "IsEmitting");
        _fireList = type == null ? null : AccessTools.Field(type, "AllAtmosphericFires");
        _fireGate = type == null ? null : AccessTools.Field(type, "AtmosphericFireLookup");
        if (type != null && _fireGate == null) Log("fire lights: AtmosphericFire.AtmosphericFireLookup not found, the fire list is read under its own lock");
        var size = data == null ? null : AccessTools.Field(data.FieldType, "Size");
        var color = data == null ? null : AccessTools.Field(data.FieldType, "Color");
        string gasWhy = type == null ? "The game's class AtmosphericFire was not found."
            : _fireList == null ? "The game's fire list (AllAtmosphericFires) was not found."
            : place == null || emitting == null ? "The game's fires no longer have Position or IsEmitting."
            : data == null || size == null || color == null ? "The game's fires no longer carry their flame size and color."
            : null;
        if (gasWhy != null) { _fireList = null; Log("fire lights: " + gasWhy + " Gas fires stay dark."); }
        else
        {
            _firePlace = Getter<Vector3>(place);
            _fireBurning = Compiled<bool>(type, x => System.Linq.Expressions.Expression.Call(x, emitting), x => (bool)emitting.Invoke(x, null));
            _fireSize = Compiled<float>(type, x => System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Field(x, data), size), x => (float)size.GetValue(data.GetValue(x)));
            _fireColor = Compiled<Color>(type, x => System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Field(x, data), color), x => (Color)color.GetValue(data.GetValue(x)));
        }
        SetFeature("gasfire", gasWhy == null, gasWhy);
        // Things on fire: a helmet, what is held, anything burning (ThingFire, code ThingFire.cs:13). The game gives them full size, so no strength to read.
        var things = AccessTools.TypeByName("Objects.ThingFire");
        var thingEmitting = things == null ? null : AccessTools.PropertyGetter(things, "IsValid");
        var thingPlace = things == null ? null : AccessTools.Property(things, "Position");
        _thingFireList = things == null ? null : AccessTools.Field(things, "AllThingFires");
        string thingWhy = things == null ? "The game's class ThingFire was not found."
            : _thingFireList == null ? "The game's list of burning things (AllThingFires) was not found."
            : thingEmitting == null || thingPlace == null ? "The game's burning things no longer have IsValid or Position."
            : null;
        if (thingWhy != null) { _thingFireList = null; Log("fire lights: " + thingWhy + " Burning things stay dark."); }
        else
        {
            _thingFirePlace = Getter<Vector3>(thingPlace);
            _thingFireBurning = Compiled<bool>(things, x => System.Linq.Expressions.Expression.Call(x, thingEmitting), x => (bool)thingEmitting.Invoke(x, null));
        }
        SetFeature("burning", thingWhy == null, thingWhy);
    }

    // The burning thing nearest the camera within reach (the player's own, in practice) also gets four lamps around it, so the suit is lit from outside.
    const float BurnRingNear = 3f, BurnRingGap = 0.6f, BurnRingShare = 0.6f, BurnRingReach = 1.8f;
    readonly Light[] _burnRing = new Light[4];
    static readonly Vector3[] BurnRingSides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    void UpdateBurnRing(Vector3 at, float flicker, bool lit)
    {
        for (int i = 0; i < _burnRing.Length; i++)
        {
            if (!lit)
            {
                if (_burnRing[i] != null && _burnRing[i].enabled) _burnRing[i].enabled = false;
                continue;
            }
            if (_burnRing[i] == null) _burnRing[i] = NewFillLight("CadStyleFireLight");
            _burnRing[i].transform.position = at + BurnRingSides[i] * BurnRingGap;
            SetColoredLamp(_burnRing[i], FireOrange * (flicker * BurnRingShare * V(S.FirePower)), BurnRingReach);
        }
    }

    // Burning things get lamps first; the flicker is smooth noise, different for each lamp.
    const float ThingFireScore = -1000f;
    const float FlickerSpeed = 9f;

    void UpdateFireLights()
    {
        int most = Mathf.RoundToInt(V(S.FireMax));
        if (most <= 0 || V(S.FirePower) <= 0f) { ClearFireLights(); return; }
        if (!_firesLooked) LookForFires();
        if ((_fireList == null && _thingFireList == null) || _frame % FireEveryFrames != 0) return;
        if (_fireList != null) CopyLocked(_fireList.GetValue(null) as System.Collections.IList, _fireGate == null ? null : _fireGate.GetValue(null), _fireCopy);
        else _fireCopy.Clear();
        var fires = _fireCopy;
        int count = fires.Count, found = 0;
        if (_fireScore.Length < count) { Array.Resize(ref _fireScore, count * 2); Array.Resize(ref _fireIndex, count * 2); }
        var here = _cam.transform.position;
        float gamma = V(S.FireGamma);
        for (int i = 0; i < count; i++)
        {
            var fire = fires[i];
            if (fire == null) continue;
            bool burning;
            try { burning = _fireBurning(fire); }
            catch { continue; }
            if (!burning) continue;
            float strength = Mathf.Pow(Mathf.Clamp01(_fireSize(fire)), gamma);
            if (strength < 0.001f) continue;
            _fireScore[found] = -strength / (1f + (_firePlace(fire) - here).sqrMagnitude * 0.01f);
            _fireIndex[found] = i;
            found++;
        }
        if (_thingFireList != null) CopyLocked(_thingFireList.GetValue(null) as System.Collections.IList, null, _thingCopy); else _thingCopy.Clear();
        var things = _thingCopy;
        int thingCount = things.Count;
        if (_fireScore.Length < count + thingCount) { Array.Resize(ref _fireScore, (count + thingCount) * 2); Array.Resize(ref _fireIndex, (count + thingCount) * 2); }
        for (int i = 0; i < thingCount; i++)
        {
            var fire = things[i];
            if (fire == null) continue;
            bool burning;
            try { burning = _thingFireBurning(fire); }
            catch { continue; }
            if (!burning) continue;
            _fireScore[found] = ThingFireScore - 1f / (1f + (_thingFirePlace(fire) - here).sqrMagnitude);
            _fireIndex[found] = -1 - i;
            found++;
        }
        Array.Sort(_fireScore, _fireIndex, 0, found);
        bool ringLit = false;
        for (int k = 0; k < found && _fireIndex[k] < 0; k++)
        {
            var nearest = _thingFirePlace(things[-1 - _fireIndex[k]]);
            if ((nearest - here).sqrMagnitude > BurnRingNear * BurnRingNear) continue;
            UpdateBurnRing(nearest, 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * FlickerSpeed, 99f), true);
            ringLit = true;
            break;
        }
        if (!ringLit) UpdateBurnRing(Vector3.zero, 0f, false);
        int use = Mathf.Min(most, found);
        while (_fireLamps.Count < use) _fireLamps.Add(NewFillLight("CadStyleFireLight"));
        for (int k = 0; k < _fireLamps.Count; k++)
        {
            var lamp = _fireLamps[k];
            if (lamp == null) continue;
            if (k >= use)
            {
                if (lamp.enabled) lamp.enabled = false;
                continue;
            }
            if (_fireIndex[k] < 0)
            {
                var thing = things[-1 - _fireIndex[k]];
                float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * FlickerSpeed, k * 3.7f);
                lamp.transform.position = _thingFirePlace(thing);
                SetColoredLamp(lamp, FireOrange * (flicker * V(S.FirePower)), FireReach);
                continue;
            }
            var fire = fires[_fireIndex[k]];
            float strength = Mathf.Pow(Mathf.Clamp01(_fireSize(fire)), gamma);
            lamp.transform.position = _firePlace(fire);
            SetColoredLamp(lamp, Color.Lerp(FireOrange, Normalized(_fireColor(fire)), 0.5f) * (strength * V(S.FirePower)), FireReach);
        }
        for (int k = _fireLamps.Count - 1; k >= most; k--)
        {
            if (_fireLamps[k] != null) Destroy(_fireLamps[k].gameObject);
            _fireLamps.RemoveAt(k);
        }
    }

    void ClearFireLights()
    {
        foreach (var lamp in _fireLamps)
            if (lamp != null) Destroy(lamp.gameObject);
        _fireLamps.Clear();
        for (int i = 0; i < _burnRing.Length; i++)
        {
            if (_burnRing[i] != null) Destroy(_burnRing[i].gameObject);
            _burnRing[i] = null;
        }
    }

    void ClearDeviceLights()
    {
        foreach (var d in _deviceLamps) DestroyLamps(d);
        _deviceLamps.Clear();
        _deviceKnown.Clear();
        _deviceChecked.Clear();
        _poolCursor = 0;
        _wantedMask = -1;
    }

    // ---- Timing, the sensor for hitches: any job slower than 8 ms is logged at once (a name at most every 5 s); every 60 s the slowest
    // time of each job and how often the memory cleanup ran. Nothing is allocated per call.
    const float SlowJobMs = 8f;
    readonly Dictionary<string, float> _jobWorst = new Dictionary<string, float>();
    readonly Dictionary<string, float> _jobLogged = new Dictionary<string, float>();
    float _jobReportAt;
    int _gcAtReport = -1;

    static long Clock()
    {
        return System.Diagnostics.Stopwatch.GetTimestamp();
    }

    void Note(string job, long started)
    {
        float ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        float worst;
        if (!_jobWorst.TryGetValue(job, out worst) || ms > worst) _jobWorst[job] = ms;
        float logged;
        if (ms > SlowJobMs && (!_jobLogged.TryGetValue(job, out logged) || Time.unscaledTime - logged > 5f))
        {
            _jobLogged[job] = Time.unscaledTime;
            Log("slow: " + job + " took " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
        }
    }

    void ReportJobs()
    {
        if (Time.unscaledTime < _jobReportAt) return;
        _jobReportAt = Time.unscaledTime + 60f;
        RollLog();
        int gc = GC.CollectionCount(0);
        if (_gcAtReport >= 0 && _jobWorst.Count > 0)
            Log("last minute: memory cleanups " + (gc - _gcAtReport) + ", device lamps " + _deviceLamps.Count + " (" + string.Join(" ", _deviceLamps.GroupBy(x => x.Kind).Select(g => g.Key + " " + g.Count())) + "), fire lamps " + _fireLamps.Count + ", slowest " + string.Join(", ", _jobWorst.Select(kv => kv.Key + " " + kv.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms")));
        _gcAtReport = gc;
        _jobWorst.Clear();
    }

    // The player's own: a worn or held light always sits close to the camera, anyone else's normally doesn't.
    const float FlashlightSearch = 2f;
    Light _flashLamp;
    System.Reflection.FieldInfo _wearables;
    System.Reflection.PropertyInfo _wearableOn;
    Func<object, bool> _wearableIsOn;
    bool _wearablesLooked;

    void UpdateFlashlight()
    {
        if (V(S.FlashPower) <= 0f) { ClearFlashlight(); return; }
        if (!_wearablesLooked) LookForWearables();
        if (_wearables == null) return;
        var list = _wearables.GetValue(null) as System.Collections.IList;
        if (list == null) return;
        var here = _cam.transform.position;
        Component best = null;
        float bestDistance = FlashlightSearch * FlashlightSearch;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i] as Component;
            if (item == null) continue;
            float distance = (item.transform.position - here).sqrMagnitude;
            if (distance >= bestDistance || !_wearableIsOn(item)) continue;
            best = item;
            bestDistance = distance;
        }
        if (best == null)
        {
            if (_flashLamp != null && _flashLamp.enabled) _flashLamp.enabled = false;
            return;
        }
        if (_flashLamp == null) _flashLamp = NewFillLight("CadStyleFlashlightLight");
        var own = best.GetComponentInChildren<Light>(true);
        _flashLamp.transform.position = own != null ? own.transform.position : best.transform.position;
        _flashLamp.intensity = V(S.FlashPower);
        _flashLamp.range = V(S.FlashReach);
        if (!_flashLamp.enabled) _flashLamp.enabled = true;
    }

    void LookForWearables()
    {
        _wearablesLooked = true;
        var thing = AccessTools.TypeByName("Assets.Scripts.Objects.Thing");
        var wearable = AccessTools.TypeByName("Assets.Scripts.Objects.Items.IWearableLight");
        _wearables = thing == null ? null : AccessTools.Field(thing, "AllIWearableLights");
        _wearableOn = wearable == null ? null : AccessTools.Property(wearable, "OnOff");
        string why = _wearables == null ? "The game's list of worn lights (Thing.AllIWearableLights) was not found."
            : _wearableOn == null ? "The game's worn lights no longer have OnOff." : null;
        if (why != null) { _wearables = null; Log("flashlight light: " + why + " It stays off."); }
        else _wearableIsOn = Getter<bool>(_wearableOn);
        SetFeature("flashlight", why == null, why);
    }

    void ClearFlashlight()
    {
        if (_flashLamp != null) Destroy(_flashLamp.gameObject);
        _flashLamp = null;
    }

    void UpdateAdaptation()
    {
        if (_lines == null) return;
        float top = _b[(int)B.AdaptOn] ? V(S.AdaptGamma) : 0f;
        _lines.Adapt = top > 0f;
        if (top <= 0f)
        {
            _adaptGamma = 1f;
            _lines.Gamma = 1f;
            return;
        }
        float light = _lines.MeasuredLight;
        if (light < 0f) return;
        float bright = Mathf.InverseLerp(AdaptLogMin, AdaptLogMax, Mathf.Log(Mathf.Max(light * V(S.AdaptSensitivity), 0.0001f), 2f));
        float target = 1f + top * bright;
        float seconds = target > _adaptGamma ? V(S.AdaptDown) : V(S.AdaptUp);
        _adaptGamma = Mathf.Lerp(_adaptGamma, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(seconds, 0.01f)));
        _lines.Gamma = _adaptGamma;
    }

    // ---- Lamp shadow fix: the game's lighting shader with only its shadowed point lamp programs replaced. ---------------
    // With the look off the game's own shader is back in place, so nothing of this runs.
    // Cell test: shadow map cells whose stored point lies on the lit surface's own plane, within this many meters, are ignored. At 0 the shader reads as in v0.0.15.
    Shader _lampShader;
    bool _lampFixOn;
    BuiltinShaderMode _lampModeBefore;

    void LoadLampShader()
    {
        const string name = "cadlamps_dx11.bundle";
        // The lamp shader is the game engine's own lighting shader with one part changed, copied at build time from this engine version
        // (ENGINE in build_look.py). After a game update to another engine the copy may no longer fit, so the game's own lighting stays.
        if (Application.unityVersion != BuiltForEngine)
        {
            Log("lamp fix unavailable: built for engine " + BuiltForEngine + ", the game now runs " + Application.unityVersion + "; the game's own lighting stays");
            _lampWhy = "Made for engine " + BuiltForEngine + ", the game now runs " + Application.unityVersion + ". The game's own lighting is used.";
            return;
        }
        try
        {
            var b = AssetBundle.LoadFromFile(Path.Combine(_dir, "Content", name));
            if (b == null) { Log(name + ": LoadFromFile returned null, lamp fix unavailable"); _lampWhy = "The file " + name + " did not load. The game's own lighting is used."; return; }
            var s = b.LoadAllAssets<Shader>().FirstOrDefault();
            Log(name + ": shader " + (s == null ? "missing" : "'" + s.name + "' supported=" + s.isSupported + " passes=" + s.passCount));
            if (s != null && s.isSupported && s.passCount == 2) _lampShader = s;
            else
            {
                Log("lamp fix unavailable, the game's lighting stays");
                _lampWhy = (s == null ? "The file " + name + " holds no shader." : !s.isSupported ? "This graphics mode can't run it (made for DirectX 11)." : "The shader has an unexpected form.") + " The game's own lighting is used.";
            }
        }
        catch (Exception e) { Log(name + ": " + e); _lampWhy = "Loading it failed: " + e.Message; }
    }

    // The engine version the shader files were built from (ENGINE in build_look.py).
    const string BuiltForEngine = "2022.3.62f3";

    void SetLampFix(bool on)
    {
        if (_lampShader == null) on = false;
        if (on == _lampFixOn) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (on)
            {
                _lampModeBefore = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading);
                GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, _lampShader);
                GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseCustom);
            }
            else GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, _lampModeBefore);
            _lampFixOn = on;
            if (!on)
            {
                try { GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, null); }
                catch (Exception e) { Log("lamp fix: clearing the custom shader failed: " + e.Message); }
            }
            var now = GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading);
            Log("lamp fix " + (on ? "on" : "off") + ": mode was " + _lampModeBefore + ", now " + GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading) + ", custom shader " + (now == null ? "none" : now.name) + ", took " + clock.ElapsedMilliseconds + " ms");
        }
        catch (Exception e)
        {
            Log("lamp fix failed, the game's lighting stays: " + e);
            _lampShader = null;
            _lampWhy = "Switching it on failed: " + e.Message + " The game's own lighting is used.";
            SetFeature("lampfix", false, _lampWhy);
            _lampFixOn = false;
            try { GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseBuiltin); } catch { }
        }
    }

    // ---- Lamp spots: only the lamps' own flares, by their object name. The sun has its own and is never touched. --------
    readonly HashSet<LensFlare> _spotsOff = new HashSet<LensFlare>();

    void ApplyLampSpots(bool off)
    {
        if (!off && _spotsOff.Count == 0) return;
        if (off)
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<LensFlare>())
            {
                if (f == null || f.gameObject.name != "lodFlare" || !f.enabled || IsGameTemplate(f)) continue;
                f.enabled = false;
                _spotsOff.Add(f);
            }
            if (_spotsOff.Count == 0 && InWorld && !_flaresMissingLogged) { _flaresMissingLogged = true; Log("lamp spots: no lens flares named lodFlare found, nothing to switch off"); }
            return;
        }
        foreach (var f in _spotsOff)
            if (f != null) f.enabled = true;
        _spotsOff.Clear();
    }

    // ---- Cost meter: look on and off in turns, without vsync, so frame times show the real cost. ------------------------
    const float MeasureSettle = 1f, MeasureSample = 2f;
    const int MeasureSpans = 6;
    const int ResultsShown = 5;
    int _measureSpan = -1;
    float _measureSpanAt;
    int _vsyncBefore, _targetFpsBefore;
    readonly List<float>[] _measureTimes = { new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>() };
    readonly List<string> _measureResults = new List<string>();

    string BenchmarkPath { get { return Path.Combine(_dir, "benchmark_results.csv"); } }

    void StartMeasure()
    {
        if (_measureSpan >= 0 || !_active) return;
        _vsyncBefore = QualitySettings.vSyncCount;
        _targetFpsBefore = Application.targetFrameRate;
        foreach (var list in _measureTimes) list.Clear();
        _measureSpan = 0;
        _measureSpanAt = Time.unscaledTime;
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        var sorted = new List<float>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    void UpdateMeasure()
    {
        if (_measureSpan < 0) return;
        // The game may set these again at any time.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        float inSpan = Time.unscaledTime - _measureSpanAt;
        if (inSpan > MeasureSettle) _measureTimes[_measureSpan].Add(Time.unscaledDeltaTime * 1000f);
        if (inSpan < MeasureSettle + MeasureSample) return;
        _measureSpan++;
        _measureSpanAt = Time.unscaledTime;
        if (_measureSpan < MeasureSpans) { SetActive(_measureSpan % 2 == 0); return; }
        _measureSpan = -1;
        SetActive(true);
        QualitySettings.vSyncCount = _vsyncBefore;
        Application.targetFrameRate = _targetFpsBefore;
        FinishMeasure();
    }

    void FinishMeasure()
    {
        var on = new List<float>();
        var off = new List<float>();
        for (int i = 0; i < MeasureSpans; i++) (i % 2 == 0 ? on : off).AddRange(_measureTimes[i]);
        float onMs = Median(on), offMs = Median(off), change = onMs - offMs;
        string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string summary = string.Format(CultureInfo.InvariantCulture, "{0} UTC: mod {1} {2:0.0} ms per frame (on {3:0.0} ms = {4:0} fps; off {5:0.0} ms = {6:0} fps)",
            stamp, change >= 0f ? "costs" : "saves", Mathf.Abs(change), onMs, onMs > 0f ? 1000f / onMs : 0f, offMs, offMs > 0f ? 1000f / offMs : 0f);
        var spans = string.Join(";", _measureTimes.Select(t => Median(t).ToString("0.00", CultureInfo.InvariantCulture)));
        var counts = string.Join(";", _measureTimes.Select(t => t.Count.ToString(CultureInfo.InvariantCulture)));
        var values = string.Join(";", Sliders.Select((d, i) => d.Key + "=" + _v[i].ToString("0.#####", CultureInfo.InvariantCulture))
            .Concat(ToggleKeys.Select((k, i) => k + "=" + (_b[i] ? "1" : "0"))));
        string record = stamp + "," + Csv(BuildStamp) + "," + Screen.width + "x" + Screen.height + "," + Screen.currentResolution.refreshRateRatio.value.ToString("0.##", CultureInfo.InvariantCulture)
            + "," + _vsyncBefore + "," + _targetFpsBefore + "," + onMs.ToString("0.00", CultureInfo.InvariantCulture) + "," + offMs.ToString("0.00", CultureInfo.InvariantCulture)
            + "," + spans + "," + counts + "," + values + "," + Csv(summary);
        _measureResults.Add(summary);
        if (_measureResults.Count > ResultsShown) _measureResults.RemoveAt(0);
        Log("cost meter: " + summary);
        try { AppendBenchmark(record); }
        catch (Exception e) { Log("benchmark save failed: " + e.Message); }
    }

    // A field holding a comma or quote goes in quotes (the build stamp has a comma; until v0.0.34 it shifted the columns).
    static string Csv(string field)
    {
        return field.IndexOfAny(new[] { ',', '"' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    // Append-only: a new file gets only its header, and each record writes its line break first, so a broken write can only harm itself.
    void AppendBenchmark(string record)
    {
        const string Header = "utc,build,screen,refresh_hz,vsync_setting,fps_target_setting,look_on_ms,look_off_ms,span_ms_on_off_in_turns,span_frames,settings,summary";
        bool isNew = !File.Exists(BenchmarkPath);
        var bytes = new UTF8Encoding(false).GetBytes((isNew ? Header : "") + "\n" + record);
        using (var fs = new FileStream(BenchmarkPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
    }

    void LoadBenchmarks()
    {
        _measureResults.Clear();
        try
        {
            if (!File.Exists(BenchmarkPath)) return;
            foreach (var line in File.ReadAllLines(BenchmarkPath).Skip(1).Reverse().Take(ResultsShown).Reverse())
            {
                int comma = line.LastIndexOf(',');
                if (comma >= 0) _measureResults.Add(line.Substring(comma + 1).Trim('"'));
            }
        }
        catch (Exception e) { Log("benchmarks read failed: " + e.Message); }
    }

    // ---- Texture fade: copies exist only while the slider is above 0. ----------------------------------------------------
    static readonly string[] FadeSkippedShaders = { "UI/", "GUI/", "TextMeshPro", "Hidden/", "Sprites/", "Particles/", "Legacy Shaders/Particles", "Skybox/" };
    static readonly string[] FadeStrengths = { "_BumpScale", "_OcclusionStrength", "_DetailNormalMapScale" };
    const int FadeBakesPerFrame = 8;
    const int FadeMaxSide = 512;
    const float FadeTopWithLines = 0.94f;           // a little color has to stay for the lines to find
    const float FadeSmallestColorStep = 0.006f;     // about what the G-buffer's 8 bits can still show

    class FadeEntry
    {
        public Texture Source, Output;
        public int Mode = -1;       // -1 not looked at yet, 0 palette, 1 to its average, 2 left alone
        public int Lod;
        public bool Linear;
        public Color Average;
        public readonly List<KeyValuePair<Material, string>> Users = new List<KeyValuePair<Material, string>>();
    }

    static readonly string[] FadeSlots = { "_MainTex", "_EmissionMap" };
    readonly Dictionary<Texture, FadeEntry> _fade = new Dictionary<Texture, FadeEntry>();
    readonly Dictionary<Material, float[]> _fadeStrengths = new Dictionary<Material, float[]>();
    readonly Queue<FadeEntry> _fadeQueue = new Queue<FadeEntry>();
    Texture2D _fadeParams;
    float _fadeWanted, _fadeWantedAt, _fadeBaked;
    bool _fadeOn;

    // The grading only works on faded copies, so with Texture fade at 0 it costs nothing and changes nothing.
    string _gradeWanted = "", _gradeBaked = "";

    bool GradeNeutral()
    {
        return V(S.GradeBlack) == 0f && V(S.GradeWhite) == 0f && V(S.GradeGamma) == 1f && V(S.GradeSaturation) == 1f;
    }

    string GradeKey()
    {
        return GradeNeutral() ? "" : string.Join("|", new[] { S.GradeBlack, S.GradeWhite, S.GradeGamma, S.GradeSaturation, S.GradeKeep, S.GradeKeepG, S.GradeKeepB }.Select(k => V(k).ToString("R", CultureInfo.InvariantCulture)));
    }

    void UpdateFade()
    {
        float t = V(S.TextureFade);
        if (t <= 0f) { if (_fadeOn) RestoreFade(); return; }
        if (V(S.KeepColorLines) > 0f) t = Mathf.Min(t, FadeTopWithLines);
        string grade = GradeKey();
        if (t != _fadeWanted || grade != _gradeWanted) { _fadeWanted = t; _gradeWanted = grade; _fadeWantedAt = Time.unscaledTime; }
        if (!_fadeOn || _frame % 300 == 0) { long started = Clock(); ScanFade(); Note("texture fade scan", started); }
        if ((t != _fadeBaked || grade != _gradeBaked) && Time.unscaledTime - _fadeWantedAt > 0.15f)
        {
            _fadeBaked = t;
            _gradeBaked = grade;
            _fadeQueue.Clear();
            foreach (var e in _fade.Values) _fadeQueue.Enqueue(e);
            foreach (var kv in _fadeStrengths)
            {
                if (kv.Key == null) continue;
                for (int i = 0; i < FadeStrengths.Length; i++)
                    if (kv.Key.HasProperty(FadeStrengths[i])) kv.Key.SetFloat(FadeStrengths[i], kv.Value[i] * (1f - t));
            }
        }
        bool hadWork = _fadeQueue.Count > 0;
        for (int i = 0; i < FadeBakesPerFrame && _fadeQueue.Count > 0; i++) BakeFade(_fadeQueue.Dequeue());
        if (hadWork && _fadeQueue.Count == 0) LogFadeSummary();
    }

    void LogFadeSummary()
    {
        int palettes = 0, averaged = 0, left = 0;
        long bytes = 0;
        foreach (var e in _fade.Values)
        {
            if (e.Mode == 0) palettes++; else if (e.Mode == 1) averaged++; else left++;
            if (e.Output != null)
                bytes += (long)(e.Output.width * e.Output.height * 4 * 1.34f * (e.Output is Texture2DArray ? ((Texture2DArray)e.Output).depth : 1));
        }
        Log("texture fade " + _fadeBaked.ToString("0.00", CultureInfo.InvariantCulture) + ": palettes " + palettes + ", averaged " + averaged + ", left alone " + left + ", materials " + _fadeStrengths.Count + ", extra video memory about " + (bytes / 1048576) + " MB");
    }

    void ScanFade()
    {
        _fadeOn = true;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || m.shader == null || IsTerrain(m) || m.renderQueue >= 2500 || !m.HasProperty("_Glossiness") || !m.HasProperty("_MainTex")) continue;
            string shader = m.shader.name;
            if (FadeSkippedShaders.Any(p => shader.StartsWith(p))) continue;
            if (!_fadeStrengths.ContainsKey(m))
            {
                var saved = new float[FadeStrengths.Length];
                for (int i = 0; i < saved.Length; i++)
                {
                    saved[i] = m.HasProperty(FadeStrengths[i]) ? m.GetFloat(FadeStrengths[i]) : 1f;
                    if (m.HasProperty(FadeStrengths[i])) m.SetFloat(FadeStrengths[i], saved[i] * (1f - _fadeBaked));
                }
                _fadeStrengths[m] = saved;
            }
            foreach (var slot in FadeSlots)
            {
                if (!m.HasProperty(slot)) continue;
                var tex = m.GetTexture(slot);
                if (tex == null || _fadeOutputs.Contains(tex)) continue;
                FadeEntry entry;
                if (!_fade.TryGetValue(tex, out entry))
                {
                    entry = new FadeEntry { Source = tex };
                    _fade[tex] = entry;
                    if (_fadeBaked > 0f) _fadeQueue.Enqueue(entry);
                }
                var user = new KeyValuePair<Material, string>(m, slot);
                if (!entry.Users.Contains(user)) entry.Users.Add(user);
                if (entry.Output != null) m.SetTexture(slot, entry.Output);
            }
        }
    }

    readonly HashSet<Texture> _fadeOutputs = new HashSet<Texture>();

    // Palettes by name; for everything else a small picture gives the average color.
    void ClassifyFade(FadeEntry e)
    {
        e.Mode = 2;
        e.Linear = !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(e.Source.graphicsFormat);
        if (e.Source is Texture2DArray)
        {
            if (e.Source.name.StartsWith("ColorPalette")) e.Mode = 0;
            return;
        }
        if (!(e.Source is Texture2D)) return;
        if (e.Source.name.StartsWith("color_")) { e.Mode = 0; return; }
        const int S2 = 64;
        var rt = RenderTexture.GetTemporary(S2, S2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var keep = RenderTexture.active;
        Graphics.Blit(e.Source, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(S2, S2, TextureFormat.RGBA32, false, false);
        read.ReadPixels(new Rect(0, 0, S2, S2), 0, 0);
        RenderTexture.active = keep;
        RenderTexture.ReleaseTemporary(rt);
        var p = read.GetPixels32();
        Destroy(read);
        float mr = 0f, mg = 0f, mb = 0f;
        foreach (var c in p) { mr += c.r; mg += c.g; mb += c.b; }
        mr /= p.Length; mg /= p.Length; mb /= p.Length;
        e.Mode = 1;
        var avg = new Color(mr / 255f, mg / 255f, mb / 255f);
        e.Average = e.Linear ? avg : avg.linear;
    }

    void BakeFade(FadeEntry e)
    {
        if (e.Source == null) return;
        if (e.Mode < 0) ClassifyFade(e);
        if (e.Mode == 2) return;
        if (_fadeParams == null) _fadeParams = new Texture2D(5, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        var array = e.Source as Texture2DArray;
        if (e.Output == null)
        {
            e.Lod = 0;
            if (e.Mode == 1)
                while (Mathf.Max(e.Source.width >> e.Lod, e.Source.height >> e.Lod) > FadeMaxSide && e.Lod + 1 < e.Source.mipmapCount) e.Lod++;
            int w = Mathf.Max(1, e.Source.width >> e.Lod), h = Mathf.Max(1, e.Source.height >> e.Lod);
            if (array != null)
            {
                var o = new Texture2DArray(w, h, array.depth, TextureFormat.RGBA32, true, e.Linear) { hideFlags = HideFlags.HideAndDontSave };
                o.Apply(false, true);
                e.Output = o;
            }
            else
            {
                var o = new Texture2D(w, h, TextureFormat.RGBA32, true, e.Linear) { hideFlags = HideFlags.HideAndDontSave };
                o.Apply(false, true);
                e.Output = o;
            }
            e.Output.filterMode = e.Source.filterMode;
            e.Output.wrapMode = e.Source.wrapMode;
            e.Output.anisoLevel = e.Source.anisoLevel;
            _fadeOutputs.Add(e.Output);
        }
        var desc = new RenderTextureDescriptor(e.Output.width, e.Output.height, RenderTextureFormat.ARGB32, 0) { useMipMap = true, autoGenerateMips = false, sRGB = !e.Linear };
        var keep = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(desc);
        Shader.SetGlobalTexture("_OLFadeSrc", e.Source);
        Shader.SetGlobalTexture("_OLFadeP", _fadeParams);
        int slices = array != null ? array.depth : 1;
        for (int slice = 0; slice < slices; slice++)
        {
            _fadeParams.SetPixel(0, 0, new Color(_fadeBaked, e.Mode, e.Lod, slice));
            _fadeParams.SetPixel(1, 0, new Color(e.Output.width, e.Output.height, 0f, 0f));
            _fadeParams.SetPixel(2, 0, e.Average);
            _fadeParams.SetPixel(3, 0, new Color(V(S.GradeBlack), V(S.GradeWhite), V(S.GradeGamma), V(S.GradeSaturation)));
            _fadeParams.SetPixel(4, 0, new Color(V(S.GradeKeep), e.Linear || GradeNeutral() ? 0f : 1f, V(S.GradeKeepG), V(S.GradeKeepB)));
            _fadeParams.Apply(false);
            Graphics.SetRenderTarget(rt);
            _mat.SetPass(array != null ? 11 : 10);
            Graphics.DrawMeshNow(_quad, Matrix4x4.identity);
            rt.GenerateMips();
            int mips = Mathf.Min(rt.mipmapCount, e.Output.mipmapCount);
            for (int mip = 0; mip < mips; mip++) Graphics.CopyTexture(rt, 0, mip, e.Output, slice, mip);
        }
        RenderTexture.active = keep;
        RenderTexture.ReleaseTemporary(rt);
        foreach (var u in e.Users)
            if (u.Key != null && u.Key.GetTexture(u.Value) == e.Source) u.Key.SetTexture(u.Value, e.Output);
    }

    // Only materials still showing our copy get the original back; the game may have set something else since.
    void RestoreFade()
    {
        foreach (var e in _fade.Values)
        {
            foreach (var u in e.Users)
                if (u.Key != null && e.Output != null && u.Key.GetTexture(u.Value) == e.Output) u.Key.SetTexture(u.Value, e.Source);
            if (e.Output != null) Destroy(e.Output);
        }
        foreach (var kv in _fadeStrengths)
        {
            if (kv.Key == null) continue;
            for (int i = 0; i < FadeStrengths.Length; i++)
                if (kv.Key.HasProperty(FadeStrengths[i])) kv.Key.SetFloat(FadeStrengths[i], kv.Value[i]);
        }
        _fade.Clear();
        _fadeOutputs.Clear();
        _fadeStrengths.Clear();
        _fadeQueue.Clear();
        _fadeBaked = 0f;
        _fadeWanted = 0f;
        _fadeOn = false;
        Log("texture fade off, copies released");
    }

    void BuildBuffers()
    {
        if (_gbufferBuffer != null) _cam.RemoveCommandBuffer(CameraEvent.BeforeReflections, _gbufferBuffer);
        if (_releaseBuffer != null) _cam.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _releaseBuffer);
        bool glow = V(S.GlowStrength) > 0f;
        int t0 = Shader.PropertyToID("_OLGB0"), t1 = Shader.PropertyToID("_OLGB1"), t2 = Shader.PropertyToID("_OLGB2"), flat = Shader.PropertyToID("_OLFlat");
        var g = new CommandBuffer { name = "CadStyle gbuffer" };
        g.GetTemporaryRT(t0, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        g.GetTemporaryRT(t1, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        g.GetTemporaryRT(t2, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
        g.GetTemporaryRT(flat, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
        g.Blit(BuiltinRenderTextureType.GBuffer0, t0);
        g.Blit(BuiltinRenderTextureType.GBuffer1, t1);
        g.Blit(BuiltinRenderTextureType.GBuffer2, t2);
        g.SetRenderTarget(new RenderTargetIdentifier[] { BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1, BuiltinRenderTextureType.GBuffer2, flat }, BuiltinRenderTextureType.CameraTarget);
        g.DrawMesh(_quad, Matrix4x4.identity, _mat, 0, 0);
        g.ReleaseTemporaryRT(t0);
        g.ReleaseTemporaryRT(t1);
        g.ReleaseTemporaryRT(t2);
        int glowFirst = Shader.PropertyToID("_OLGlowA");
        if (glow)
        {
            // Lamps' own light before any lighting, so a white wall lit by them doesn't glow. An exact copy, because the frame can't be read while it is the target.
            int emission = Shader.PropertyToID("_OLGlowSrc");
            g.GetTemporaryRT(emission, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            g.GetTemporaryRT(glowFirst, -2, -2, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            g.CopyTexture(BuiltinRenderTextureType.CameraTarget, emission);
            g.SetRenderTarget(glowFirst);
            g.DrawMesh(_quad, Matrix4x4.identity, _mat, 0, 6);
            g.ReleaseTemporaryRT(emission);
        }
        // The frame holds ambient and emission at this point; reflections and lamps are added after it.
        g.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        g.DrawMesh(_quad, Matrix4x4.identity, _mat, 0, 3);
        _cam.AddCommandBuffer(CameraEvent.BeforeReflections, g);
        _gbufferBuffer = g;
        // The flat normals and the glow's first level stay alive for the lines pass, which runs between these two events.
        var r = new CommandBuffer { name = "CadStyle release" };
        r.ReleaseTemporaryRT(flat);
        if (glow) r.ReleaseTemporaryRT(glowFirst);
        _cam.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, r);
        _releaseBuffer = r;
        _builtGlow = glow;
        if (_lines != null) _lines.Glow = glow;
        Log("white glow " + (glow ? "on" : "off"));
    }


    void RestoreTerrain()
    {
        foreach (var m in _terrain)
            if (m.Key != null)
                foreach (var t in m.Value) m.Key.SetTexture(t.Key, t.Value);
        _terrain.Clear();
    }

    Texture2D Solid(Texture source)
    {
        Texture2D solid;
        if (_solidCache.TryGetValue(source, out solid) && solid != null) return solid;
        var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var keep = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
        read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
        read.Apply(false, false);
        RenderTexture.active = keep;
        RenderTexture.ReleaseTemporary(rt);
        long r = 0, g = 0, b = 0, a = 0;
        var px = read.GetPixels32();
        foreach (var p in px) { r += p.r; g += p.g; b += p.b; a += p.a; }
        Destroy(read);
        var c = new Color32((byte)(r / px.Length), (byte)(g / px.Length), (byte)(b / px.Length), (byte)(a / px.Length));
        solid = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
        solid.SetPixels32(new[] { c, c, c, c });
        solid.Apply(false, false);
        _solidCache[source] = solid;
        _ownSolids.Add(solid);
        return solid;
    }

    void PreRender(Camera c)
    {
        if (c != _cam || _params == null) return;
        if (!_active)
        {
            // F9 off, only the dither may still run and needs its one row.
            if (_ditherFx == null) return;
            _params.SetPixel(17, 0, AdaptRow());
            _params.Apply(false);
            Shader.SetGlobalTexture("_OLParams", _params);
            return;
        }
        float n = c.nearClipPlane, f = c.farClipPlane;
        float x, y;
        if (SystemInfo.usesReversedZBuffer) { x = -1f + f / n; y = 1f; }
        else { x = 1f - f / n; y = f / n; }
        float tanV = Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var v2w = c.cameraToWorldMatrix;
        _params.SetPixel(0, 0, new Color(x, y, x / f, y / f));
        // On DX11 the camera's buffers are stored upside-down: pixel row 0 is the bottom of the view.
        float rowSign = SystemInfo.graphicsUVStartsAtTop ? -1f : 1f;
        _params.SetPixel(1, 0, new Color(tanV * c.aspect, tanV * rowSign, 1f / c.pixelWidth, 1f / c.pixelHeight));
        _params.SetPixel(2, 0, (Color)v2w.GetRow(0));
        _params.SetPixel(3, 0, (Color)v2w.GetRow(1));
        _params.SetPixel(4, 0, (Color)v2w.GetRow(2));
        const float fadeStart = LineFadeStart, fadeEnd = LineFadeEnd;
        _params.SetPixel(5, 0, new Color(Mathf.Cos(V(S.CreaseAngle) * Mathf.Deg2Rad), V(S.DepthStep), Mathf.Max(V(S.ColorStep) * (1f - V(S.KeepColorLines) * _fadeBaked), Mathf.Min(V(S.ColorStep), FadeSmallestColorStep)), V(S.LineOpacity)));
        _params.SetPixel(6, 0, new Color(V(S.LineWidth), fadeStart, fadeEnd, f * 0.98f));
        float az = V(S.FacetAzimuth) * Mathf.Deg2Rad, el = V(S.FacetElevation) * Mathf.Deg2Rad;
        _params.SetPixel(7, 0, new Color(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az), V(S.FacetShade)));
        _params.SetPixel(8, 0, new Color(V(S.AmbientKeep), V(S.SmoothForms), V(S.Posterize), V(S.Bands)));
        _params.SetPixel(9, 0, new Color(V(S.BandTop), V(S.BandFloor), 0f, 0f));
        // Last frame's view, for following the line history through camera motion. Relative to the camera, so large world coordinates stay exact.
        var w2v = c.worldToCameraMatrix;
        var camPos = c.transform.position;
        if (!_hasPrevCam) { _hasPrevCam = true; _prevW2V = w2v; _prevCamPos = camPos; }
        var moved = camPos - _prevCamPos;
        for (int row = 0; row < 3; row++)
        {
            var r = _prevW2V.GetRow(row);
            _params.SetPixel(10 + row, 0, new Color(r.x, r.y, r.z, moved[row]));
        }
        float keep = V(S.LineStability);
        // Blended, a steady line can sit at half strength on two pixels. Lifting it keeps lines as black as without blending.
        _params.SetPixel(13, 0, new Color(1f + Mathf.Clamp01(keep * 2f), keep, 0f, 0f));
        // The weight of each lower level. Past Radius 1 and 5 passes the sum stays frozen, so going further only adds wide light and the lamp's core keeps its strength.
        float glowStep = 0.35f + 0.75f * V(S.GlowRadius), glowTotal = 0f, glowWeight = 1f;
        int glowPasses = Mathf.RoundToInt(V(S.GlowPasses));
        for (int level = 0; level < Mathf.Min(glowPasses, 5); level++) { glowTotal += glowWeight; glowWeight *= Mathf.Min(glowStep, 1.1f); }
        _params.SetPixel(14, 0, new Color(V(S.GlowStrength) * 4f / glowTotal, glowStep, 0f, 0f));
        _params.SetPixel(15, 0, new Color(GlowThreshold, V(S.GlowColor), 0f, 0f));
        _params.SetPixel(16, 0, new Color(LampFixLimit, 0f, LampCellTest, 0f));
        _params.SetPixel(17, 0, AdaptRow());
        _params.SetPixel(18, 0, new Color(V(S.BoostR), V(S.BoostG), V(S.BoostB), 0f));
        _prevW2V = w2v;
        _prevCamPos = camPos;
        _params.Apply(false);
        Shader.SetGlobalTexture("_OLParams", _params);
        if (_lines != null)
        {
            _lines.DrawLines = V(S.LineWidth) > 0f && V(S.LineOpacity) > 0f;
            _lines.DrawShade = V(S.FacetShade) > 0f || V(S.Posterize) > 0f;
            _lines.Stabilize = keep > 0f;
            _lines.GlowLevels = glowPasses;
        }
    }

    // ---- Presets: click loads, hold saves. ---------------------------------------------------------------------------
    const int PresetCount = 4;
    const float ClickSeconds = 0.25f, HoldSeconds = 1f;
    readonly float[][] _slotV = new float[PresetCount][];
    readonly bool[][] _slotB = new bool[PresetCount][];
    readonly Rect[] _slotRects = new Rect[PresetCount];
    int _activeSlot = -1;
    int _pressSlot = -1;
    float _pressAt;
    bool _holdDone;
    int _savedSlot = -1;
    float _savedAt;
    enum UndoKind { None, Defaults, Load, Save, Delete }
    UndoKind _undoKind;
    int _undoActive = -1, _undoSlot;
    string _undoFileText;

    string PresetPath(int slot) { return Path.Combine(_dir, "look_preset_" + (slot + 1) + ".txt"); }

    // Sliders added after a preset was saved take their defaults, so an old preset stays loadable.
    void ParseValues(string[] lines, float[] v, bool[] b, bool readActive)
    {
        foreach (var line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
            int si = Array.FindIndex(Sliders, d => d.Key == key);
            float f;
            if (si >= 0 && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f))
                v[si] = Mathf.Clamp(f, Sliders[si].Min, Sliders[si].Max);
            int bi = Array.IndexOf(ToggleKeys, key);
            if (bi >= 0) b[bi] = val == "1";
            int slot;
            if (readActive && key == "active_preset" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot >= 1 && slot <= PresetCount)
                _activeSlot = slot - 1;
            int opens;
            if (readActive && key == "window_opens" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out opens) && opens >= 0 && opens < WindowOpensLabels.Length) _windowOpens = opens;
            if (readActive && key == "pause_on_open") _pauseOnOpen = val == "1";
            if (readActive && key == "status_seen") _statusSeen = val;
        }
    }

    void ReadPreset(int slot)
    {
        _slotV[slot] = null;
        _slotB[slot] = null;
        try
        {
            string path = PresetPath(slot);
            if (!File.Exists(path)) return;
            var v = new float[_v.Length];
            var b = new bool[_b.Length];
            for (int i = 0; i < v.Length; i++) v[i] = Sliders[i].Default;
            for (int i = 0; i < b.Length; i++) b[i] = ToggleDefaults[i];
            ParseValues(File.ReadAllLines(path), v, b, false);
            _slotV[slot] = v;
            _slotB[slot] = b;
        }
        catch (Exception e) { Log("preset " + (slot + 1) + " read failed: " + e.Message); }
    }

    void TakeUndo(UndoKind kind)
    {
        _vBeforeReset = (float[])_v.Clone();
        _bBeforeReset = (bool[])_b.Clone();
        _undoActive = _activeSlot;
        _undoKind = kind;
    }

    void LoadPreset(int slot)
    {
        if (_slotV[slot] == null) return;
        TakeUndo(UndoKind.Load);
        Array.Copy(_slotV[slot], _v, _v.Length);
        Array.Copy(_slotB[slot], _b, _b.Length);
        _activeSlot = slot;
        MarkChanged();
        ApplyToggles();
        Log("preset " + (slot + 1) + " loaded");
    }

    void SavePreset(int slot)
    {
        try
        {
            string path = PresetPath(slot);
            string before = File.Exists(path) ? File.ReadAllText(path) : null;
            WriteSafely(path, ValuesText());
            ReadPreset(slot);
            TakeUndo(UndoKind.Save);
            _undoSlot = slot;
            _undoFileText = before;
            _activeSlot = slot;
            _savedSlot = slot;
            _savedAt = Time.unscaledTime;
            MarkChanged();
            Log("preset " + (slot + 1) + " saved");
        }
        catch (Exception e) { Log("preset " + (slot + 1) + " save failed: " + e.Message); }
    }

    void UndoLast()
    {
        if (_undoKind == UndoKind.Save || _undoKind == UndoKind.Delete)
        {
            try
            {
                string path = PresetPath(_undoSlot);
                if (_undoKind == UndoKind.Delete && File.Exists(DeletedPath(_undoSlot)) && !File.Exists(path)) Replace(DeletedPath(_undoSlot), path);
                else if (_undoFileText == null) File.Delete(path);
                else WriteSafely(path, _undoFileText);
                ReadPreset(_undoSlot);
                _activeSlot = _undoActive;
                Log("preset " + (_undoSlot + 1) + (_undoKind == UndoKind.Delete ? " delete undone" : " save undone"));
            }
            catch (Exception e) { Log("preset undo failed, it stays undoable: " + e.Message); return; }
        }
        else
        {
            Array.Copy(_vBeforeReset, _v, _v.Length);
            Array.Copy(_bBeforeReset, _b, _b.Length);
            _activeSlot = _undoActive;
            ApplyToggles();
        }
        _vBeforeReset = null;
        _bBeforeReset = null;
        _undoFileText = null;
        _undoKind = UndoKind.None;
        MarkChanged();
    }

    bool MatchesSlot(int slot)
    {
        if (_slotV[slot] == null) return false;
        for (int i = 0; i < _v.Length; i++) if (_v[i] != _slotV[slot][i]) return false;
        for (int i = 0; i < _b.Length; i++) if (_b[i] != _slotB[slot][i]) return false;
        return true;
    }

    // ---- Presets: click loads, hold 1 s saves; with Delete armed, holding 3 s deletes. -----------------------------------
    const float DeleteSeconds = 3f;
    bool _layoutUndo, _layoutMeasured;
    static readonly string Dot = ((char)0xB7).ToString();   // middle dot, kept out of the source as a character
    bool _deleteArmed;
    int _deletedSlot = -1;
    float _deletedAt;

    void FinishPresetPress()
    {
        int slot = _pressSlot;
        bool click = !_holdDone && Time.unscaledTime - _pressAt <= ClickSeconds;
        _pressSlot = -1;
        if (click && !_deleteArmed) LoadPreset(slot);
    }

    // The file moves to look_preset_N.deleted.txt (look_preset_N.txt.bak keeps the version before the last save), and Undo moves it back.
    string DeletedPath(int slot) { return Path.Combine(_dir, "look_preset_" + (slot + 1) + ".deleted.txt"); }

    void DeletePreset(int slot)
    {
        try
        {
            string path = PresetPath(slot);
            if (!File.Exists(path)) return;
            string before = File.ReadAllText(path);
            Replace(path, DeletedPath(slot));
            ReadPreset(slot);
            TakeUndo(UndoKind.Delete);
            _undoSlot = slot;
            _undoFileText = before;
            if (_activeSlot == slot) _activeSlot = -1;
            _deletedSlot = slot;
            _deletedAt = Time.unscaledTime;
            _deleteArmed = false;
            MarkChanged();
            Log("preset " + (slot + 1) + " deleted, kept as " + Path.GetFileName(DeletedPath(slot)));
        }
        catch (Exception e) { Log("preset " + (slot + 1) + " delete failed: " + e.Message); }
    }

    // The hold is timed while drawing, where the slot's own position is known: leaving the slot gives up, the full time saves or deletes.
    void PresetRow()
    {
        var e = Event.current;
        // Decided in the layout pass and kept for the event that follows it: a load on mouse-up or a save finishing in a repaint
        // must not add a button the layout pass didn't have.
        if (e.type == EventType.Layout) _layoutUndo = _undoKind != UndoKind.None;
        bool showUndo = _layoutUndo;
        var row = GUILayoutUtility.GetRect(10f, 36f, GUILayout.ExpandWidth(true));
        const float gap = 4f;
        float defaultsWidth = (row.width - gap * 4f) * 1.4f / 5.4f, slotWidth = (row.width - gap * 4f - defaultsWidth) / 4f;
        float holdTime = _deleteArmed ? DeleteSeconds : HoldSeconds;
        for (int i = 0; i < PresetCount; i++)
        {
            var r = new Rect(row.x + i * (slotWidth + gap), row.y, slotWidth, row.height);
            bool empty = _slotV[i] == null;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition) && !(empty && _deleteArmed))
            {
                _pressSlot = i;
                _pressAt = Time.unscaledTime;
                _holdDone = false;
                e.Use();
            }
            if (e.type != EventType.Repaint) continue;
            if (i == _pressSlot && !_holdDone)
            {
                if (!r.Contains(e.mousePosition)) _pressSlot = -1;
                else if (Time.unscaledTime - _pressAt >= holdTime)
                {
                    _holdDone = true;
                    if (_deleteArmed) DeletePreset(i); else SavePreset(i);
                }
            }
            empty = _slotV[i] == null;
            bool active = i == _activeSlot && !empty, pressed = i == _pressSlot && !_holdDone;
            string text = empty ? "EMPTY" : (i + 1).ToString(CultureInfo.InvariantCulture);
            if (active && !MatchesSlot(i)) text += " *";
            if (i == _savedSlot && Time.unscaledTime - _savedAt < 1.5f) text = "SAVED";
            if (i == _deletedSlot && Time.unscaledTime - _deletedAt < 1.5f) text = "DELETED";
            (empty ? _sSlotEmpty : pressed ? _sButtonDown : _sButton).Draw(r, GUIContent.none, false, false, false, false);
            if (pressed)
            {
                float held = Mathf.InverseLerp(ClickSeconds, holdTime, Time.unscaledTime - _pressAt);
                if (held > 0f) GUI.DrawTexture(new Rect(r.x, r.y, r.width * held, r.height), _deleteArmed ? _txDelete : _txHoldAccent);
            }
            if (_deleteArmed && !empty) Outline(r, 2f, _txDelete);
            else if (active) Outline(r, 1f, _txAccent);
            GUI.Label(r, text, active ? _sSlotTextActive : empty ? _sSlotTextEmpty : _sSlotText);
            if (pressed && _deleteArmed)
            {
                // The part of the number the red has covered turns dark; the rest stays readable.
                float washed = Mathf.InverseLerp(ClickSeconds, holdTime, Time.unscaledTime - _pressAt);
                GUI.BeginGroup(new Rect(r.x, r.y, r.width * washed, r.height));
                GUI.Label(new Rect(0f, 0f, r.width, r.height), text, _sSlotTextOnRed);
                GUI.EndGroup();
            }
        }
        var defaultsRect = new Rect(row.x + 4f * (slotWidth + gap), row.y, defaultsWidth, row.height);
        if (GUI.Button(defaultsRect, "DEFAULTS", _sButton))
        {
            TakeUndo(UndoKind.Defaults);
            SetDefaults();
            MarkChanged();
            ApplyToggles();
        }
        GUILayout.Space(4f);
        var deleteRect = GUILayoutUtility.GetRect(10f, 30f, GUILayout.ExpandWidth(true));
        deleteRect.width = slotWidth * 4f + gap * 3f;
        if (GUI.Button(deleteRect, _deleteArmed ? "DELETE ARMED " + Dot + " CLICK TO DISARM" : "DELETE", _deleteArmed ? _sDeleteArmed : _sDelete)) _deleteArmed = !_deleteArmed;
        GUILayout.Space(4f);
        GUILayout.Label(_deleteArmed ? "HOLD A PRESET 3 SEC. TO DELETE IT" : "CLICK LOADS " + Dot + " HOLD 1 SEC. SAVES YOUR STYLE CHOICES", _deleteArmed ? _sHintRed : _sHint);
        if (showUndo)
        {
            string undo = _undoKind == UndoKind.Defaults ? "UNDO DEFAULTS" : _undoKind == UndoKind.Load ? "UNDO LOAD" : _undoKind == UndoKind.Delete ? "UNDO DELETE" : "UNDO SAVE";
            GUILayout.Space(4f);
            if (GUILayout.Button(undo, _sButton, GUILayout.Width(170f), GUILayout.Height(32f)) && _undoKind != UndoKind.None) UndoLast();
        }
    }

    // A saved or deleted preset stays undoable while tuning goes on; undoing a load or defaults only makes sense right after it.
    void ClearUndo()
    {
        if (_undoKind == UndoKind.Save || _undoKind == UndoKind.Delete) return;
        _vBeforeReset = null;
        _bBeforeReset = null;
        _undoKind = UndoKind.None;
    }

    void ApplyToggles()
    {
        if (!_active) return;
        ApplyEffectHolds();
        ApplyLampSpots(_b[(int)B.LampSpotsOff]);
        if (_b[(int)B.FlatTerrain]) ScanTerrain(); else RestoreTerrain();
    }

    // ---- Features: what the mod checked against the game, for the log, the markers and the Status tab. ---------------------
    class Feature
    {
        public string Key, Name, Desc, Why;
        public int State = 1;   // 1 available, 0 not available, -1 checked once a world is loaded
        public bool World;
    }

    readonly List<Feature> _features = new List<Feature>();
    readonly Dictionary<string, Feature> _featureByKey = new Dictionary<string, Feature>();

    void AddFeature(string key, string name, string desc, bool world = false)
    {
        var f = new Feature { Key = key, Name = name, Desc = desc, World = world, State = world ? -1 : 1 };
        _features.Add(f);
        _featureByKey[key] = f;
    }

    Feature Feat(string key)
    {
        Feature f;
        return _featureByKey.TryGetValue(key, out f) ? f : null;
    }

    void SetFeature(string key, bool ok, string why)
    {
        var f = Feat(key);
        if (f == null) return;
        f.State = ok ? 1 : 0;
        f.Why = ok ? null : (why ?? "Not available.");
    }

    void SetPending(string key)
    {
        var f = Feat(key);
        if (f == null) return;
        f.State = -1;
        f.Why = null;
    }

    bool Ok(string key)
    {
        var f = Feat(key);
        return f != null && f.State == 1;
    }

    // 0 if any is not available, -1 if any is still to be checked, else 1.
    int StateOf(params string[] keys)
    {
        int state = 1;
        foreach (var k in keys)
        {
            var f = Feat(k);
            if (f == null) continue;
            if (f.State == 0) return 0;
            if (f.State < 0) state = -1;
        }
        return state;
    }

    void DefineFeatures()
    {
        if (_features.Count > 0) return;
        AddFeature("pause", "PAUSE", "The game is paused while this window is open (switch in the title bar).");
        AddFeature("look", "CAD LOOK", "Lines, flat facets, light bands, glow.");
        AddFeature("lampfix", "LAMP/SUN LIGHT FIX", "Stops lamp and sun light from bleeding through walls.");
        AddFeature("terrain", "SOLID-COLOR TERRAIN", "Ground and rock in flat colors.", true);
        AddFeature("effects", "GAME EFFECTS", "Bloom and volumetric light are switched as you set them.", true);
        AddFeature("bluenoise", "BLUE NOISE", "Fine noise that hides color steps in dark gradients.");
        AddFeature("screen", "SCREEN LIGHTS", "Computers light the room in front of their screen.");
        AddFeature("screenglow", "OTHER SCREENS", "Other devices with a screen light the room in its color.");
        AddFeature("machine", "FABRICATOR LIGHTS", "Fabricators light the room at their front.");
        AddFeature("console", "CONSOLE LIGHTS", "Consoles light the room at their screen.");
        AddFeature("vending", "VENDING MACHINE LIGHTS", "Vending machines light the room at their front.");
        AddFeature("battery", "BATTERY LIGHTS", "Station batteries glow in the color of their charge.");
        AddFeature("arcfurnace", "ARC FURNACE LIGHT", "Green when on, orange while smelting.");
        AddFeature("furnace", "FURNACE LIGHTS", "Furnaces light the room with their own flame.");
        AddFeature("apc", "POWER CONTROLLER LIGHTS", "The LED, and the cell's color while the door is open.");
        AddFeature("vents", "ACTIVE VENT LIGHTS", "The switch light of active vents.");
        AddFeature("poweredvents", "POWERED VENT LIGHTS", "The switch light of powered vents.");
        AddFeature("crusher", "ICE CRUSHER LIGHT", "Its switch light, red while crushing.");
        AddFeature("heaters", "WALL HEATER LIGHTS", "Wall heaters glow red while heating.");
        AddFeature("pipeheaters", "PIPE HEATER LIGHTS", "Pipe heaters glow orange while heating.");
        AddFeature("transformers", "TRANSFORMER LIGHTS", "The green light of transformers.");
        AddFeature("displays", "LED DISPLAY LIGHTS", "LED displays light the room in their color.");
        AddFeature("doors", "DOOR LIGHTS", "Airlock and door status lights reach into the room.");
        AddFeature("waypoint", "TRADER WAYPOINT LIGHT", "The waypoint's bulbs light their surroundings.");
        AddFeature("rocket", "ROCKET ENGINE LIGHT", "Firing engines light their surroundings.");
        AddFeature("gasfire", "GAS FIRE LIGHTS", "Burning gas lights the world.");
        AddFeature("burning", "BURNING ITEM LIGHTS", "Burning things light the world, and you.");
        AddFeature("flashlight", "FLASHLIGHT LIGHT", "Your flashlight lights the CAD look.");
        AddFeature("mouse", "WINDOW MOUSE GUARD", "Clicks on this window don't reach the game.");
        AddFeature("keys", "KEYS FILE", "keys.txt in the mod folder sets the keys.");
    }

    static readonly string[] SmallDeviceKeys = { "apc", "vents", "poweredvents", "crusher", "heaters", "pipeheaters", "transformers", "displays", "doors", "waypoint" };
    static readonly string[] SmallDeviceNames = { "POWER CONTROLLERS", "ACTIVE VENTS", "POWERED VENTS", "ICE CRUSHERS", "WALL HEATERS", "PIPE HEATERS", "TRANSFORMERS", "LED DISPLAYS", "DOORS", "WAYPOINT" };

    string _lookWhy, _lampWhy;

    // Everything that can be checked without a world, at game start. The rest follows once a world is loaded.
    void RunChecks()
    {
        DefineFeatures();
        SetFeature("look", _mat != null, _lookWhy);
        SetFeature("lampfix", _lampShader != null, _lampWhy ?? (_mat == null ? "It needs the CAD look's shader files, which did not load. The game's own lighting is used." : "The lamp fix shader did not load. The game's own lighting is used."));
        var broken = PanelCursorGuard.PatchReport.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Where(p => !p.EndsWith(" ok")).ToArray();
        SetFeature("mouse", broken.Length == 0, "Not found or failed in the game: " + string.Join(", ", broken) + ". Clicks on this window may also reach the game.");
        var world = AccessTools.TypeByName("WorldManager");
        _setPause = world == null ? null : AccessTools.Method(world, "SetGamePause", new[] { typeof(bool) });
        _isPausedProp = world == null ? null : AccessTools.Property(world, "IsGamePaused");
        var game = AccessTools.TypeByName("Assets.Scripts.GameManager");
        _gameStateProp = game == null ? null : AccessTools.Property(game, "GameState");
        if (_gameStateProp != null)
        {
            var prop = _gameStateProp;
            try { _gameStateCode = System.Linq.Expressions.Expression.Lambda<Func<int>>(System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(null, prop), typeof(int))).Compile(); }
            catch { _gameStateCode = () => Convert.ToInt32(prop.GetValue(null, null)); }
        }
        var inventory = AccessTools.TypeByName("Assets.Scripts.Inventory.InventoryManager");
        _showMenu = inventory == null ? null : AccessTools.Field(inventory, "ShowMenu");
        var net = AccessTools.TypeByName("Assets.Scripts.Networking.NetworkManager");
        _networkRole = net == null ? null : AccessTools.Field(net, "NetworkRole");
        string pauseWhy = _setPause == null || _isPausedProp == null ? "The game's pause switch (WorldManager.SetGamePause) was not found."
            : _gameStateProp == null ? "The game's state (GameManager.GameState) was not found, so the mod can't tell the menu from a world."
            : null;
        if (pauseWhy != null) { Log("pause: " + pauseWhy); _setPause = null; }
        if (_networkRole == null) Log("pause: NetworkManager.NetworkRole not found, the game is never paused (it could be multiplayer)");
        if (_gameStateProp == null) Log("window: the game's state was not found, the window does not open by itself");
        SetFeature("pause", pauseWhy == null, pauseWhy);
        LookForDevices();
        LookForFires();
        LookForWearables();
        SetFeature("bluenoise", File.Exists(Path.Combine(_dir, Path.Combine("Content", "bluenoise.png"))), "The file Content\\bluenoise.png is missing.");
        SetFeature("keys", _keysWhy == null, _keysWhy);
        SetPending("terrain");
        SetPending("effects");
        _notToday = NotToday();
        Log("checks: " + string.Join(", ", _features.Select(f => f.Key + (f.State == 1 ? " ok" : f.State == 0 ? " NOT AVAILABLE" : " later")).ToArray()));
    }

    // Camera effects and terrain exist only in a world; checked a few seconds after it starts.
    readonly HashSet<string> _effectsSeen = new HashSet<string>();
    bool _worldChecked, _flaresMissingLogged;

    void CheckWorld()
    {
        _effectsSeen.Clear();
        foreach (var c in _cam.GetComponents<MonoBehaviour>()) if (c != null) _effectsSeen.Add(c.GetType().Name);
        var missing = OffEffects.Concat(new[] { AlwaysOffEffect }).Where(n => !_effectsSeen.Contains(n)).ToArray();
        string why = missing.Length == 0 ? null : "Not found on the game's camera: " + string.Join(", ", missing) + ". Those stay as the game sets them.";
        if (why != null) Log("game effects: " + why);
        SetFeature("effects", why == null, why);
        int terrain = 0;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.shader != null && IsTerrain(m)) terrain++;
        why = terrain > 0 ? null : "No terrain material with the known texture names was found. The ground keeps its textures.";
        if (why != null) Log("terrain: " + why);
        SetFeature("terrain", why == null, why);
        Log("world checks: game effects " + (Ok("effects") ? "ok" : "NOT AVAILABLE") + ", terrain materials " + terrain);
    }

    string StatusSignature()
    {
        return string.Join(";", _features.Where(f => !f.World).Select(f => f.Key + (f.State == 1 ? "+" : "-")).ToArray());
    }

    // ---- Game state, pausing, opening by itself. ---------------------------------------------------------------------
    System.Reflection.MethodInfo _setPause;
    System.Reflection.PropertyInfo _isPausedProp, _gameStateProp;
    System.Reflection.FieldInfo _networkRole;
    bool _pausedByUs;
    Func<int> _gameStateCode;
    int _lastStateCode = -2;
    string _gameState = "";
    float _gameStateSince;
    bool _autoOpenDone;
    bool _pauseOnOpen = true;
    int _windowOpens;   // 0 each start, 1 on change, 2 never
    string _statusSeen = "";
    bool _notToday;
    static readonly string[] WindowOpensLabels = { "EACH START", "ON CHANGE", "NEVER" };
    static readonly string[] NotTodayLabels = { "AS SET ABOVE", "NOT AGAIN TODAY" };

    // "None" is the main menu, "Running" a world (code, GameManager.cs:870-906, WorldManager.cs:1544).
    bool InWorld { get { return _gameState == "Running" || _gameState == "Paused"; } }

    void UpdateGameState()
    {
        int code = -1;
        try { if (_gameStateCode != null) code = _gameStateCode(); }
        catch { }
        // GameState: None 0, Joining 1, Waiting 2, Running 3, Loading 4, Paused 5 (code, GridSystem/GameState.cs). A name only when it changes.
        string now = code == _lastStateCode ? _gameState : code == 0 ? "None" : code == 3 ? "Running" : code == 5 ? "Paused" : code < 0 ? "" : "State" + code;
        _lastStateCode = code;
        if (_pausedByUs && _frame % 30 == 0 && !GamePaused()) { _pausedByUs = false; Log("the game was resumed by something else"); }
        if (now != _gameState)
        {
            bool wasWorld = InWorld;
            Log("game state " + (_gameState.Length == 0 ? "-" : _gameState) + " -> " + (now.Length == 0 ? "-" : now));
            _gameState = now;
            _gameStateSince = Time.unscaledTime;
            if (wasWorld && !InWorld) _pausedByUs = false;
            if (InWorld && !wasWorld)
            {
                _worldChecked = false;
                SetPending("terrain");
                SetPending("effects");
            }
        }
        // At the main menu, once it has settled: at most once per game start.
        if (!_autoOpenDone && _gameStateProp != null && now == "None" && Camera.main != null && Time.unscaledTime - _gameStateSince > 2f)
        {
            _autoOpenDone = true;
            string why = AutoOpenWhy();
            Log("window at the main menu: " + why);
            if (why == "opens") SetPanelOpen(true);
        }
        if (InWorld && !_worldChecked && _cam != null && Time.unscaledTime - _gameStateSince > 5f)
        {
            _worldChecked = true;
            CheckWorld();
        }
    }

    string AutoOpenWhy()
    {
        if (_windowOpens == 2) return "stays closed (set to never)";
        if (_notToday) return "stays closed (not again today)";
        if (_windowOpens == 1 && StatusSignature() == _statusSeen) return "stays closed (nothing changed)";
        return "opens";
    }

    // Pausing only in a single-player world: a client can't, and a host would stop everyone.
    string PauseBlocked()
    {
        if (_setPause == null || _isPausedProp == null) return Feat("pause") != null ? Feat("pause").Why : "The game's pause switch was not found.";
        if (!InWorld) return "Only in a world.";
        if (_networkRole == null) return "The game's network role was not found, so the game is not paused.";
        object role = null;
        try { role = _networkRole.GetValue(null); } catch { }
        if (role == null || role.ToString() != "None") return "Multiplayer: the game is not paused, it would stop everyone.";
        return null;
    }

    System.Reflection.FieldInfo _showMenu;

    bool GameMenuShown()
    {
        try { return _showMenu != null && (bool)_showMenu.GetValue(null); }
        catch { return false; }
    }

    bool GamePaused()
    {
        try { return _isPausedProp != null && (bool)_isPausedProp.GetValue(null, null); }
        catch { return false; }
    }

    // Pauses only a running game, and resumes only what it paused itself.
    void ApplyPause(bool want)
    {
        try
        {
            if (want)
            {
                if (_pausedByUs || PauseBlocked() != null || GamePaused()) return;
                _setPause.Invoke(null, new object[] { true });
                _pausedByUs = true;
                Log("game paused while the window is open");
            }
            else if (_pausedByUs)
            {
                _pausedByUs = false;
                // Resume only our own pause: not under the game's menu, not once the game has turned multiplayer.
                if (!GamePaused()) return;
                if (GameMenuShown()) { Log("game stays paused: its own menu is open"); return; }
                string blocked = PauseBlocked();
                if (blocked != null) { Log("game stays paused: " + blocked); return; }
                _setPause.Invoke(null, new object[] { false });
                Log("game resumed");
            }
        }
        catch (Exception e)
        {
            Log("pause failed: " + e.Message);
            _pausedByUs = false;
        }
    }

    string NotTodayPath { get { return Path.Combine(_dir, "feature_status_not_today.txt"); } }
    static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

    bool NotToday()
    {
        try { return File.Exists(NotTodayPath) && File.ReadAllText(NotTodayPath).Trim() == Today; }
        catch { return false; }
    }

    // His design: a tiny file holding today's date; delete it or change the date and the window opens again.
    void SetNotToday(bool on)
    {
        try
        {
            if (on) WriteSafely(NotTodayPath, Today + "\n");
            else if (File.Exists(NotTodayPath)) File.Delete(NotTodayPath);
            _notToday = on;
            Log("not again today: " + (on ? "on (" + Today + ")" : "off"));
        }
        catch (Exception e) { Log("not again today failed: " + e.Message); }
    }

    // ---- Keys: keys.txt in the mod folder. ---------------------------------------------------------------------------
    KeyCode _keyLook = KeyCode.F9, _keyWindow = KeyCode.F11, _keyClose = KeyCode.Escape;
    int _closeReleasedFrame = -1;
    string _keysWhy;
    string KeysPath { get { return Path.Combine(_dir, "keys.txt"); } }

    const string KeysDefault =
        "# Gcotu Stationeers CAD Style: keys\n" +
        "# One line per key: name = key. Lines starting with # are notes.\n" +
        "# Key names are Unity's, for example F9, F11, Escape, Home, Insert, PageUp, Keypad5, Alpha1, LeftBracket.\n" +
        "# All names: https://docs.unity3d.com/ScriptReference/KeyCode.html\n" +
        "# Delete this file to get the defaults back. Changes count from the next game start.\n" +
        "\n" +
        "# Turns the look on and off.\n" +
        "look = F9\n" +
        "# Opens and closes the window.\n" +
        "window = F11\n" +
        "# Closes the window.\n" +
        "close = Escape\n";

    void LoadKeys()
    {
        try
        {
            if (!File.Exists(KeysPath)) { WriteSafely(KeysPath, KeysDefault); Log("keys.txt written with the default keys"); }
            var bad = new List<string>();
            foreach (var raw in File.ReadAllLines(KeysPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                KeyCode k;
                if (eq <= 0 || !TryKey(line.Substring(eq + 1).Trim(), out k)) { bad.Add(line); continue; }
                string name = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (name == "look") _keyLook = k;
                else if (name == "window") _keyWindow = k;
                else if (name == "close") _keyClose = k;
                else bad.Add(line);
            }
            if (_keyLook == _keyWindow || _keyLook == _keyClose)
            {
                bad.Add("the look key is also another key");
                _keyLook = KeyCode.F9; _keyWindow = KeyCode.F11; _keyClose = KeyCode.Escape;
            }
            _keysWhy = bad.Count == 0 ? null : "Not understood in keys.txt: " + string.Join(" | ", bad.ToArray()) + ". Those keys keep their defaults.";
        }
        catch (Exception e) { _keysWhy = "keys.txt could not be read: " + e.Message + ". The default keys are used."; }
        if (_keysWhy != null) Log("keys: " + _keysWhy);
        PanelCursorGuard.CloseKey = _keyClose;
        Log("keys: look " + _keyLook + ", window " + _keyWindow + ", close " + _keyClose);
    }

    static bool TryKey(string s, out KeyCode k)
    {
        k = KeyCode.None;
        if (s.Length == 0 || char.IsDigit(s[0])) return false;
        try
        {
            k = (KeyCode)Enum.Parse(typeof(KeyCode), s, true);
            return k != KeyCode.None && Enum.IsDefined(typeof(KeyCode), k) && (k < KeyCode.Mouse0 || k > KeyCode.Mouse6);
        }
        catch { return false; }
    }

    static string KeyName(KeyCode k)
    {
        return k == KeyCode.Escape ? "ESC" : k.ToString().ToUpperInvariant();
    }

    void OpenModFolder()
    {
        // Opened through the shell, so the player's own file manager comes up, whichever it is (his machine doesn't use Explorer).
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _dir, UseShellExecute = true, Verb = "open" }); }
        catch (Exception e) { Log("open mod folder failed: " + e.Message); }
    }

    // ---- His channel link under the build stamp: accent blue, underlined, the system's hand pointer while over it. ----------
    const string ChannelText = "YouTube: God, creator of the universe";
    const string ChannelUrl = "https://www.youtube.com/@OwnerOfTheCosmos";
    bool _linkHover;       // set during the window's repaint, read in LateUpdate
    bool _handShown, _handTried;
    Texture2D _hand;
    Vector2 _handHot;

    void LinkLine(string text, string url)
    {
        var content = new GUIContent(text);
        Vector2 size = _sLink.CalcSize(content);
        Rect r = GUILayoutUtility.GetRect(size.x, size.y, _sLink, GUILayout.ExpandWidth(false));
        var e = Event.current;
        if (e.type == EventType.Repaint)
        {
            _sLink.Draw(r, content, false, false, false, false);
            // Text() keeps 5 units below the measured text height; the line sits in the first of them.
            GUI.DrawTexture(new Rect(r.x, r.yMax - _sLink.padding.bottom + 1f, size.x, 1f), _txAccent);
            if (r.Contains(e.mousePosition)) _linkHover = true;
        }
        // A full click (press and release on the text) opens it; the browser is whatever the player has set.
        if (GUI.Button(r, GUIContent.none, GUIStyle.none))
        {
            try { Application.OpenURL(url); Log("opened " + url); }
            catch (Exception ex) { Log("open link failed: " + ex.Message); }
        }
    }

    void ShowHand(bool show)
    {
        if (show == _handShown) return;
        try
        {
            if (show)
            {
                if (!_handTried) { _handTried = true; _hand = LoadSystemHand(out _handHot); }
                if (_hand == null) return;
                Cursor.SetCursor(_hand, _handHot, CursorMode.Auto);
            }
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);   // null: back to the game's default pointer
            _handShown = show;
        }
        catch (Exception e) { _hand = null; Log("link pointer: " + e.Message); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapData { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr hinst, IntPtr name);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CopyImage(IntPtr image, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetObjectW(IntPtr obj, int size, out BitmapData data);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    // Windows' own link pointer (OCR_HAND) at the size Windows draws pointers on this screen, as a texture for Cursor.SetCursor.
    Texture2D LoadSystemHand(out Vector2 hotspot)
    {
        hotspot = Vector2.zero;
        uint dpi = 96;
        try { uint d = GetDpiForWindow(GetActiveWindow()); if (d > 0) dpi = d; } catch (Exception) { }
        int want = 32;
        try { int m = GetSystemMetricsForDpi(13, dpi); if (m > 0) want = m; } catch (Exception) { }   // 13 = SM_CXCURSOR
        // LoadImage with OCR_HAND fails (error 1814, tested); the shared IDC_HAND copied at the wanted size works (tested at 96/144/192 dpi).
        IntPtr shared = LoadCursorW(IntPtr.Zero, new IntPtr(32649));                               // 32649 = IDC_HAND
        if (shared == IntPtr.Zero) { Log("link pointer: Windows gave no hand cursor"); return null; }
        IntPtr cursor = CopyImage(shared, 2, want, want, 0x4000);                                   // 2 = IMAGE_CURSOR, 0x4000 = LR_COPYFROMRESOURCE
        if (cursor == IntPtr.Zero) { Log("link pointer: hand cursor copy failed, error " + Marshal.GetLastWin32Error()); return null; }
        IconInfo info;
        if (!GetIconInfo(cursor, out info)) { DestroyCursor(cursor); Log("link pointer: no cursor info"); return null; }
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            BitmapData bm;
            GetObjectW(info.hbmMask, Marshal.SizeOf(typeof(BitmapData)), out bm);
            int w = bm.bmWidth, h = info.hbmColor == IntPtr.Zero ? bm.bmHeight / 2 : bm.bmHeight;
            if (w <= 0 || h <= 0) { Log("link pointer: empty cursor"); return null; }
            byte[] mask = ReadBits(dc, info.hbmMask, w, info.hbmColor == IntPtr.Zero ? 2 * h : h);
            byte[] color = info.hbmColor == IntPtr.Zero ? null : ReadBits(dc, info.hbmColor, w, h);
            bool hasAlpha = false;
            if (color != null) for (int i = 3; i < color.Length; i += 4) if (color[i] != 0) { hasAlpha = true; break; }
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool andBit = mask[i] != 0;   // white in the AND mask = transparent
                    Color32 c;
                    if (color != null)
                        c = new Color32(color[i + 2], color[i + 1], color[i], hasAlpha ? color[i + 3] : (byte)(andBit ? 0 : 255));
                    else
                    {
                        bool xorBit = mask[((y + h) * w + x) * 4] != 0;
                        c = andBit && !xorBit ? new Color32(0, 0, 0, 0) : xorBit && !andBit ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                    }
                    px[(h - 1 - y) * w + x] = c;   // texture rows count from the bottom
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            hotspot = new Vector2(info.xHotspot, info.yHotspot);
            Log("link pointer: system hand " + w + "x" + h + " at dpi " + dpi);
            return tex;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            DestroyCursor(cursor);
        }
    }

    static byte[] ReadBits(IntPtr dc, IntPtr bitmap, int w, int h)
    {
        var bi = new BitmapInfoHeader { biSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        if (GetDIBits(dc, bitmap, 0, (uint)h, bits, ref bi, 0) == 0) throw new Exception("GetDIBits failed");
        return bits;
    }

    // ---- The window: tabs General, Look, Lights, Advanced, Status (his design, 2026-11-08). -----------------------------
    const float PanelWidth = 440f;
    const int PanelWindowId = 0x4F4C4F4B;
    const string Version = "1.0.1";
    bool _panelOpen;
    bool _panelPlaced;
    CursorLockMode _lockBefore;
    bool _visibleBefore;
    Rect _panelRect;      // in panel units, before _panelScale
    float _panelScale = 1f;
    int _tab;
    static readonly string[] TabNames = { "GENERAL", "LOOK", "LIGHTS", "ADVANCED", "STATUS" };
    readonly Vector2[] _tabScroll = new Vector2[5];   // each tab keeps its place for this game run

    void SetPanelOpen(bool open)
    {
        if (open == _panelOpen) return;
        _panelOpen = open;
        if (open)
        {
            _lockBefore = Cursor.lockState;
            _visibleBefore = Cursor.visible;
            PanelCursorGuard.PanelOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            string seen = StatusSignature();
            if (seen != _statusSeen) { _statusSeen = seen; MarkChanged(); }
            if (_pauseOnOpen) ApplyPause(true);
        }
        else
        {
            PanelCursorGuard.PanelOpen = false;
            PanelCursorGuard.PressStartedOnPanel = false;
            Cursor.lockState = _lockBefore;
            Cursor.visible = _visibleBefore;
            _pressSlot = -1;
            _deleteArmed = false;
            ApplyPause(false);
            if (_settingsDirty) SaveSettings();
        }
    }

    void LateUpdate()
    {
        UpdateLampsLate();
        ShowHand(_panelOpen && _tab == 0 && _linkHover);
        if (!_panelOpen) return;
        Cursor.visible = true;
    }

    // ---- Look of the window: colors, textures drawn in code, styles. -----------------------------------------------------
    static Color Hex(string h)
    {
        Color c;
        ColorUtility.TryParseHtmlString(h, out c);
        return c;
    }

    static readonly Color CBody = Hex("#121315"), CTitle = Hex("#1d1f22"), CDark = Hex("#0b0c0e"), CText = Hex("#dcdee1"), CMuted = Hex("#8b8e94"),
        CAccent = Hex("#7fb0e3"),   // his blue accent, okhsl(250, 60, 70)
        CFill = Hex("#183755"), CBright = Hex("#7eb0e3"),   // bar fill okhsl(250, 60, 23) and bar value (number and line) okhsl(250, 60, 70), his design
        CNotch = Hex("#36506b"),   // the fill mixed 14 % toward white in OKLab, as on his color page
        CPresetMark = Hex("#ffd878"),   // where the preset in use has a bar, okhsl(88, 100, 88)
        CField = Hex("#1a1b1e"),
        CGreen = Hex("#338d2c"), CRed = Hex("#d82235"), CPocketGreen = Hex("#55d861"), CPocketRed = Hex("#f8696f"),   // his switch colors, 2026-11-08
        CDelete = Hex("#ff4d42"), CBadText = Hex("#f0a0a4"), CRow = Hex("#17181b"), CRowBad = Hex("#211819");

    static Texture2D Fill(Color c)
    {
        // HideAndDontSave: otherwise a scene load reclaims the texture and the window goes transparent.
        var t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixels(new[] { c, c, c, c });
        t.Apply();
        return t;
    }

    // pixel(x, row counted from the top)
    static Texture2D Paint(int w, int h, Func<int, int, Color> pixel, bool mips = false)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, mips) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[(h - 1 - y) * w + x] = pixel(x, y);
        t.SetPixels(px);
        t.Apply(mips, false);
        return t;
    }

    // How much of pixel (x, y) a rounded rectangle covers, for smooth edges.
    static float Cover(float x, float y, float left, float top, float w, float h, float r)
    {
        float px = x + 0.5f - left, py = y + 0.5f - top;
        float qx = Mathf.Abs(px - w * 0.5f) - (w * 0.5f - r), qy = Mathf.Abs(py - h * 0.5f) - (h * 0.5f - r);
        float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        return Mathf.Clamp01(0.5f - d);
    }

    static Color Mix(Color under, Color over, float a)
    {
        var c = Color.Lerp(under, over, Mathf.Clamp01(a));
        c.a = under.a;
        return c;
    }

    static Color Alpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // The switch slot, lit as a whole: sunken, darker at the top. Drawn at twice the size so it stays sharp on big screens.
    static Texture2D SlotTexture(Color fill)
    {
        const int k = 2, w = 60 * k, h = 24 * k;
        return Paint(w, h, (x, y) => Alpha(Mix(fill, Color.black, 0.75f * (1f - y / (5f * k))), Cover(x, y, 0, 0, w, h, 5f * k)));
    }

    // D-4: the pocket the active word sits in; dark top and sides, light bottom edge, a soft shadow at the top inside.
    static Texture2D PocketTexture(Color fill, int width, int height, int k)
    {
        int w = width * k, h = height * k;
        return Paint(w, h, (x, y) =>
        {
            var c = Mix(fill, Color.black, 0.45f * (1f - y / (2.5f * k)));
            bool edge = Cover(x, y, k, k, w - 2 * k, h - 2 * k, 2f * k) < 0.5f;
            if (edge) c = y >= h - 1.5f * k ? Mix(c, Color.white, 0.45f) : Mix(c, Color.black, 0.6f);
            return Alpha(c, Cover(x, y, 0, 0, w, h, 3f * k));
        });
    }

    // A sunken field: rounded or square, darker at the top.
    static Texture2D CavityTexture(int w, int h, float r, Color fill)
    {
        return Paint(w, h, (x, y) => Alpha(Mix(fill, Color.black, 0.75f * (1f - y / 5f)), r > 0f ? Cover(x, y, 0, 0, w, h, r) : 1f));
    }

    static Texture2D ShadowTexture()
    {
        return Paint(8, 12, (x, y) => new Color(0f, 0f, 0f, 0.75f * Mathf.Clamp01(1f - y / 5f)));
    }

    // Raised: a light top edge, a dark bottom edge. Pressed: the other way round.
    static Texture2D ButtonTexture(bool down)
    {
        const int w = 8, h = 16;
        return Paint(w, h, (x, y) =>
        {
            if (!down)
            {
                if (y == 0) return Hex("#5a5d64");
                if (y >= h - 3) return Hex("#040405");
                return Color.Lerp(Hex("#3a3d42"), Hex("#232529"), (y - 1f) / (h - 4f));
            }
            if (y < 3) return Hex("#040405");
            if (y == h - 1) return Hex("#3d4045");
            return Color.Lerp(Hex("#18191c"), Hex("#222428"), (y - 3f) / (h - 4f));
        });
    }

    static Texture2D EdgeTexture(Color fill, Color top, int topRows, Color bottom, int bottomRows)
    {
        return Paint(4, 8, (x, y) => y < topRows ? top : y >= 8 - bottomRows ? bottom : fill);
    }

    static Texture2D OutlineTexture(Color fill, Color line)
    {
        return Paint(4, 4, (x, y) => x == 0 || y == 0 || x == 3 || y == 3 ? line : fill);
    }

    static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
        return new Vector2(px - ax - t * vx, py - ay - t * vy).magnitude;
    }

    // Round markers: green check, red cross, gray while still to be checked.
    static Texture2D MarkerTexture(int state)
    {
        const int s = 32;
        Color fill = state == 1 ? CGreen : state == 0 ? CRed : Hex("#4a4d53"), glyph = Hex("#121315");
        float k = s / 26f, stroke = 1.5f * k;
        return Paint(s, s, (x, y) =>
        {
            float px = x + 0.5f, py = y + 0.5f;
            float a = Mathf.Clamp01(s * 0.5f - new Vector2(px - s * 0.5f, py - s * 0.5f).magnitude + 0.5f);
            float d = 99f;
            if (state == 1) d = Mathf.Min(Segment(px, py, 7.5f * k, 13.5f * k, 11f * k, 17f * k), Segment(px, py, 11f * k, 17f * k, 18.5f * k, 9f * k));
            if (state == 0) d = Mathf.Min(Segment(px, py, 8.5f * k, 8.5f * k, 17.5f * k, 17.5f * k), Segment(px, py, 17.5f * k, 8.5f * k, 8.5f * k, 17.5f * k));
            return Alpha(Color.Lerp(fill, glyph, Mathf.Clamp01(stroke - d + 0.5f)), a);
        }, true);
    }

    Font _mono;
    GUISkin _skin;
    Texture2D _txPresetMark, _txBright, _txTitle, _txDark, _txField, _txFill, _txNotch, _txAccent, _txHoldAccent, _txDelete, _txSlotGreen, _txSlotRed, _txPocketGreen, _txPocketRed, _txMarkOk, _txMarkBad, _txMarkLater;
    GUIStyle _windowStyle, _sContent, _sTitle, _sSmall, _sSmallRed, _sLabel, _sGroup, _sGroupBad, _sBody, _sBodyBad, _sKey, _sBarLabel, _sBarValue, _sWordStrong, _sWordFaint,
        _sChoiceOn, _sChoiceOff, _sButton, _sButtonDown, _sTabFront, _sTabBack, _sTabTextFront, _sTabTextBack, _sSlotEmpty, _sSlotText, _sSlotTextActive, _sSlotTextEmpty,
        _sSlotTextOnRed, _sFooter, _sFooterChanged, _sLabelBox, _sSmallBox, _sSmallRedBox, _sGroupBox, _sGroupBadBox, _sDelete, _sDeleteArmed, _sHint, _sHintRed, _sCavity, _sPocketAccent, _sShadow, _sRow, _sRowBad, _sStatusText, _sStatusBad, _sBigX, _sChip, _sChipBad, _sLink;

    // Text in the flow gets room above and below its lines (the font's measured height leaves the descenders out), and is never clipped.
    GUIStyle Text(int size, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
    {
        var s = new GUIStyle { font = _mono, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = align, wordWrap = wrap, richText = false, clipping = TextClipping.Overflow };
        s.padding = new RectOffset(0, 0, 3, 5);
        s.normal.textColor = s.hover.textColor = s.active.textColor = s.onNormal.textColor = color;
        return s;
    }

    // Text placed in a fixed box (bars, switches, buttons): centered in the box itself.
    GUIStyle BoxText(int size, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var s = Text(size, color, bold, align);
        s.padding = new RectOffset(0, 0, 0, 0);
        return s;
    }

    static GUIStyle Back(Texture2D tex, RectOffset border = null, RectOffset padding = null)
    {
        var s = new GUIStyle();
        s.normal.background = s.hover.background = s.active.background = s.onNormal.background = tex;
        s.border = border ?? new RectOffset(0, 0, 0, 0);
        s.padding = padding ?? new RectOffset(0, 0, 0, 0);
        return s;
    }

    void MakeStyles()
    {
        if (_windowStyle != null) return;
        try
        {
            _mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Lucida Console" }, 13);
            if (_mono != null) _mono.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception e) { Log("window font: " + e.Message); }
        _txTitle = Fill(CTitle);
        _txDark = Fill(CDark);
        _txField = Fill(CField);
        _txFill = Fill(CFill);
        _txNotch = Fill(CNotch);
        _txAccent = Fill(CAccent);
        _txBright = Fill(CBright);
        _txPresetMark = Fill(CPresetMark);
        _txHoldAccent = Fill(Alpha(CAccent, 0.35f));
        _txDelete = Fill(CDelete);
        _txSlotGreen = SlotTexture(CGreen);
        _txSlotRed = SlotTexture(CRed);
        _txPocketGreen = PocketTexture(CPocketGreen, 28, 20, 2);
        _txPocketRed = PocketTexture(CPocketRed, 28, 20, 2);
        _txMarkOk = MarkerTexture(1);
        _txMarkBad = MarkerTexture(0);
        _txMarkLater = MarkerTexture(-1);

        _windowStyle = Back(Fill(CBody));
        _sContent = new GUIStyle { padding = new RectOffset(14, 18, 4, 14) };
        _sTitle = BoxText(13, CText, true, TextAnchor.MiddleLeft);
        _sSmall = Text(11, CMuted);
        _sSmallRed = Text(11, CBadText);
        _sLabel = Text(13, CText);
        _sGroup = Text(11, Hex("#b9bcc2"));
        _sGroupBad = Text(11, CBadText);
        _sBody = Text(13, Hex("#c4c6ca"), false, TextAnchor.UpperLeft, true);
        _sBodyBad = Text(12, CBadText, false, TextAnchor.UpperLeft, true);
        _sKey = Text(13, CAccent);
        _sBarLabel = BoxText(13, CText, false, TextAnchor.MiddleLeft);
        _sBarLabel.padding = new RectOffset(10, 0, 0, 0);
        _sBarValue = BoxText(13, CBright, false, TextAnchor.MiddleRight);
        _sBarValue.padding = new RectOffset(0, 10, 0, 0);
        _sWordStrong = BoxText(11, new Color(0f, 0f, 0f, 0.88f), true, TextAnchor.MiddleCenter);
        _sWordFaint = BoxText(11, new Color(0f, 0f, 0f, 0.35f), true, TextAnchor.MiddleCenter);
        _sChoiceOn = BoxText(11, new Color(0f, 0f, 0f, 0.88f), true, TextAnchor.MiddleCenter);
        _sChoiceOff = BoxText(11, CMuted, true, TextAnchor.MiddleCenter);
        var up = ButtonTexture(false);
        var down = ButtonTexture(true);
        _sButton = BoxText(13, CText);
        _sButton.normal.background = _sButton.hover.background = up;
        _sButton.active.background = down;
        _sButton.border = new RectOffset(1, 1, 4, 4);
        _sButton.padding = new RectOffset(12, 12, 4, 4);
        _sButtonDown = BoxText(13, CText);
        _sButtonDown.normal.background = _sButtonDown.hover.background = _sButtonDown.active.background = down;
        _sButtonDown.border = new RectOffset(1, 1, 4, 4);
        _sTabFront = Back(EdgeTexture(CBody, Hex("#4a4d53"), 1, CBody, 0), new RectOffset(0, 0, 1, 0));
        _sTabBack = Back(EdgeTexture(Hex("#17181b"), Hex("#2c2e33"), 1, Hex("#050506"), 1), new RectOffset(0, 0, 1, 1));
        _sTabTextFront = BoxText(12, Hex("#f2f3f4"), true, TextAnchor.MiddleCenter);
        _sTabTextBack = BoxText(12, Hex("#7d8086"), false, TextAnchor.MiddleCenter);
        _sSlotEmpty = Back(OutlineTexture(Hex("#16171a"), Hex("#3a3c41")), new RectOffset(1, 1, 1, 1));
        _sSlotText = BoxText(13, CText, false, TextAnchor.MiddleCenter);
        _sSlotTextActive = BoxText(13, CAccent, true, TextAnchor.MiddleCenter);
        _sSlotTextEmpty = BoxText(13, Hex("#5a5d63"), false, TextAnchor.MiddleCenter);
        _sSlotTextOnRed = BoxText(13, Hex("#1a0b0a"), true, TextAnchor.MiddleCenter);
        _sDelete = BoxText(12, Hex("#e5a59f"), false, TextAnchor.MiddleCenter);
        _sDelete.normal.background = _sDelete.hover.background = _sDelete.active.background = EdgeTexture(Hex("#2a1a1a"), Hex("#3d2626"), 1, Hex("#050506"), 2);
        _sDelete.border = new RectOffset(0, 0, 1, 2);
        _sDeleteArmed = BoxText(12, Hex("#1a0b0a"), true, TextAnchor.MiddleCenter);
        _sDeleteArmed.normal.background = _sDeleteArmed.hover.background = _sDeleteArmed.active.background = EdgeTexture(CDelete, CDelete, 0, Hex("#7a1d17"), 2);
        _sDeleteArmed.border = new RectOffset(0, 0, 0, 2);
        _sHint = Text(11, CMuted, false, TextAnchor.UpperLeft, true);
        _sHintRed = Text(11, Hex("#ff8a80"), false, TextAnchor.UpperLeft, true);
        _sCavity = Back(CavityTexture(16, 28, 5f, CField), new RectOffset(6, 6, 0, 0));
        _sPocketAccent = Back(PocketTexture(CAccent, 12, 24, 1), new RectOffset(4, 4, 0, 0));
        _sShadow = Back(ShadowTexture(), new RectOffset(0, 0, 6, 0));
        _sRow = Back(Fill(CRow), null, new RectOffset(10, 10, 8, 8));
        _sRowBad = Back(Fill(CRowBad), null, new RectOffset(10, 10, 8, 8));
        _sStatusText = Text(12, CMuted, false, TextAnchor.UpperLeft, true);
        _sStatusBad = Text(12, Hex("#e6a9ac"), false, TextAnchor.UpperLeft, true);
        _sBigX = BoxText(18, CText, true, TextAnchor.MiddleCenter);
        _sChip = BoxText(11, Hex("#b9bcc2"), false, TextAnchor.MiddleLeft);

        _sChip.padding = new RectOffset(22, 7, 3, 3);
        _sChipBad = BoxText(11, CBadText, false, TextAnchor.MiddleLeft);

        _sChipBad.padding = new RectOffset(22, 7, 3, 3);
        _sFooter = BoxText(11, CMuted, false, TextAnchor.MiddleLeft);
        _sLabelBox = BoxText(13, CText, false, TextAnchor.MiddleLeft);
        _sSmallBox = BoxText(11, CMuted, false, TextAnchor.MiddleLeft);
        _sSmallRedBox = BoxText(11, CBadText, false, TextAnchor.MiddleLeft);
        _sGroupBox = BoxText(11, Hex("#b9bcc2"), false, TextAnchor.MiddleLeft);
        _sGroupBadBox = BoxText(11, CBadText, false, TextAnchor.MiddleLeft);
        // The scrollbar in the window's own look: a thin dark track, a gray thumb, no arrow buttons.
        try
        {
            _skin = UnityEngine.Object.Instantiate(GUI.skin);
            _skin.hideFlags = HideFlags.HideAndDontSave;
            _skin.verticalScrollbar = new GUIStyle { name = "verticalscrollbar", fixedWidth = 8f, margin = new RectOffset(4, 2, 2, 2) };
            _skin.verticalScrollbar.normal.background = Fill(Hex("#0e0f11"));
            _skin.verticalScrollbarThumb = new GUIStyle { name = "verticalscrollbarthumb", fixedWidth = 8f };
            _skin.verticalScrollbarThumb.normal.background = Fill(Hex("#3a3c41"));
            _skin.verticalScrollbarUpButton = new GUIStyle { name = "verticalscrollbarupbutton", fixedWidth = 8f, fixedHeight = 0.01f };
            _skin.verticalScrollbarDownButton = new GUIStyle { name = "verticalscrollbardownbutton", fixedWidth = 8f, fixedHeight = 0.01f };
        }
        catch (Exception e) { _skin = null; Log("window scrollbar: " + e.Message); }
        _sFooterChanged = BoxText(11, CAccent, true, TextAnchor.MiddleLeft);
        _sLink = Text(11, CAccent);
    }

    Texture2D Marker(int state)
    {
        return state == 1 ? _txMarkOk : state == 0 ? _txMarkBad : _txMarkLater;
    }

    static void Outline(Rect r, float t, Texture2D tex)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
    }

    // ---- Controls. ---------------------------------------------------------------------------------------------------------
    // The switch: the pocket slides onto the word it stands for. Any click on it flips it, so it can be hit blind (his call).
    bool Switch(Rect r, bool on)
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            e.Use();
            return !on;
        }
        if (e.type == EventType.Repaint)
        {
            GUI.DrawTexture(r, on ? _txSlotGreen : _txSlotRed);
            GUI.DrawTexture(new Rect(on ? r.x + 31f : r.x + 1f, r.y + 2f, 28f, 20f), on ? _txPocketGreen : _txPocketRed);
            GUI.Label(new Rect(r.x, r.y, 30f, r.height), "OFF", on ? _sWordFaint : _sWordStrong);
            GUI.Label(new Rect(r.x + 30f, r.y, 30f, r.height), "ON", on ? _sWordStrong : _sWordFaint);
        }
        return on;
    }

    // A labelled switch row; a marker in front when the feature behind it is not available (or still to be checked).
    bool SwitchRow(string label, bool on, int state = 1)
    {
        var row = GUILayoutUtility.GetRect(10f, 32f, GUILayout.ExpandWidth(true));
        float x = row.x;
        if (state != 1)
        {
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(x, row.y + 9f, 14f, 14f), Marker(state));
            x += 22f;
        }
        GUI.Label(new Rect(x, row.y, row.xMax - 66f - x, row.height), label, _sLabelBox);
        return Switch(new Rect(row.xMax - 60f, row.y + 4f, 60f, 24f), on);
    }

    void ToggleSwitch(B which, string label, int state = 1)
    {
        int i = (int)which;
        bool now = SwitchRow(label, _b[i], state);
        if (now == _b[i]) return;
        _b[i] = now;
        ClearUndo();
        MarkChanged();
        ApplyToggles();
    }

    // The choice slider: the same pocket, in the accent color, on the chosen word. With two words and toggle set, any click flips it.
    int Choice(string[] labels, int index, bool toggle = false)
    {
        var r = GUILayoutUtility.GetRect(10f, 28f, GUILayout.ExpandWidth(true));
        var e = Event.current;
        int n = labels.Length;
        float seg = r.width / n;
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            e.Use();
            if (toggle && n == 2) return 1 - index;
            return Mathf.Clamp((int)((e.mousePosition.x - r.x) / seg), 0, n - 1);
        }
        if (e.type == EventType.Repaint)
        {
            _sCavity.Draw(r, false, false, false, false);
            _sPocketAccent.Draw(new Rect(r.x + index * seg + 2f, r.y + 2f, seg - 4f, r.height - 4f), false, false, false, false);
            for (int j = 0; j < n; j++) GUI.Label(new Rect(r.x + j * seg, r.y, seg, r.height), labels[j], j == index ? _sChoiceOn : _sChoiceOff);
        }
        return index;
    }

    // The value bar: drag or click anywhere on it. Fill to the value, scale marks at real values, a bright line at the value, a mark where the preset in use has it.
    void BarRow(S which)
    {
        int i = (int)which;
        var d = Sliders[i];
        var r = GUILayoutUtility.GetRect(10f, 28f, GUILayout.ExpandWidth(true));
        GUILayout.Space(3f);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; SetFromBar(i, r, e.mousePosition.x); e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { SetFromBar(i, r, e.mousePosition.x); e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                float span = d.Max - d.Min;
                float pos = Mathf.Clamp01((_v[i] - d.Min) / span);
                if (d.Curve != 1f) pos = Mathf.Pow(pos, 1f / d.Curve);
                GUI.DrawTexture(r, _txField);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * pos, r.height), _txFill);
                foreach (float tick in Ticks(i)) GUI.DrawTexture(new Rect(r.x + r.width * tick - 0.5f, r.yMax - 5f, 1f, 5f), _txNotch);
                if (_activeSlot >= 0 && _slotV[_activeSlot] != null && _slotV[_activeSlot][i] != _v[i])
                {
                    float was = Mathf.Clamp01((_slotV[_activeSlot][i] - d.Min) / span);
                    if (d.Curve != 1f) was = Mathf.Pow(was, 1f / d.Curve);
                    GUI.DrawTexture(new Rect(Mathf.Clamp(r.x + r.width * was - 1f, r.x, r.xMax - 2f), r.y, 2f, r.height - 6f), _txPresetMark);
                }
                GUI.DrawTexture(new Rect(Mathf.Clamp(r.x + r.width * pos - 1f, r.x, r.xMax - 2f), r.y, 2f, r.height), _txBright);
                _sShadow.Draw(r, false, false, false, false);
                GUI.Label(r, d.Label.ToUpperInvariant(), _sBarLabel);
                GUI.Label(r, _v[i].ToString(d.Format, CultureInfo.InvariantCulture), _sBarValue);
                break;
        }
    }

    // Scale marks at round values of each slider's own range (1, 2 or 5 times a power of ten, five to ten of them),
    // placed where those values really sit, curve included. Positions 0 to 1 along the bar.
    readonly float[][] _ticks = new float[(int)S.Count][];

    float[] Ticks(int i)
    {
        if (_ticks[i] != null) return _ticks[i];
        var d = Sliders[i];
        float span = d.Max - d.Min;
        var values = new List<float>();
        if (d.Curve == 1f)
        {
            // Straight bars: evenly spaced round values.
            float raw = span / 8f, power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            float step = power;
            foreach (float m in new[] { 1f, 2f, 5f, 10f }) { step = m * power; if (step >= raw) break; }
            for (float v = Mathf.Ceil(d.Min / step - 0.0001f) * step; v < d.Max - step * 0.01f; v += step) values.Add(v);
        }
        else
        {
            // Curved bars are fine at the low end, so their marks are 1, 2, 5 in every power of ten: 0.1, 0.2, 0.5, 1, 2, 5, 10 ...
            for (float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(span)) - 4f); power <= d.Max; power *= 10f)
                foreach (float m in new[] { 1f, 2f, 5f }) values.Add(m * power);
        }
        var list = new List<float>();
        float last = -1f;
        foreach (float v in values)
        {
            if (v <= d.Min + span * 0.005f || v >= d.Max - span * 0.005f) continue;
            float p = (v - d.Min) / span;
            if (d.Curve != 1f) p = Mathf.Pow(p, 1f / d.Curve);
            if (p - last < 0.03f) continue;   // marks closer than about 12 px say nothing
            list.Add(p);
            last = p;
        }
        return _ticks[i] = list.ToArray();
    }

    void SetFromBar(int i, Rect r, float x)
    {
        var d = Sliders[i];
        float p = Mathf.Clamp01((x - r.x) / r.width);
        if (d.Curve != 1f) p = Mathf.Pow(p, d.Curve);
        float v = Mathf.Clamp(Mathf.Round((d.Min + (d.Max - d.Min) * p) / d.Step) * d.Step, d.Min, d.Max);
        if (v == _v[i]) return;
        _v[i] = v;
        ClearUndo();
        MarkChanged();
    }

    void Bars(params S[] which)
    {
        foreach (var s in which) BarRow(s);
    }

    void Section(string name)
    {
        GUILayout.Space(20f);
        GUILayout.Label(name, _sGroup);
        GUILayout.Space(4f);
    }

    // A group header with the marker of the features behind it; "n OF m" when only some of them work.
    void Group(string name, int state, string extra = null)
    {
        GUILayout.Space(16f);
        var r = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(r.x, r.y + 4f, 14f, 14f), Marker(state));
        var labelRect = new Rect(r.x + 22f, r.y, r.width - 22f, r.height);
        GUI.Label(labelRect, name, state == 0 ? _sGroupBadBox : _sGroupBox);
        if (extra != null)
        {
            float w = _sGroup.CalcSize(new GUIContent(name)).x;
            GUI.Label(new Rect(labelRect.x + w + 12f, r.y, 120f, r.height), extra, _sSmallRedBox);
        }
        GUILayout.Space(4f);
    }

    // Several features under one set of sliders: the group shows a check if any works, with chips naming each when not all do.
    void GroupOf(string name, string[] keys, string[] names)
    {
        int ok = keys.Count(k => Ok(k)), bad = keys.Count(k => StateOf(k) == 0);
        Group(name, ok == 0 && bad > 0 ? 0 : StateOf(keys) == -1 ? -1 : 1, bad > 0 && ok > 0 ? ok + " OF " + keys.Length : null);
        if (bad == 0) return;
        var e = Event.current;
        var full = GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true));
        if (e.type == EventType.Repaint && full.width > 10f) _chipWidth = full.width;
        float width = _chipWidth;
        float x = 0f;
        int lines = 1;
        var sizes = new float[keys.Length];
        for (int j = 0; j < keys.Length; j++)
        {
            sizes[j] = _sChip.CalcSize(new GUIContent(names[j])).x;
            if (x > 0f && x + sizes[j] > width) { lines++; x = 0f; }
            x += sizes[j] + 4f;
        }
        var area = GUILayoutUtility.GetRect(10f, lines * 24f, GUILayout.ExpandWidth(true));
        x = 0f;
        float y = 0f;
        for (int j = 0; j < keys.Length; j++)
        {
            if (x > 0f && x + sizes[j] > width) { y += 24f; x = 0f; }
            var chip = new Rect(area.x + x, area.y + y, sizes[j], 20f);
            bool works = Ok(keys[j]);
            GUI.Label(chip, names[j], works ? _sChip : _sChipBad);
            if (e.type == EventType.Repaint) GUI.DrawTexture(new Rect(chip.x + 6f, chip.y + 4f, 12f, 12f), Marker(StateOf(keys[j])));
            x += sizes[j] + 4f;
        }
        GUILayout.Space(4f);
    }

    float KeyColumn()
    {
        float w = 76f;
        foreach (var k in new[] { _keyLook, _keyWindow, _keyClose }) w = Mathf.Max(w, _sKey.CalcSize(new GUIContent(KeyName(k))).x + 12f);
        return w;
    }

    float _chipWidth = PanelWidth - 48f;

    void KeyLine(KeyCode key, string what)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(KeyName(key), _sKey, GUILayout.Width(KeyColumn()));
        GUILayout.Label(what, _sLabel);
        GUILayout.EndHorizontal();
    }

    // ---- The tabs. -----------------------------------------------------------------------------------------------------------
    void DrawGeneral()
    {
        Section("ABOUT");
        GUILayout.Label("Gives Stationeers a CAD drawing look: black outlines, flat facets, light bands. Stops lamp/sun light from bleeding through walls. Devices, fire and your flashlight light their surroundings.", _sBody);
        Section("KEYS");
        KeyLine(_keyLook, "LOOK ON / OFF");
        KeyLine(_keyWindow, "OPEN / CLOSE THIS WINDOW");
        KeyLine(_keyClose, "CLOSE THIS WINDOW");
        if (_keysWhy != null) GUILayout.Label(_keysWhy, _sBodyBad);
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("CHANGE KEYS IN KEYS.TXT", _sSmall, GUILayout.Height(32f));
        if (GUILayout.Button("OPEN MOD FOLDER", _sButton, GUILayout.Width(170f), GUILayout.Height(32f))) OpenModFolder();
        GUILayout.EndHorizontal();
        Section("THIS WINDOW OPENS BY ITSELF AT THE MAIN MENU");
        int opens = Choice(WindowOpensLabels, _windowOpens);
        if (opens != _windowOpens) { _windowOpens = opens; MarkChanged(); }
        GUILayout.Space(6f);
        int today = Choice(NotTodayLabels, _notToday ? 1 : 0, true);
        if ((today == 1) != _notToday) SetNotToday(today == 1);
        Section("PRESETS");
        PresetRow();
        GUILayout.Space(18f);
        GUILayout.Label(BuildStamp, _sSmall);
        LinkLine(ChannelText, ChannelUrl);
    }

    void DrawLook()
    {
        int look = StateOf("look");
        if (look == 0) GUILayout.Label("THE LOOK IS NOT AVAILABLE, SEE STATUS", _sSmallRed);
        Group("LINES", look);
        Bars(S.LineWidth, S.LineOpacity);
        Group("FACETS", look);
        Bars(S.SmoothForms, S.FacetShade, S.AmbientKeep);
        Group("LIGHT BANDS", look);
        Bars(S.Posterize, S.Bands, S.BandTop, S.BandFloor);
        Group("GLOW", look);
        Bars(S.GlowStrength, S.GlowRadius, S.GlowPasses);
        ToggleSwitch(B.AdaptOn, "EYE ADAPTATION");
        Group("SCENE", look);
        Bars(S.TextureFade, S.KeepColorLines);
        ToggleSwitch(B.DitherOn, "BLUE NOISE", StateOf("bluenoise"));
        ToggleSwitch(B.FlatTerrain, "SOLID-COLOR TERRAIN", StateOf("terrain"));
        int fix = StateOf("lampfix");
        Group("LAMP/SUN LIGHT FIX", fix);
        GUILayout.Label(fix == 1 ? "WORKS BY ITSELF, NO SETTINGS" : "NOT AVAILABLE, SEE STATUS", fix == 1 ? _sSmall : _sSmallRed);
    }

    void DrawLights()
    {
        Group("SCREEN LIGHTS", StateOf("screen", "screenglow"));
        ToggleSwitch(B.ScreenLights, "ON");
        Bars(S.ScreenPower, S.ScreenReach);
        GroupOf("MACHINE LIGHTS", new[] { "machine", "console", "vending" }, new[] { "FABRICATORS", "CONSOLES", "VENDING MACHINES" });
        Bars(S.MakerR, S.MakerG, S.MakerB, S.MakerReach);
        Group("BATTERY LIGHTS", StateOf("battery"));
        Bars(S.BatteryPower, S.BatteryReach, S.BatteryBias);
        Group("ARC FURNACE LIGHT", StateOf("arcfurnace"));
        Bars(S.FurnacePower, S.FurnaceReach);
        Group("FURNACE LIGHTS", StateOf("furnace"));
        Bars(S.FlamePower, S.FlameReach, S.FlameRing);
        Group("ROCKET ENGINE LIGHT", StateOf("rocket"));
        Bars(S.RocketPower, S.RocketReach);
        GroupOf("FIRE LIGHTS", new[] { "gasfire", "burning" }, new[] { "BURNING GAS", "BURNING THINGS" });
        Bars(S.FireMax, S.FirePower, S.FireGamma);
        GroupOf("SMALL DEVICE LIGHTS", SmallDeviceKeys, SmallDeviceNames);
        Bars(S.ApcLed, S.ApcCell, S.ApcReach);
        Group("FLASHLIGHT LIGHT", StateOf("flashlight"));
        Bars(S.FlashPower, S.FlashReach);
    }

    void DrawAdvanced()
    {
        int look = StateOf("look");
        Group("LINES", look);
        Bars(S.CreaseAngle, S.DepthStep, S.ColorStep, S.LineStability);
        Group("FACET SHADING DIRECTION", look);
        Bars(S.FacetAzimuth, S.FacetElevation);
        Group("GLOW COLOR", look);
        Bars(S.GlowColor, S.BoostR, S.BoostG, S.BoostB);
        Group("EYE ADAPTATION", look);
        Bars(S.AdaptGamma, S.AdaptSensitivity, S.AdaptDown, S.AdaptUp);
        if (Event.current.type == EventType.Layout) _layoutMeasured = _b[(int)B.AdaptOn] && V(S.AdaptGamma) > 0f && _lines != null;
        if (_layoutMeasured)
            GUILayout.Label("MEASURED LIGHT " + (_lines == null || _lines.MeasuredLight < 0f ? "..." : _lines.MeasuredLight.ToString("0.00", CultureInfo.InvariantCulture)) + ", GAMMA " + _adaptGamma.ToString("0.00", CultureInfo.InvariantCulture), _sSmall);
        Group("BLUE NOISE", StateOf("bluenoise"));
        Bars(S.Dither);
        Group("FADED TEXTURE COLORS", look);
        GUILayout.Label(V(S.TextureFade) > 0f ? "FOR TEXTURES FADED BY TEXTURE FADE" : "WORK WITH TEXTURE FADE ABOVE 0 (LOOK TAB)", _sSmall);
        Bars(S.GradeBlack, S.GradeWhite, S.GradeGamma, S.GradeSaturation, S.GradeKeep, S.GradeKeepG, S.GradeKeepB);
        Group("GAME EFFECTS", StateOf("effects"));
        ToggleSwitch(B.HoldBloom, "BLOOM OFF");
        ToggleSwitch(B.HoldVolumetric, "VOLUMETRIC LIGHT OFF");
        ToggleSwitch(B.LampSpotsOff, "LAMP SPOTS OFF");
        Section("MOD COST");
        string measure = _measureSpan >= 0 ? "MEASURING, STAND STILL... (" + (_measureSpan + 1) + "/" + MeasureSpans + ")" : "MEASURE (18 S, LOOK FLIPS ON AND OFF)";
        if (_active || _measureSpan >= 0) { if (GUILayout.Button(measure, _sButton, GUILayout.Height(32f))) StartMeasure(); }
        else GUILayout.Label("TURN THE LOOK ON TO MEASURE", _sSmall, GUILayout.Height(32f));
        GUILayout.Space(8f);
        foreach (var result in _measureResults) { GUILayout.Label(result, _sHint); GUILayout.Space(4f); }
    }

    void DrawStatus()
    {
        int ok = _features.Count(f => f.State == 1), bad = _features.Count(f => f.State == 0), later = _features.Count(f => f.State < 0);
        GUILayout.Space(10f);
        GUILayout.Label(ok + " OF " + (ok + bad + later) + " AVAILABLE" + (later > 0 ? " " + Dot + " " + later + " CHECKED IN A WORLD" : "") + " " + Dot + " ENGINE " + Application.unityVersion.ToUpperInvariant(), _sHint);
        GUILayout.Space(6f);
        var order = _features.Where(f => f.Key == "pause").Concat(_features.Where(f => f.Key != "pause" && f.State == 0)).Concat(_features.Where(f => f.Key != "pause" && f.State != 0));
        foreach (var f in order)
        {
            string text = f.State == 0 ? f.Why : f.State < 0 ? "Checked a few seconds after a world is loaded." : f.Desc;
            if (f.Key == "pause" && f.State == 1 && InWorld && PauseBlocked() != null) text = PauseBlocked();
            GUILayout.BeginHorizontal(f.State == 0 ? _sRowBad : _sRow);
            var m = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(m, Marker(f.State));
            GUILayout.Space(10f);
            GUILayout.BeginVertical();
            GUILayout.Label(f.Name, _sLabel);
            GUILayout.Label(text, f.State == 0 ? _sStatusBad : _sStatusText);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(7f);
        }
    }

    void DrawTitleBar(Rect r)
    {
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, _txTitle);
        float x = r.x + 12f, cy = r.y + r.height * 0.5f;
        var name = new GUIContent("CAD STYLE");
        float nameWidth = _sTitle.CalcSize(name).x;
        GUI.Label(new Rect(x, r.y, nameWidth, r.height), name, _sTitle);
        x += nameWidth + 6f;
        var ver = new GUIContent("V" + Version);
        float verWidth = _sSmall.CalcSize(ver).x;
        GUI.Label(new Rect(x, r.y + 1f, verWidth, r.height), ver, _sSmallBox);
        x += verWidth + 10f;
        bool look = Switch(new Rect(x, cy - 12f, 60f, 24f), _active);
        if (look != _active) SetActive(look);
        var close = new Rect(r.xMax - 6f - 100f, cy - 15f, 100f, 30f);
        bool closing = GUI.Button(close, GUIContent.none, _sButton);
        GUI.Label(new Rect(close.x + 12f, close.y, 60f, close.height), "CLOSE", _sLabelBox);
        GUI.Label(new Rect(close.xMax - 30f, close.y - 1f, 24f, close.height), "X", _sBigX);
        var pauseSwitch = new Rect(close.x - 10f - 60f, cy - 12f, 60f, 24f);
        bool pause = Switch(pauseSwitch, _pauseOnOpen);
        if (pause != _pauseOnOpen)
        {
            _pauseOnOpen = pause;
            MarkChanged();
            ApplyPause(pause);
        }
        // A window carried from the menu into a world doesn't pause it (that would cut into the capsule landing); the label says so.
        bool noPause = _pauseOnOpen && InWorld && !_pausedByUs;
        var label = new GUIContent(noPause ? "NOT PAUSED" : "PAUSE");
        float labelWidth = _sSmall.CalcSize(label).x;
        GUI.Label(new Rect(pauseSwitch.x - 8f - labelWidth, r.y, labelWidth, r.height), label, noPause ? _sSmallRedBox : _sSmallBox);
        if (closing) SetPanelOpen(false);
    }

    void DrawTabs(Rect r)
    {
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, _txDark);
        const float pad = 6f, gap = 3f;
        float w = (r.width - pad * 2f - gap * 4f) / 5f;
        int bad = _features.Count(f => f.State == 0);
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool front = i == _tab;
            var t = new Rect(r.x + pad + i * (w + gap), front ? r.y + 6f : r.y + 10f, w, front ? 34f : 30f);
            if (GUI.Button(t, GUIContent.none, front ? _sTabFront : _sTabBack) && i != _tab)
            {
                Pin(_glide[_tab], _tabScroll[_tab].y);
                _deleteArmed = false;
                _tab = i;
                Pin(_glide[_tab], _tabScroll[_tab].y);
            }
            var style = front ? _sTabTextFront : _sTabTextBack;
            if (i == 4 && bad > 0)
            {
                var name = new GUIContent(TabNames[i]);
                var count = new GUIContent(bad.ToString(CultureInfo.InvariantCulture));
                float nw = style.CalcSize(name).x, cw = _sSmallRed.CalcSize(count).x, total = nw + 5f + 11f + 3f + cw, x0 = t.x + (t.width - total) * 0.5f;
                GUI.Label(new Rect(x0, t.y, nw, t.height), name, style);
                if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(x0 + nw + 5f, t.y + t.height * 0.5f - 5.5f, 11f, 11f), _txMarkBad);
                GUI.Label(new Rect(x0 + nw + 19f, t.y, cw, t.height), count, _sSmallRedBox);
            }
            else GUI.Label(t, TabNames[i], style);
        }
    }

    // The preset in use, on every tab: tuning on the Look tab still shows that the preset has changed.
    void DrawFooter(Rect r)
    {
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, _txTitle);
        var inner = new Rect(r.x + 12f, r.y, r.width - 24f, r.height);
        bool loaded = _activeSlot >= 0 && _slotV[_activeSlot] != null;
        if (!loaded) { GUI.Label(inner, "NO PRESET IN USE", _sFooter); return; }
        bool changed = !MatchesSlot(_activeSlot);
        GUI.Label(inner, "PRESET " + (_activeSlot + 1) + (changed ? " " + Dot + " CHANGED, HOLD ITS BUTTON TO SAVE" : ""), changed ? _sFooterChanged : _sFooter);
    }

    // ---- Weighted wheel scrolling, as in Workspacer (scroll-glide.js): a critically damped spring, 300 ms.
    // The wheel moves the target, which is not clamped; the view stops where it meets the end, without a jump.
    class GlideState
    {
        public float Pos, Target, Vel, Shown;
        public bool Animating;
        public float Last;
    }

    const float GlideOmega = 6f / 0.3f;
    const float WheelPixels = 24f;   // per unit of IMGUI's wheel delta (about 3 per notch); his call: 80 % of the first 30
    readonly GlideState[] _glide = { new GlideState(), new GlideState(), new GlideState(), new GlideState(), new GlideState() };
    Rect _viewRect;
    float _contentHeight;

    float ScrollMax { get { return Mathf.Max(0f, _contentHeight - _viewRect.height); } }

    static void Pin(GlideState g, float p)
    {
        g.Pos = g.Target = g.Shown = p;
        g.Vel = 0f;
        g.Animating = false;
    }

    void Glide(Event e)
    {
        var g = _glide[_tab];
        float max = ScrollMax, live = _tabScroll[_tab].y, now = Time.unscaledTime;
        // The scrollbar was dragged (even mid-glide), or the view was clamped: the glide continues from where the view really is.
        if (Mathf.Abs(live - g.Shown) > 1f) Pin(g, Mathf.Clamp(live, 0f, max));
        if (e.type == EventType.ScrollWheel && _viewRect.Contains(e.mousePosition))
        {
            float pixels = e.delta.y * WheelPixels;
            float from = g.Animating ? g.Target : g.Shown, wanted = from + pixels;
            bool blocked = !g.Animating && ((g.Shown <= 0f && wanted < 0f) || (g.Shown >= max && wanted > max));
            if (!blocked)
            {
                g.Target = wanted;
                if (!g.Animating) { g.Pos = g.Shown; g.Vel = 0f; g.Last = now; g.Animating = true; }
            }
            e.Use();
            return;
        }
        if (e.type != EventType.Repaint || !g.Animating) return;
        g.Pos = Mathf.Clamp(g.Pos, 0f, max);
        if ((g.Pos <= 0f && g.Target <= 0f) || (g.Pos >= max && g.Target >= max)) { Pin(g, g.Pos <= 0f && g.Target <= 0f ? 0f : max); _tabScroll[_tab].y = g.Shown; return; }
        float dt = Mathf.Clamp(now - g.Last, 0.001f, 0.05f);
        g.Last = now;
        float x = g.Pos - g.Target, b = g.Vel + GlideOmega * x, decay = Mathf.Exp(-GlideOmega * dt);
        float next = g.Target + (x + b * dt) * decay, nextVel = (g.Vel - GlideOmega * b * dt) * decay;
        if (g.Target < 0f && next <= 0f) Pin(g, 0f);
        else if (g.Target > max && next >= max) Pin(g, max);
        else
        {
            g.Pos = next;
            g.Vel = nextVel;
            if (g.Target >= 0f && g.Target <= max && Mathf.Abs(g.Target - g.Pos) < 0.5f && Mathf.Abs(g.Vel) < 8f) Pin(g, g.Target);
            else g.Shown = Mathf.Round(g.Pos);
        }
        _tabScroll[_tab].y = g.Shown;
    }

    void DrawPanel(int id)
    {
        if (Event.current.type == EventType.Repaint) _linkHover = false;
        try
        {
            DrawTitleBar(GUILayoutUtility.GetRect(PanelWidth, 38f));
            DrawTabs(GUILayoutUtility.GetRect(PanelWidth, 40f));
            Glide(Event.current);
            _tabScroll[_tab] = GUILayout.BeginScrollView(_tabScroll[_tab], false, true, GUIStyle.none, _skin != null ? _skin.verticalScrollbar : GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(_sContent);
            switch (_tab)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawLook(); break;
                case 2: DrawLights(); break;
                case 3: DrawAdvanced(); break;
                default: DrawStatus(); break;
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) _contentHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _viewRect = GUILayoutUtility.GetLastRect();
            DrawFooter(GUILayoutUtility.GetRect(PanelWidth, 30f));
        }
        catch (Exception e)
        {
            var text = e.ToString();
            if (text != _lastUiError) { _lastUiError = text; Log("ERROR " + text); }
        }
        GUI.DragWindow(new Rect(0f, 0f, PanelWidth, 38f));
    }

    void OnGUI()
    {
        if (!_panelOpen) return;
        MakeStyles();
        var keep = GUI.matrix;
        _panelScale = Mathf.Max(1f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(_panelScale, _panelScale, 1f));
        var rawEvent = Event.current.rawType;
        if (_pressSlot >= 0 && rawEvent == EventType.MouseUp) FinishPresetPress();
        if (rawEvent == EventType.MouseUp) PanelCursorGuard.PressStartedOnPanel = false;
        if (rawEvent == EventType.MouseDown && _panelRect.Contains(Event.current.mousePosition)) PanelCursorGuard.PressStartedOnPanel = true;
        float screenW = Screen.width / _panelScale, screenH = Screen.height / _panelScale;
        if (!_panelPlaced)
        {
            _panelPlaced = true;
            _panelRect = new Rect(screenW - PanelWidth - 16f, 0f, PanelWidth, screenH);
        }
        var keepSkin = GUI.skin;
        if (_skin != null) GUI.skin = _skin;
        _panelRect = GUILayout.Window(PanelWindowId, new Rect(_panelRect.x, 0f, PanelWidth, screenH), DrawPanel, GUIContent.none, _windowStyle, GUILayout.Width(PanelWidth), GUILayout.Height(screenH));
        GUI.skin = keepSkin;
        _panelRect.x = Mathf.Clamp(_panelRect.x, 0f, Mathf.Max(0f, screenW - PanelWidth));
        _panelRect.y = 0f;
        _panelRect.height = screenH;
        PanelCursorGuard.PanelRect = _panelRect;
        PanelCursorGuard.PanelScale = _panelScale;
        GUI.matrix = keep;
    }
}
