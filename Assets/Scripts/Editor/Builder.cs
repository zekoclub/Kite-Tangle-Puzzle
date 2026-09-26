using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Command-line Android build.
// Usage: Unity -batchmode -quit -projectPath <project> -buildTarget Android -executeMethod Builder.PerformBuild [-buildOutput Builds/KiteTangle.apk]
public static class Builder
{
    const string PackageId = "com.zekoclub.kitetangle";

    public static void PerformBuild()
    {
        if (!Build(ArgValue("-buildOutput") ?? "Builds/KiteTangle.apk")) EditorApplication.Exit(1);
    }

    public static bool Build(string output)
    {
        PlayerSettings.companyName = "ZekoClub";
        PlayerSettings.productName = "Kite Tangle";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        EditorUserBuildSettings.buildAppBundle = output.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) throw new Exception("No enabled scenes in Build Settings. Run Kite Tangle > Build Game Scene.");

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

    static string ArgValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, flag);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }
}
