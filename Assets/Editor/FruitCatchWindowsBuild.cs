#if UNITY_EDITOR
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
        Directory.CreateDirectory(OutputRoot);
        string[] scenes =
        {
            "Assets/Scenes/Preparation.unity",
            "Assets/Scenes/MainGame.unity"
        };

        BuildReport report = BuildPipeline.BuildPlayer(
            scenes,
            Path.Combine(OutputRoot, "FruitCatch.exe"),
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"FruitCatch Windowsビルドに失敗しました: {report.summary.result}");
            return;
        }

        CopyDistributionFiles();
        AssetDatabase.Refresh();
        Debug.Log($"FruitCatch配布版を作成しました: {Path.GetFullPath(OutputRoot)}");
        EditorUtility.RevealInFinder(Path.GetFullPath(OutputRoot));
    }

    private static void CopyDistributionFiles()
    {
        string distribution = "Tools/Distribution";
        CopyFile(Path.Combine(distribution, "Setup-FruitCatch.ps1"), Path.Combine(OutputRoot, "Setup-FruitCatch.ps1"));
        CopyFile(Path.Combine(distribution, "Start-FruitCatch.ps1"), Path.Combine(OutputRoot, "Start-FruitCatch.ps1"));
        CopyFile(Path.Combine(distribution, "Setup FruitCatch.bat"), Path.Combine(OutputRoot, "Setup FruitCatch.bat"));
        CopyFile(Path.Combine(distribution, "Start FruitCatch.bat"), Path.Combine(OutputRoot, "Start FruitCatch.bat"));
        CopyFile(Path.Combine(distribution, "はじめに.txt"), Path.Combine(OutputRoot, "はじめに.txt"));

        string tracking = Path.Combine(OutputRoot, "Tracking");
        Directory.CreateDirectory(tracking);
        Directory.CreateDirectory(Path.Combine(tracking, "face_models"));
        CopyFile("Tools/face_tracker_registered.py", Path.Combine(tracking, "face_tracker_registered.py"));
        CopyFile("Tools/requirements.txt", Path.Combine(tracking, "requirements.txt"));
        CopyFile("Tools/camera_config.json", Path.Combine(tracking, "camera_config.json"));
        foreach (string model in Directory.GetFiles("Tools/face_models"))
            CopyFile(model, Path.Combine(tracking, "face_models", Path.GetFileName(model)));
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? OutputRoot);
        File.Copy(source, destination, true);
    }
}
#endif
