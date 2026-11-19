using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// カメラ判定プログラムをUnityと一緒に起動し、アプリ終了時に停止します。
    /// EditorではPythonスクリプトを起動します。配布版は起動用バッチが同じ役割を持ちます。
    /// </summary>
    public sealed class FruitCatchTrackerProcess : MonoBehaviour
    {
        [SerializeField] private bool startAutomatically = true;
        [SerializeField] private string editorPythonCommand = "python";
        [SerializeField] private string editorScriptRelativePath = "Tools/face_tracker_registered.py";

        private static Process trackerProcess;
        private static bool quitHookRegistered;

        private void Awake()
        {
            if (!quitHookRegistered)
            {
                Application.quitting += StopTracker;
                quitHookRegistered = true;
            }

            if (startAutomatically)
                StartTracker();
        }

        public void StartTracker()
        {
            if (trackerProcess != null && !trackerProcess.HasExited)
                return;

            try
            {
                ProcessStartInfo info = CreateStartInfo();
                if (info == null)
                    return;

                trackerProcess = Process.Start(info);
                if (trackerProcess == null)
                    Debug.LogError("FruitCatchTrackerProcess: 判定プログラムを起動できませんでした。");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"FruitCatchTrackerProcess: 判定プログラムの起動に失敗しました。開発用のSpaceキーで進行できます。\n{exception.Message}");
            }
        }

        private ProcessStartInfo CreateStartInfo()
        {
#if UNITY_EDITOR
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string scriptPath = Path.GetFullPath(Path.Combine(
                projectRoot ?? string.Empty, editorScriptRelativePath));
            if (!File.Exists(scriptPath))
            {
                Debug.LogWarning($"FruitCatchTrackerProcess: Pythonスクリプトが見つかりません: {scriptPath}");
                return null;
            }

            return CreateHiddenStartInfo(editorPythonCommand, Quote(scriptPath), projectRoot);
#else
            // 配布版は Start FruitCatch.bat がPythonを起動してからゲームを開く。
            return null;
#endif
        }

        private static ProcessStartInfo CreateHiddenStartInfo(string fileName, string arguments, string workingDirectory)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static void StopTracker()
        {
            if (trackerProcess == null)
                return;

            try
            {
                if (!trackerProcess.HasExited)
                    trackerProcess.Kill();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"FruitCatchTrackerProcess: 終了処理に失敗しました: {exception.Message}");
            }
            finally
            {
                trackerProcess.Dispose();
                trackerProcess = null;
            }
        }
    }
}
