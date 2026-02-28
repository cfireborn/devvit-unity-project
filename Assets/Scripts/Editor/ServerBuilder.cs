using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

public static class ServerBuilder
{
    const string OutputPath = "Builds/LinuxServer/GameServer";

    [MenuItem("Build/Build Linux Server")]
    public static void BuildLinuxServer()
    {
        string[] scenes = GetEnabledScenes();

        var options = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = OutputPath,
            target           = BuildTarget.StandaloneLinux64,
            subtarget        = (int)StandaloneBuildSubtarget.Server,
            options          = BuildOptions.None,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"Server build succeeded → {OutputPath}");
        else
            Debug.LogError($"Server build FAILED: {report.summary.totalErrors} error(s)");
    }

    static string[] GetEnabledScenes()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled)
                scenes.Add(scene.path);
        return scenes.ToArray();
    }
}
