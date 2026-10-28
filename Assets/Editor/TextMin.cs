// Minimal PICO Spatial text repro. Batch: Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod <method>
//   TextMin.Setup  PICO Spatial project setup, as the PICO Unity SDK Portal does it (once)
//   TextMin.Apk    the one-text scene, then Build/TextMin.apk
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ByteDance.PICO.SpatialAdapter.Component;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class TextMin
{
    const string ScenePath = "Assets/Scenes/TextMin.unity";

    // ---------------------------------------------------------------- setup

    [MenuItem("TextMin/Setup PICO Spatial")]
    public static void Setup()
    {
        // 1. PICO Unity SDK Portal > PICO Spatial > Configs: the Apply button of each item
        var portalType = Type("ByteDance.PICO.XR.Editor.PXR_PortalEditor");
        var portal = ScriptableObject.CreateInstance(portalType);
        try
        {
            foreach (var name in new[] { "applyBuildTargetAction", "applyMinAndroidAPIAction", "applyARM64Action", "applyPICOXRPluginAction", "applyPICOAppModeSpatialAdapterAction" })
            {
                var action = (Action)portalType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(portal);
                try { action(); Debug.Log("TextMin: Portal " + name); }
                catch (Exception e) { Debug.LogWarning($"TextMin: Portal {name}: {e.Message} (opens a window in the GUI; the setting itself is applied)"); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(portal); }

        // 2. The Spatial Adapter's "Confirm Project Setup" (SpatialAdapterSetup.Setup), without its dialog
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        var manifests = Path.GetFullPath("Packages/com.bytedance.pico.spatialadapter/Manifest");
        Directory.CreateDirectory("Assets/Plugins/Android");
        foreach (var f in Directory.GetFiles(manifests)) File.Copy(f, Path.Combine("Assets/Plugins/Android", Path.GetFileName(f)), true);
        AssetDatabase.Refresh();

        // 3. Project Validation > Fix All (Android)
        var validator = Type("Unity.XR.CoreUtils.Editor.BuildValidator");
        var ruleType = validator.Assembly.GetType("Unity.XR.CoreUtils.Editor.BuildValidationRule");
        var issues = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(ruleType));
        validator.GetMethod("GetCurrentValidationIssues", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new[] { issues, (object)BuildTargetGroup.Android });
        var fixable = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ruleType));
        foreach (var rule in (IEnumerable)issues) if ((bool)ruleType.GetProperty("FixItAutomatic").GetValue(rule)) fixable.Add(rule);
        if (fixable.Count > 0) validator.GetMethod("FixIssues", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { fixable, "Fix All" });
        Debug.Log($"TextMin: Project Validation fixed {fixable.Count}");

        // 4. PICO Spatial settings: default spatial camera = Unconstrained (Full Space)
        var settingsType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .First(t => t.Name == "SpatialAdapterRuntimeSettings" && t.GetMethod("GetOrCreateSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null);
        var settings = (UnityEngine.Object)settingsType.GetMethod("GetOrCreateSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var so = new SerializedObject(settings);
        so.FindProperty("m_defaultSpatialCameraConfiguration").objectReferenceValue =
            AssetDatabase.LoadMainAssetAtPath("Packages/com.bytedance.pico.spatialadapter/Resources/Default Unconstrained Spatial Camera Configuration.asset");
        so.ApplyModifiedPropertiesWithoutUndo();

        // build speed only (a repro): IL2CPP debug, "faster (smaller) builds"
        PlayerSettings.SetIl2CppCompilerConfiguration(UnityEditor.Build.NamedBuildTarget.Android, Il2CppCompilerConfiguration.Debug);
        PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.productName = "PICO Text Min";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.vibelab.textmin");

        // TMP Essential Resources (the default font) come in with build.ps1 -Setup: Unity's -importPackage
        AssetDatabase.SaveAssets();
        Debug.Log("TextMin: setup done");
    }

    static Type Type(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null) ?? throw new Exception("TextMin: no " + fullName);

    // ---------------------------------------------------------------- the scene: one text

    [MenuItem("TextMin/Build scene")]
    public static void Scene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // one row per issue in the report, 40 mm text, rows 100 mm apart, 1 m ahead (Full Space: the origin is the floor where you start)
        var bitmap = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/LiberationSans Bitmap.asset");   // SMOOTH_HINTED, static atlas
        var sdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/LiberationSans Made SDF.asset");    // SDFAA, static atlas
        if (bitmap == null || sdf == null) throw new Exception("TextMin: missing font assets in Assets/Fonts");
        var later = new GameObject("Runtime changes").AddComponent<SetTextAtStart>();

        // world-space Canvas, 1 unit = 1 mm, for the UGUI rows
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)canvasGo.transform;
        crt.position = new Vector3(0, 1.4f, 1f); crt.sizeDelta = new Vector2(700, 600); crt.localScale = Vector3.one * 0.001f;

        // issue 1: bitmap font on a Canvas
        Text(crt, "1 Bitmap UGUI", bitmap, "1 Bitmap font (UGUI)", 250);
        // issue 2: SDF font on a Canvas (exported with alphaMode BLEND)
        Text(crt, "2 SDF UGUI", sdf, "2 SDF font (UGUI)", 150);
        // issue 3: 3D TextMeshPro, SDF with a cutout copy of TMP's shader (alphaMode MASK), orange face colour
        var cutout = CutoutMaterial(sdf, new Color(1f, 0.55f, 0.1f));
        Text3D("3 SDF 3D cutout", sdf, cutout, "3 SDF 3D, cutout, orange", 1.45f);
        // issue 3: the same, inactive at export and switched on 1 s after launch
        var hidden = Text3D("4 Hidden at export", sdf, cutout, "4 Hidden at export, on at 1 s", 1.35f);
        hidden.gameObject.SetActive(false);
        later.Show = new[] { hidden.gameObject };
        // runtime strings (names, counts): an SDF UGUI text whose string is set 1 s after launch
        later.Text = Text(crt, "5 SDF UGUI runtime", sdf, "5 Set at runtime: --", -150);
        later.Value = "5 Set at runtime: 12:34";
        // PICO's own text component, the candidate: string also set 1 s after launch, translucent background
        later.Natives = new[]
        {
            NativeText("6 Native text", "6 Native text, as exported", 1.05f, new Color(0, 0, 0, 0.4f), 0),
            // localized strings: English at export, Chinese at 1 s; opaque background with rounded corners
            NativeText("7 Native text CJK", "7 Native text, English at export", 0.95f, new Color(0.15f, 0.15f, 0.2f, 1f), 0.01f),
        };
        later.NativeValues = new[] { "6 Native text, set at 1 s", "7 原生文字：中文，1 秒后设置" };

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("TextMin: scene " + ScenePath);
    }

    /// <summary>A SpatialAdapterNativeText line, white, 1 m ahead at height y m, on a 700 x 80 mm background.</summary>
    static SpatialAdapterNativeText NativeText(string name, string s, float y, Color background, float cornerRadius)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(0, y, 1f);
        var t = go.AddComponent<SpatialAdapterNativeText>();
        var so = new SerializedObject(t);
        so.FindProperty("_text").stringValue = s;
        so.FindProperty("_textColor").colorValue = Color.white;
        // the component's own constant says 1 pt = 0.352806 mm, so 40 mm would be 113 pt, but 113 pt drew ~3x the TMP rows on the emulator
        so.FindProperty("_textSize").intValue = Mathf.RoundToInt(40f / 0.352806f / 3f);
        so.FindProperty("_backgroundColor").colorValue = background;
        so.FindProperty("_backgroundSize").vector2Value = new Vector2(0.7f, 0.08f);  // meters
        so.FindProperty("_backgroundCornerRadius").floatValue = cornerRadius;       // meters
        so.FindProperty("_justification").enumValueIndex = (int)TextJustification.Center;
        so.ApplyModifiedPropertiesWithoutUndo();
        return t;
    }

    /// <summary>A 3D TextMeshPro line (MeshRenderer), 40 mm tall, 1 m ahead at height y m.</summary>
    static TMP_Text Text3D(string name, TMP_FontAsset font, Material material, string s, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        // 3D TextMeshPro meshes are 1/10 the size of TextMeshProUGUI ones at the same font size: 1 unit = 10 mm here
        var rt = (RectTransform)go.transform; rt.position = new Vector3(0, y, 1f); rt.sizeDelta = new Vector2(70, 8); rt.localScale = Vector3.one * 0.01f;
        var t = go.AddComponent<TextMeshPro>();
        t.font = font; t.fontSharedMaterial = material; t.text = s; t.fontSize = 40; t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    /// <summary>The font's material on a copy of TMP's Distance Field shader tagged RenderType=TransparentCutout (exports as glTF alphaMode MASK).</summary>
    static Material CutoutMaterial(TMP_FontAsset font, Color face)
    {
        const string shaderPath = "Assets/Shaders/TMP_SDF Cutout.shader", matPath = "Assets/Shaders/LiberationSans Made SDF Cutout.mat";
        if (!File.Exists(shaderPath))
        {
            Directory.CreateDirectory("Assets/Shaders");
            var src = File.ReadAllText("Assets/TextMesh Pro/Shaders/TMP_SDF.shader")
                .Replace("Shader \"TextMeshPro/Distance Field\"", "Shader \"TextMeshPro/Distance Field Cutout\"")   // UnityGLTF exports only "TextMeshPro*" shaders as text materials
                .Replace("\"RenderType\"=\"Transparent\"", "\"RenderType\"=\"TransparentCutout\"")
                .Replace("#include \"TMPro", "#include \"Assets/TextMesh Pro/Shaders/TMPro");
            File.WriteAllText(shaderPath, src);
            AssetDatabase.ImportAsset(shaderPath);
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(font.material) { shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetColor("_FaceColor", face);
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>An empty full-size RectTransform under the canvas, to hold one text.</summary>
    static RectTransform Holder(RectTransform canvas, string name)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(canvas, false); rt.sizeDelta = canvas.sizeDelta;
        return rt;
    }

    /// <summary>A TextMeshProUGUI line on the canvas, 40 mm tall, centred at y mm.</summary>
    static TMP_Text Text(RectTransform canvas, string name, TMP_FontAsset font, string s, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(canvas, false); rt.sizeDelta = new Vector2(600, 80); rt.anchoredPosition = new Vector2(0, y);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = font; t.text = s; t.fontSize = 40; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    /// <summary>LiberationSans (from TMP Essential Resources) made into a static font asset by script (TMP_FontAsset.CreateFontAsset),
    /// printable ASCII; clearDynamic is TMP's "Clear Dynamic Data On Build" (on by default for a font made this way).</summary>
    static TMP_FontAsset MadeFont(string path, UnityEngine.TextCore.LowLevel.GlyphRenderMode mode, int pointSize, int padding, int atlas, bool clearDynamic)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var have = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (have != null) return have;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var ttf = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        var font = TMP_FontAsset.CreateFontAsset(ttf, pointSize, padding, mode, atlas, atlas, AtlasPopulationMode.Dynamic, false);
        font.TryAddCharacters(new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray()), out _);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        font.name = name; font.atlasTexture.name = font.name + " Atlas"; font.material.name = font.name + " Material";
        AssetDatabase.CreateAsset(font, path);
        AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        AssetDatabase.AddObjectToAsset(font.material, font);
        var so = new SerializedObject(font); so.FindProperty("m_ClearDynamicDataOnBuild").boolValue = clearDynamic; so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return font;
    }

    /// <summary>An exact copy of a font asset at another path (outside TMP's Resources folder).</summary>
    static TMP_FontAsset CopyOf(TMP_FontAsset font, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(font), path);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
    }

    // ---------------------------------------------------------------- build

    public static void Apk()
    {
        Scene();
        // PICO's exporter never deletes old export files: start clean
        if (AssetDatabase.IsValidFolder("Assets/StreamingAssets/SpatialAdapterExport")) AssetDatabase.DeleteAsset("Assets/StreamingAssets/SpatialAdapterExport");
        EditorUserBuildSettings.buildAppBundle = false;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Build/TextMin.apk", target = BuildTarget.Android });
        Debug.Log($"TextMin: APK {report.summary.result}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
