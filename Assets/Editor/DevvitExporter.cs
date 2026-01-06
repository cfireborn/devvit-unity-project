using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using static UnityEditor.PlayerSettings.WebGL;

namespace Devvit.Editor
{
    /// <summary>
    /// Automates the Devvit WebGL export flow: run two builds with different compression settings
    /// and copy the required artifacts to the provided output directory.
    /// </summary>
    public static class DevvitExporter
    {
        private const string OutputArg = "-devvitOutput";
        private const string BaseNameArg = "-devvitBuildName";

        [MenuItem("Devvit/Export Devvit WebGL Bundle")]
        public static void ExportFromMenu()
        {
            RunExport(DevvitExportOptions.CreateWithDefaults());
        }

        /// <summary>
        /// Entry point for command line usage. This is invoked via -executeMethod.
        /// </summary>
        public static void ExportForDevvit()
        {
            RunExport(DevvitExportOptions.FromCommandLine());
        }

        private static void RunExport(DevvitExportOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            EnsureWebGLBuildTarget();
            PlayerSettings.WebGL.decompressionFallback = true;

            string[] enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new InvalidOperationException("Devvit export failed: no enabled scenes found in EditorBuildSettings.");
            }

            var gzipDirectory = Path.Combine(options.WorkingDirectory, "WebGL-GZip");
            var disabledDirectory = Path.Combine(options.WorkingDirectory, "WebGL-Disabled");

            PrepareDirectory(gzipDirectory);
            PrepareDirectory(disabledDirectory);
            Directory.CreateDirectory(options.OutputDirectory);

            BuildVariant(
                enabledScenes,
                gzipDirectory,
                WebGLCompressionFormat.Gzip,
                "First pass (GZip)");
            CopyArtifacts(gzipDirectory, options.OutputDirectory, options.ArtifactBaseName, ".data.unityweb", ".wasm.unityweb");

            BuildVariant(
                enabledScenes,
                disabledDirectory,
                WebGLCompressionFormat.Disabled,
                "Second pass (Disabled compression)");
            CopyArtifacts(disabledDirectory, options.OutputDirectory, options.ArtifactBaseName, ".framework.js");

            AssetDatabase.Refresh();
            Debug.Log($"Devvit export complete. Files copied to: {options.OutputDirectory}");
        }

        private static void EnsureWebGLBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                return;
            }

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            if (!switched)
            {
                throw new InvalidOperationException("Unable to switch build target to WebGL.");
            }
        }

        private static void BuildVariant(
            string[] scenes,
            string buildDirectory,
            WebGLCompressionFormat compressionFormat,
            string description)
        {
            PlayerSettings.WebGL.compressionFormat = compressionFormat;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                locationPathName = buildDirectory
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Devvit export failed during {description}: {report.summary.result}");
            }
        }

        private static void CopyArtifacts(
            string buildDirectory,
            string destinationDirectory,
            string baseName,
            params string[] suffixes)
        {
            string buildFolder = Path.Combine(buildDirectory, "Build");
            foreach (string suffix in suffixes)
            {
                string source = Directory.EnumerateFiles(buildFolder, $"*{suffix}", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(source))
                {
                    throw new FileNotFoundException($"Unable to find WebGL artifact matching '*{suffix}' in '{buildFolder}'.");
                }

                string destinationFileName = $"{baseName}{suffix}";
                string destinationPath = Path.Combine(destinationDirectory, destinationFileName);

                File.Copy(source, destinationPath, overwrite: true);
            }
        }

        private static void PrepareDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }

        private sealed class DevvitExportOptions
        {
            private DevvitExportOptions(string outputDirectory, string workingDirectory, string artifactBaseName)
            {
                OutputDirectory = outputDirectory;
                WorkingDirectory = workingDirectory;
                ArtifactBaseName = artifactBaseName;
            }

            public string OutputDirectory { get; }
            public string WorkingDirectory { get; }
            public string ArtifactBaseName { get; }

            public static DevvitExportOptions CreateWithDefaults()
            {
                return new DevvitExportOptions(
                    GetDefaultOutputDirectory(),
                    GetDefaultWorkingDirectory(),
                    PlayerSettings.productName);
            }

            public static DevvitExportOptions FromCommandLine()
            {
                string output = null;
                string artifactName = null;

                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case OutputArg when i + 1 < args.Length:
                            output = args[++i];
                            break;
                        case BaseNameArg when i + 1 < args.Length:
                            artifactName = args[++i];
                            break;
                    }
                }

                return new DevvitExportOptions(
                    string.IsNullOrEmpty(output) ? GetDefaultOutputDirectory() : Path.GetFullPath(output),
                    GetDefaultWorkingDirectory(),
                    string.IsNullOrEmpty(artifactName) ? PlayerSettings.productName : artifactName);
            }

            private static string GetDefaultOutputDirectory()
            {
                return Path.Combine(ProjectRoot, "DevvitExports");
            }

            private static string GetDefaultWorkingDirectory()
            {
                return Path.Combine(ProjectRoot, "Builds/DevvitAutomation");
            }
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
