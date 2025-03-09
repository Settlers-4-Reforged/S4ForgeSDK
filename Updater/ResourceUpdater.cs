﻿using ForgeUpdater.Manifests;

using System.IO.Compression;


namespace ForgeUpdater.Updater {
    internal class ResourceUpdater<TManifest> where TManifest : Manifest {
        private static readonly Dictionary<string, Action<string>> InstallerActions = new Dictionary<string, Action<string>>() {
            { "move", MoveAction },
            { "copy", CopyAction },
            { "delete", DeleteAction },
            { "mkdir", MkdirAction }
        };

        public ResourceUpdater(TManifest target, string updateZip, string targetFolder) {
            TargetManifest = target;
            UpdateZip = updateZip;

            TargetFolder = targetFolder;
        }

        bool ShouldClearResidualFiles => TargetManifest.ClearResidualFiles;

        TManifest TargetManifest { get; set; }
        string UpdateZip { get; set; }
        private string TargetFolder { get; set; }

        public IEnumerable<float> Update() {
            using ZipArchive zip = ZipFile.OpenRead(UpdateZip);
            HandleActionScript(zip);


            if (Directory.Exists(TargetFolder)) {
                if (ShouldClearResidualFiles) {
                    ClearResidualFiles();
                }
            } else {
                Directory.CreateDirectory(TargetFolder);
            }


            int fileCount = zip.Entries.Count;
            int currentFile = 0;
            foreach (ZipArchiveEntry entry in zip.Entries) {
                // Report progress...
                yield return (float)currentFile++ / fileCount;

                string targetPath = Path.Combine(TargetFolder, entry.FullName);

                if (entry.FullName.EndsWith("/")) {
                    UpdaterLogger.LogDebug("Creating directory: {0}", targetPath);
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                if (File.Exists(targetPath) && IsFileIgnored(entry.FullName)) {
                    UpdaterLogger.LogDebug("Ignoring existing file: {0}", targetPath);
                } else {
                    UpdaterLogger.LogDebug("Extracting file: {0}", targetPath);
                    DeleteFile(targetPath);
                    entry.ExtractToFile(targetPath, true);
                }
            }

            yield return 1;
        }

        private void ClearResidualFiles() {
            foreach (string file in Directory.GetFiles(TargetFolder, "*", SearchOption.AllDirectories)) {
                string relativePath = file.Substring(TargetFolder.Length).TrimStart(Path.DirectorySeparatorChar);
                if (IsFileIgnored(relativePath)) {
                    UpdaterLogger.LogDebug("Ignoring residual file: {0}", file);
                    continue;
                }

                UpdaterLogger.LogDebug("Deleting residual file: {0}", file);
                DeleteFile(file);
            }
        }

        private static void DeleteFile(string file) {
            if (!File.Exists(file)) return;

            try {
                File.Delete(file);
            } catch (Exception e) {
                UpdaterLogger.LogWarn("Failed to delete file: {0}, trying to rename it and delete later. Error {1}", file, e);

                try {
                    // Open C# assemblies are locked by handle only, but the file can be still be renamed.
                    File.Move(file, file + ".updater_leftover");
                } catch (Exception e2) {
                    UpdaterLogger.LogError(e2, "Failed to delete and rename file: {0}", file);
                }
            }
        }

        public static void CleanupLeftoverFiles(string folder) {
            foreach (string file in Directory.GetFiles(folder, "*.updater_leftover", SearchOption.AllDirectories)) {
                UpdaterLogger.LogDebug("Deleting residual file: {0}", file);
                File.Delete(file);
            }
        }

        private void HandleActionScript(ZipArchive zip) {
            ZipArchiveEntry? actionScript = zip.Entries.FirstOrDefault((e) => e.Name == "forge_script.txt");
            if (actionScript == null) return;

            using StreamReader reader = new StreamReader(actionScript.Open());
            string[] actionLines = reader.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            ApplyInstallerActions(actionLines);
        }

        private void ApplyInstallerActions(string[] actionLines) {
            foreach (string actionLine in actionLines) {
                try {
                    string actionName = actionLine.Split(' ')[0];

                    if (InstallerActions.TryGetValue(actionName, out Action<string>? actionHandler)) {
                        actionHandler(actionLine);
                    } else {
                        UpdaterLogger.LogWarn("Unknown installer action: {0}", actionName);
                    }
                } catch (Exception e) {
                    UpdaterLogger.LogError(e, "Failed to parse installer action: {0}", actionLine);
                }
            }
        }

        private bool IsFileIgnored(string file) {
            if (file.Equals("manifest.json", StringComparison.InvariantCultureIgnoreCase))
                return true;

            if (TargetManifest.IgnoredEntries == null)
                return false;

            foreach (string ignore in TargetManifest.IgnoredEntries) {
                string sanitizedIgnore = ignore.Replace('\\', '/');
                bool ignoreIsDirectory = sanitizedIgnore.EndsWith("/");

                if (ignoreIsDirectory) {
                    string sanitizedFile = file.Replace("\\", "/");
                    if (sanitizedFile.StartsWith(sanitizedIgnore, StringComparison.InvariantCultureIgnoreCase))
                        return true;
                } else {
                    if (string.Equals(file, ignore, StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        #region Actions

        static void MoveAction(string line) {
            string[] parts = line.Split(' ');

            if (parts.Length != 3) {
                UpdaterLogger.LogError(null, "Invalid move action: {0}", line);
                return;
            }

            string source = parts[1];
            string target = parts[2];

            if (File.Exists(target)) {
                UpdaterLogger.LogDebug("Deleting existing file: {0}", target);
                DeleteFile(target);
            }

            UpdaterLogger.LogDebug("Moving {0} to {1}", source, target);
            File.Move(source, target);
        }

        static void CopyAction(string line) {
            string[] parts = line.Split(' ');

            if (parts.Length != 3) {
                UpdaterLogger.LogError(null, "Invalid copy action: {0}", line);
                return;
            }

            string source = parts[1];
            string target = parts[2];

            if (File.Exists(target)) {
                UpdaterLogger.LogDebug("Deleting existing file: {0}", target);
                DeleteFile(target);
            }

            UpdaterLogger.LogDebug("Copying {0} to {1}", source, target);
            File.Copy(source, target);
        }

        static void DeleteAction(string line) {
            string[] parts = line.Split(' ');

            if (parts.Length != 2) {
                UpdaterLogger.LogError(null, "Invalid delete action: {0}", line);
                return;
            }

            string target = parts[1];

            if (File.Exists(target)) {
                UpdaterLogger.LogDebug("Deleting existing file: {0}", target);
                DeleteFile(target);
            }
        }

        static void MkdirAction(string line) {
            string[] parts = line.Split(' ');

            if (parts.Length != 2) {
                UpdaterLogger.LogError(null, "Invalid mkdir action: {0}", line);
                return;
            }

            string target = parts[1];

            if (Directory.Exists(target)) {
                UpdaterLogger.LogDebug("Deleting existing directory: {0}", target);
                Directory.Delete(target, true);
            }

            UpdaterLogger.LogDebug("Creating directory: {0}", target);
            Directory.CreateDirectory(target);
        }

        #endregion
    }
}
