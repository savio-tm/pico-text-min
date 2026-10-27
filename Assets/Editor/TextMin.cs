// Minimal PICO Spatial text repro. Batch: Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod <method>
//   TextMin.Setup  PICO Spatial project setup, as the PICO Unity SDK Portal does it (once)
//   TextMin.Apk    the one-text scene, then Build/TextMin.apk
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // a world-space Canvas, 1 m ahead at 1.4 m (Full Space: the origin is the floor where you start), 1 unit = 1 mm
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)canvasGo.transform;
        crt.position = new Vector3(0, 1.4f, 1f); crt.sizeDelta = new Vector2(400, 100); crt.localScale = Vector3.one * 0.001f;

        // one TextMeshPro text, 40 mm, TMP's default font
        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform; trt.SetParent(crt, false); trt.sizeDelta = new Vector2(400, 100);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset")
            ?? throw new Exception("TextMin: no TMP default font (run build.ps1 -Setup: it imports TMP Essential Resources)");
        text.text = "Hello PICO"; text.fontSize = 40; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("TextMin: scene " + ScenePath);
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
