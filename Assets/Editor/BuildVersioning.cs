using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public sealed class BuildVersioning : IPreprocessBuildWithReport
{
    const string VersionAssetPath = "Assets/Resources/BuildVersion.txt";

    static BuildVersioning()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        IncrementVersion(report.summary.platform == BuildTarget.WebGL);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            IncrementVersion(false);
    }

    static void IncrementVersion(bool updateWebGLProductVersion)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string versionPath = Path.Combine(projectRoot, VersionAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(versionPath));
        string previousVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : string.Empty;
        string date = DateTime.Now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        string prefix = date + ".";
        int run = 1;

        if (previousVersion.StartsWith(prefix, StringComparison.Ordinal))
        {
            int separator = previousVersion.IndexOf('-', prefix.Length);
            if (separator > prefix.Length &&
                int.TryParse(previousVersion.Substring(prefix.Length, separator - prefix.Length), out int previousRun))
                run = previousRun + 1;
        }

        string version = $"{date}.{run}-{GetCommitSha(projectRoot, previousVersion)}";
        File.WriteAllText(versionPath, version + Environment.NewLine);
        AssetDatabase.ImportAsset(VersionAssetPath, ImportAssetOptions.ForceSynchronousImport);
        if (updateWebGLProductVersion)
            PlayerSettings.bundleVersion = version;
    }

    static string GetCommitSha(string projectRoot, string previousVersion)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000) && process.ExitCode == 0)
            {
                string sha = process.StandardOutput.ReadToEnd().Trim();
                if (sha.Length >= 8)
                    return sha.Substring(0, 8).ToLowerInvariant();
            }
        }
        catch
        {
        }

        int separator = previousVersion.LastIndexOf('-');
        return separator >= 0 && previousVersion.Length >= separator + 9
            ? previousVersion.Substring(separator + 1, 8)
            : "00000000";
    }
}
