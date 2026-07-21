using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FaceSlapper.EditorTools
{
    /// <summary>
    /// 一键打包 Windows Player（用于本地双开联机测试）。
    /// 输出到工程根目录 Builds/Windows/FaceSlapper.exe。
    /// </summary>
    public static class FaceSlapperBuild
    {
        private const string OutputPath = "Builds/Windows/FaceSlapper.exe";

        [MenuItem("FaceSlapper/Build Player (Windows)", priority = 3)]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[FaceSlapper] 打包成功: {OutputPath}（{report.summary.totalSize / (1024 * 1024)} MB）");
            else
                Debug.LogError($"[FaceSlapper] 打包失败: {report.summary.result}");
        }
    }
}
