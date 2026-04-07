using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DurakGame.EditorTools
{
    [InitializeOnLoad]
    public static class BuildVersionStampUpdater
    {
        private const string ResourceDirectory = "Assets/Scripts/Resources";
        private const string StampAssetPath = ResourceDirectory + "/BuildVersionStamp.txt";
        private const int HashLength = 8;
        private static bool _updateQueued;

        static BuildVersionStampUpdater()
        {
            EditorApplication.projectChanged += QueueUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            QueueUpdate();
        }

        [MenuItem("Tools/Durak/Refresh Build Version Stamp")]
        public static void RefreshStampMenu()
        {
            UpdateStampIfNeeded();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                QueueUpdate();
            }
        }

        private static void QueueUpdate()
        {
            if (_updateQueued)
            {
                return;
            }

            _updateQueued = true;
            EditorApplication.delayCall += DelayedUpdate;
        }

        private static void DelayedUpdate()
        {
            _updateQueued = false;
            UpdateStampIfNeeded();
        }

        private static void UpdateStampIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueUpdate();
                return;
            }

            var newStamp = BuildStamp();
            if (string.IsNullOrWhiteSpace(newStamp))
            {
                newStamp = "unknown";
            }

            var existing = File.Exists(StampAssetPath) ? File.ReadAllText(StampAssetPath).Trim() : string.Empty;
            if (string.Equals(existing, newStamp, StringComparison.Ordinal))
            {
                return;
            }

            if (!Directory.Exists(ResourceDirectory))
            {
                Directory.CreateDirectory(ResourceDirectory);
            }

            File.WriteAllText(StampAssetPath, newStamp + Environment.NewLine, Encoding.UTF8);
            AssetDatabase.ImportAsset(StampAssetPath, ImportAssetOptions.ForceUpdate);
        }

        private static string BuildStamp()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return "nogit-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            }

            var head = RunGit(projectRoot, "rev-parse --short=8 HEAD");
            if (string.IsNullOrWhiteSpace(head))
            {
                return "local-" + ComputeFallbackHash(projectRoot);
            }

            var dirtyHash = ComputeGitDirtyHash(projectRoot);
            if (string.IsNullOrEmpty(dirtyHash))
            {
                return head + "-clean";
            }

            return head + "-" + dirtyHash + "-dirty";
        }

        private static string ComputeGitDirtyHash(string projectRoot)
        {
            var status = RunGit(projectRoot, "status --porcelain --untracked-files=all");
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            var files = ParseStatusPaths(status);
            files.RemoveAll(IsStampManagedPath);
            if (files.Count == 0)
            {
                return string.Empty;
            }

            files.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var relativePath = files[i];
                    var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                    var fullPath = Path.Combine(projectRoot, normalizedPath);

                    AppendText(sha, relativePath);
                    if (File.Exists(fullPath))
                    {
                        using (var stream = File.OpenRead(fullPath))
                        {
                            HashStream(sha, stream);
                        }
                    }
                    else
                    {
                        AppendText(sha, "<deleted>");
                    }
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToShortHex(sha.Hash, HashLength);
            }
        }

        private static List<string> ParseStatusPaths(string statusOutput)
        {
            var result = new List<string>();
            var lines = statusOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length < 4)
                {
                    continue;
                }

                var pathPart = line.Substring(3).Trim();
                var renameMarker = pathPart.IndexOf("->", StringComparison.Ordinal);
                if (renameMarker >= 0)
                {
                    pathPart = pathPart.Substring(renameMarker + 2).Trim();
                }

                pathPart = pathPart.Trim('"');
                if (!string.IsNullOrWhiteSpace(pathPart))
                {
                    result.Add(pathPart);
                }
            }

            return result;
        }

        private static string ComputeFallbackHash(string projectRoot)
        {
            var assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                return "00000000";
            }

            var files = Directory.GetFiles(assetsRoot, "*.*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            using (var sha = SHA256.Create())
            {
                for (var i = 0; i < files.Length; i++)
                {
                    var extension = Path.GetExtension(files[i]);
                    if (!IsRelevantExtension(extension))
                    {
                        continue;
                    }

                    var relative = files[i].Replace(projectRoot + Path.DirectorySeparatorChar, string.Empty);
                    relative = relative.Replace(Path.DirectorySeparatorChar, '/');
                    if (IsStampManagedPath(relative))
                    {
                        continue;
                    }

                    AppendText(sha, relative);
                    using (var stream = File.OpenRead(files[i]))
                    {
                        HashStream(sha, stream);
                    }
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToShortHex(sha.Hash, HashLength);
            }
        }

        private static bool IsStampManagedPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var normalized = relativePath.Replace('\\', '/').Trim();
            if (normalized.Equals(StampAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.Equals(StampAssetPath + ".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelevantExtension(string extension)
        {
            switch (extension)
            {
                case ".cs":
                case ".asmdef":
                case ".json":
                case ".unity":
                case ".prefab":
                case ".asset":
                case ".txt":
                    return true;
                default:
                    return false;
            }
        }

        private static string RunGit(string projectRoot, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "-C \"" + projectRoot + "\" " + arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return string.Empty;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    if (process.ExitCode != 0)
                    {
                        return string.Empty;
                    }

                    return output.Trim();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendText(HashAlgorithm algorithm, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            algorithm.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        private static void HashStream(HashAlgorithm algorithm, Stream stream)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                algorithm.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        private static string ToShortHex(byte[] bytes, int length)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "00000000";
            }

            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            var full = builder.ToString();
            if (full.Length <= length)
            {
                return full;
            }

            return full.Substring(0, length);
        }
    }

    public sealed class BuildVersionStampBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildVersionStampUpdater.RefreshStampMenu();
        }
    }
}
