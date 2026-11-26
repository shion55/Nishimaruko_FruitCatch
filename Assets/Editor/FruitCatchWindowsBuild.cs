#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FruitCatchWindowsBuild
{
    private const string OutputRoot = "Builds/FruitCatch-Windows";

    [MenuItem("FruitCatch/Windows配布版をビルド")]
    public static void Build()
    {
        string output = OutputRoot;
        string[] args = Environment.GetCommandLineArgs();
        int outputArgument = Array.IndexOf(args, "-fruitCatchOutput");
        if (Application.isBatchMode && outputArgument >= 0 && outputArgument + 1 < args.Length)
            output = args[outputArgument + 1];
        Directory.CreateDirectory(output);
        string[] scenes =
        {
            "Assets/Scenes/Preparation.unity",
            "Assets/Scenes/MainGame.unity"
        };

        BuildReport report = BuildPipeline.BuildPlayer(
            scenes,
            Path.Combine(output, "FruitCatch.exe"),
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"FruitCatch Windowsビルドに失敗しました: {report.summary.result}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        CopyDistributionFiles(output);
        AssetDatabase.Refresh();
        Debug.Log($"FruitCatch配布版を作成しました: {Path.GetFullPath(output)}");
        if (!Application.isBatchMode)
            EditorUtility.RevealInFinder(Path.GetFullPath(output));
    }

    private static void CopyDistributionFiles(string output)
    {
        string distribution = "Tools/Distribution";
        foreach (string file in new[] { "Setup-FruitCatch.ps1", "Setup FruitCatch.bat", "Start FruitCatch.bat", "はじめに.txt" })
            CopyFile(Path.Combine(distribution, file), Path.Combine(output, file));

        string tracking = Path.Combine(output, "Tracking");
        Directory.CreateDirectory(tracking);
        Directory.CreateDirectory(Path.Combine(tracking, "face_models"));
        foreach (string file in new[] { "face_tracker_registered.py", "fruitcatch_camera.py", "fruitcatch_tracking.py",
                     "fruitcatch_preview.py", "fruitcatch_launcher.py", "requirements.txt" })
            CopyFile(Path.Combine("Tools", file), Path.Combine(tracking, file));
        string config = Path.Combine(tracking, "camera_config.json");
        if (!File.Exists(config))
            CopyFile("Tools/camera_config.json", config);
        foreach (string model in new[] { "face_detection_yunet_2023mar.onnx", "pose_landmarker_lite.task",
                     "face_recognition_sface_2021dec.onnx", "LICENSE-YuNet.txt" })
            CopyFile(Path.Combine("Tools/face_models", model), Path.Combine(tracking, "face_models", model));
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? OutputRoot);
        File.Copy(source, destination, true);
    }
}
#endif
