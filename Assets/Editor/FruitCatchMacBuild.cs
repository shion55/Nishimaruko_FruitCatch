#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FruitCatchMacBuild
{
    private const string OutputRoot = "Builds/FruitCatch-macOS";
    private const string DistributionRoot = "Tools/Distribution/macOS";
    private const BuildTarget Target = BuildTarget.StandaloneOSX;
    private const string PendingBuild = "FruitCatch.MacBuild.Pending";
    private const string PreviousTarget = "FruitCatch.MacBuild.Target";
    private const string PreviousProfile = "FruitCatch.MacBuild.Profile";

    [MenuItem("FruitCatch/macOS配布版をビルド")]
    public static void Build()
    {
        if (SessionState.GetBool(PendingBuild, false)) return;
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, Target))
        {
            ReportFailure("Unity Hub で Mac Build Support (Mono) を追加してください。");
            return;
        }

        BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
        BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
        if (activeTarget == Target && activeProfile == null)
        {
            RunBuild();
            return;
        }
        if (Application.isBatchMode)
        {
            ReportFailure("バッチビルドには -buildTarget StandaloneOSX を指定してください。");
            return;
        }

        SessionState.SetInt(PreviousTarget, (int)activeTarget);
        SessionState.SetString(PreviousProfile, activeProfile != null ? AssetDatabase.GetAssetPath(activeProfile) : "");
        SessionState.SetBool(PendingBuild, true);
        if (activeProfile != null)
            BuildProfile.SetActiveBuildProfile(null);
        if (EditorUserBuildSettings.activeBuildTarget != Target &&
            !EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Standalone, Target))
        {
            SessionState.SetBool(PendingBuild, false);
            RestoreEditorTarget();
            ReportFailure("Unity Editor を macOS ターゲットへ切り替えられませんでした。");
            return;
        }
        ResumePendingBuild();
    }

    [InitializeOnLoadMethod]
    private static void ResumePendingBuild()
    {
        if (!SessionState.GetBool(PendingBuild, false)) return;
        EditorApplication.update -= BuildWhenReady;
        EditorApplication.update += BuildWhenReady;
    }

    private static void BuildWhenReady()
    {
        if (EditorUserBuildSettings.activeBuildTarget != Target ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        EditorApplication.update -= BuildWhenReady;
        SessionState.SetBool(PendingBuild, false);
        try
        {
            RunBuild();
        }
        finally
        {
            RestoreEditorTarget();
        }
    }

    private static void RestoreEditorTarget()
    {
        string path = SessionState.GetString(PreviousProfile, "");
        var target = (BuildTarget)SessionState.GetInt(PreviousTarget, (int)Target);
        SessionState.EraseString(PreviousProfile);
        SessionState.EraseInt(PreviousTarget);

        BuildProfile profile = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
        if (profile != null)
            BuildProfile.SetActiveBuildProfile(profile);
        else if (EditorUserBuildSettings.activeBuildTarget != target)
            EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(target), target);
    }

    private static void RunBuild()
    {
        try
        {
            BuildDistribution();
            Debug.Log("FruitCatch macOS 配布版を作成しました。 " + Path.GetFullPath(OutputRoot));
            if (!Application.isBatchMode)
                EditorUtility.RevealInFinder(Path.GetFullPath(OutputRoot));
        }
        catch (Exception exception)
        {
            ReportFailure(exception.ToString());
        }
    }

    private static void ReportFailure(string message)
    {
        Debug.LogError("FruitCatch macOS ビルドに失敗しました。 " + message);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    private static void BuildDistribution()
    {
        // The platform extension is optional on Windows. Resolve its macOS-only setting after the module check.
        Type macSettings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
        PropertyInfo architecture = macSettings?.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo createXcodeProject = macSettings?.GetProperty("createXcodeProject",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (architecture == null || !architecture.CanWrite || createXcodeProject == null || !createXcodeProject.CanWrite)
            throw new InvalidOperationException("macOS のビルドアーキテクチャまたは出力形式を取得できません。");

        object previousArchitecture = architecture.GetValue(null);
        object universalArchitecture = Enum.Parse(architecture.PropertyType, "x64ARM64");
        bool previousXcode = (bool)createXcodeProject.GetValue(null);
        var standalone = NamedBuildTarget.Standalone;
        ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(standalone);
        string previousMicrophoneDescription = PlayerSettings.iOS.microphoneUsageDescription;
        Directory.CreateDirectory(OutputRoot);

        BuildReport report;
        try
        {
            if (!Equals(previousArchitecture, universalArchitecture))
                architecture.SetValue(null, universalArchitecture);
            if (previousXcode)
                createXcodeProject.SetValue(null, false);
            if (previousBackend != ScriptingImplementation.Mono2x)
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.iOS.microphoneUsageDescription = "FruitCatchで声の高さと大きさを使ってゲームを操作します。";
            report = BuildPipeline.BuildPlayer(
                new[] { "Assets/Scenes/Preparation.unity", "Assets/Scenes/MainGame.unity" },
                Path.Combine(OutputRoot, "FruitCatch.app"), Target, BuildOptions.None);
        }
        finally
        {
            PlayerSettings.iOS.microphoneUsageDescription = previousMicrophoneDescription;
            if (previousBackend != ScriptingImplementation.Mono2x)
                PlayerSettings.SetScriptingBackend(standalone, previousBackend);
            if (previousXcode)
                createXcodeProject.SetValue(null, true);
            if (!Equals(previousArchitecture, universalArchitecture))
                architecture.SetValue(null, previousArchitecture);
        }

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Unity BuildPlayer: {report.summary.result}");

        foreach (string file in new[] { "Setup FruitCatch.command", "Start FruitCatch.command", "はじめに.txt" })
            CopyFile(Path.Combine(DistributionRoot, file), Path.Combine(OutputRoot, file));

        string tracking = Path.Combine(OutputRoot, "Tracking");
        foreach (string file in new[] { "face_tracker_registered.py", "fruitcatch_camera.py",
                     "fruitcatch_tracking.py", "fruitcatch_preview.py" })
            CopyFile(Path.Combine("Tools", file), Path.Combine(tracking, file));
        foreach (string file in new[] { "fruitcatch_mac_launcher.py", "requirements-macos.txt" })
            CopyFile(Path.Combine(DistributionRoot, file), Path.Combine(tracking, file));
        string config = Path.Combine(tracking, "camera_config.json");
        if (!File.Exists(config))
            CopyFile(Path.Combine(DistributionRoot, "camera_config.json"), config);

        foreach (string file in new[] { "face_detection_yunet_2023mar.onnx", "pose_landmarker_lite.task",
                     "face_recognition_sface_2021dec.onnx", "LICENSE-YuNet.txt" })
            CopyFile(Path.Combine("Tools", "face_models", file), Path.Combine(tracking, "face_models", file));

        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            using var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{Path.Combine(OutputRoot, "Setup FruitCatch.command")}\" " +
                            $"\"{Path.Combine(OutputRoot, "Start FruitCatch.command")}\"",
                UseShellExecute = false
            });
            chmod?.WaitForExit();
            if (chmod == null || chmod.ExitCode != 0)
                Debug.LogWarning("起動用 .command の実行権限を「はじめに.txt」の手順で設定してください。");
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, overwrite: true);
    }
}
#endif
