using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Command-line Android build.
// Usage: Unity -batchmode -quit -projectPath <project> -buildTarget Android -executeMethod Builder.PerformBuild
//        [-buildOutput Builds/KiteTangle.apk | Builds/KiteTangle-1.0-2.aab] [-versionCode 2] [-versionName 1.0]
// An .aab is a Play release and must be signed with the upload key from Keys/keystore.properties
// (storeFile, storePassword, keyAlias, keyPassword; Keys/ is git-ignored). An .apk falls back to debug signing.
// The passwords are only set in memory for the build and are never saved into ProjectSettings.
public static class Builder
{
    const string PackageId = "com.zekoclub.kitetangle";
    const string KeystoreProperties = "Keys/keystore.properties";
    const string IconPath = "Assets/Store/icon.png";                       // made by Tools/gen_icon.py
    const string AdaptiveBackground = "Assets/Store/adaptive-background.png";
    const string AdaptiveForeground = "Assets/Store/adaptive-foreground.png";

    public static void PerformBuild()
    {
        if (!Build(ArgValue("-buildOutput") ?? "Builds/KiteTangle.apk")) EditorApplication.Exit(1);
    }

    public static bool Build(string output)
    {
        bool release = output.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);

        PlayerSettings.companyName = "ZekoClub";
        PlayerSettings.productName = "Kite Tangle";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        EditorUserBuildSettings.buildAppBundle = release;
        // The game has its own loading look; Unity 6 no longer requires its splash.
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        ApplyIcons();

        if (int.TryParse(ArgValue("-versionCode"), out int code)) PlayerSettings.Android.bundleVersionCode = code;
        string name = ArgValue("-versionName");
        if (!string.IsNullOrEmpty(name)) PlayerSettings.bundleVersion = name;
        Debug.Log($"[Builder] {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}) -> {output}");

        if (!ApplySigning() && release)
        {
            Debug.LogError($"[Builder] No upload key ({KeystoreProperties} missing or incomplete). Play rejects debug-signed bundles.");
            return false;
        }

        if (Resources.Load<TextAsset>(LevelCache.BakedPath) == null)
        {
            Debug.LogWarning("[Builder] No baked levels: every level will be generated on the phone. Run Kite Tangle > Bake Levels.");
            if (release) return false;
        }

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) throw new Exception("No enabled scenes in Build Settings. Run Kite Tangle > Build Game Scene.");

        try
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"Build {report.summary.result}: {report.summary.totalSize} bytes -> {output}");
            return report.summary.result == BuildResult.Succeeded;
        }
        finally
        {
            // Keep the passwords out of memory-backed settings that the Editor could save later.
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasPass = "";
        }
    }

    // Desktop build for the screenshot tour (Tools/ui_tour.sh). The script keeps the desktop save it uses separate.
    public static void BuildLinuxTour()
    {
        string name = PlayerSettings.productName;
        PlayerSettings.productName = "Kite Tangle Tour";
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        bool runInBackground = PlayerSettings.runInBackground;
        PlayerSettings.runInBackground = true; // keep shooting when another window has focus
        try
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/Tour/tour.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            });
            Debug.Log($"Tour build {report.summary.result}");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
        finally
        {
            PlayerSettings.productName = name;
            PlayerSettings.runInBackground = runInBackground;
        }
    }

    // Flat icon for every legacy/round size, plus the Android adaptive layers (background 0, foreground 1).
    static void ApplyIcons()
    {
        var icon = LoadIcon(IconPath);
        if (icon == null)
        {
            Debug.LogWarning($"[Builder] No app icon at {IconPath}. Run: python3 Tools/gen_icon.py");
            return;
        }
        foreach (var kind in new[] { AndroidPlatformIconKind.Legacy, AndroidPlatformIconKind.Round })
        {
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var slot in slots) slot.SetTexture(icon, 0);
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
        }
        var bg = LoadIcon(AdaptiveBackground);
        var fg = LoadIcon(AdaptiveForeground);
        if (bg == null || fg == null) return;
        var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
        foreach (var slot in adaptive)
        {
            slot.SetTexture(bg, 0);
            slot.SetTexture(fg, 1);
        }
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive, adaptive);
    }

    // Icons must be uncompressed and without mipmaps, or Unity scales a blurry copy.
    static Texture2D LoadIcon(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return null;
        if (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled || !importer.alphaIsTransparency)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Uses the upload key if Keys/keystore.properties exists. Returns false (debug signing) otherwise.
    static bool ApplySigning()
    {
        PlayerSettings.Android.useCustomKeystore = false;
        if (!File.Exists(KeystoreProperties)) return false;

        var props = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(KeystoreProperties))
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && !line.TrimStart().StartsWith("#")) props[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        if (!props.TryGetValue("storeFile", out var store) || !props.TryGetValue("storePassword", out var storePass) ||
            !props.TryGetValue("keyAlias", out var alias) || !props.TryGetValue("keyPassword", out var keyPass))
            return false;

        if (!Path.IsPathRooted(store)) store = Path.Combine(Path.GetDirectoryName(KeystoreProperties), store);
        if (!File.Exists(store)) return false;

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = Path.GetFullPath(store);
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        Debug.Log($"[Builder] Signing with upload key '{alias}'.");
        return true;
    }

    static string ArgValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, flag);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }
}
