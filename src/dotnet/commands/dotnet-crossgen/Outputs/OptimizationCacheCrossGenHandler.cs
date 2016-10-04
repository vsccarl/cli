// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.DotNet.Tools.CrossGen.Outputs
{
    /// <summary>
    /// We do not have enough information to publish the optimization cache as a published app.
    /// </summary>
    public class OptimizationCacheCrossGenHandler : CrossGenHandler
    {
        // looks like the hash value "{Algorithm}-{value}" should be handled as an opaque string
        private const string Sha512PropertyName = "sha512";
        private readonly string _archName;
        private readonly bool _overwriteOnConflict;
        private IDictionary<string, string> _libShaValues;

        public OptimizationCacheCrossGenHandler(
            string crossGenExe,
            string diaSymReaderDll,
            CrossGenTarget crossGenTarget,
            DependencyContext depsFileContext,
            DependencyContext runtimeContext,
            string appDir,
            string outputDir,
            bool generatePDB,
            bool overwriteOnConflict)
            : base(crossGenExe, diaSymReaderDll, crossGenTarget, depsFileContext, runtimeContext, appDir, outputDir, generatePDB)
        {
            _archName = crossGenTarget.RuntimeIdentifier.Split(new char[]{'-'}).Last();
            _overwriteOnConflict = overwriteOnConflict;
            _libShaValues = new Dictionary<string, string>();
        }

        protected override string GetOutputDirFor(string sourcePathUsed, RuntimeLibrary lib, string assetPath)
        {
            var libRoot = GetOutputRootForLib(lib);
            var targetLocation = Path.Combine(libRoot, assetPath);
            return Path.GetDirectoryName(targetLocation);
        }

        protected override bool ShouldCrossGenLib(RuntimeLibrary lib)
        {
            // calculate/verify sha value ahead of time so the process can bail out completely
            // without affect the cache if there's an error
            var sha = GetShaValueToWrite(lib);
            if (sha != null)
            {
                _libShaValues.Add(lib.Name, sha);
            }

            return lib.Serviceable;
        }

        protected override void OnCrossGenCompletedFor(RuntimeLibrary lib)
        {
            string sha;
            _libShaValues.TryGetValue(lib.Name, out sha);
            if (sha != null)
            {
                var shaLocation = GetShaLocation(lib);
                File.WriteAllText(shaLocation, sha);
            }

            var libRoot = GetOutputRootForLib(lib);
            if (lib.Assemblies != null)
            {
                foreach (var assembly in lib.Assemblies)
                {
                    TryCopyOver(libRoot, assembly.Path);
                }
            }

            foreach (var group in lib.NativeLibraryGroups)
            {
                foreach (var path in group.AssetPaths)
                {
                    TryCopyOver(libRoot, path);
                }
            }

            if (lib.ResourceAssemblies != null)
            {
                foreach (var assembly in lib.ResourceAssemblies)
                {
                    TryCopyOver(libRoot, assembly.Path);
                }
            }

            foreach (var group in lib.RuntimeAssemblyGroups)
            {
                foreach (var path in group.AssetPaths)
                {
                    TryCopyOver(libRoot, path);
                }
            }
        }

        private void TryCopyOver(string libRoot, string relativePath)
        {
            var sourcePath = Path.Combine(AppDir, relativePath);
            if (File.Exists(sourcePath))
            {
                var targetPath = Path.Combine(libRoot, relativePath);
                if (!File.Exists(targetPath))
                {
                    var targetDir = Path.GetDirectoryName(targetPath);
                    Directory.CreateDirectory(targetDir);
                    File.Copy(sourcePath, targetPath);
                }
            }
            else
            {
                Reporter.Output.WriteLine($"Cannot locate resouce {relativePath} from source directory {AppDir}. It will not be copied");
            }
        }

        private string GetShaLocation(RuntimeLibrary lib)
        {
            var libRoot = GetOutputRootForLib(lib);
            return Path.Combine(libRoot, $"{lib.Name}.{lib.Version}.nupkg.sha512");
        }

        private string GetShaValueToWrite(RuntimeLibrary lib)
        {
            var libHashString = lib.Hash;
            if (!libHashString.StartsWith($"{Sha512PropertyName}-"))
            {
                throw new CrossGenException($"Unsupported Hash value for package {lib.Name}.{lib.Version}, value: {libHashString}");
            }
            var newShaValue = libHashString.Substring(Sha512PropertyName.Length + 1);

            var targetLibShaFile = GetShaLocation(lib);
            
            if (!File.Exists(targetLibShaFile) || ShouldOverwrite(lib, targetLibShaFile, newShaValue))
            {
                // We don't have to write until we need to
                return newShaValue;
            }

            return null;
        }

        private bool ShouldOverwrite(RuntimeLibrary lib, string targetLibShaFile, string newShaValue)
        {
            var oldShaValue = File.ReadAllText(targetLibShaFile);
            if (oldShaValue == newShaValue)
            {
                return false;
            }
            else if (_overwriteOnConflict)
            {
                Reporter.Output.WriteLine($"[INFO] Hash mismatch found for {lib.Name}.{lib.Version}. Overwriting existing hash file. This might causes cache misses for other applications.");
                return true;
            }
            else
            {
                throw new CrossGenException($"Hash mismatch found for {lib.Name}.{lib.Version}.");
            }
        }

        private string GetOutputRootForLib(RuntimeLibrary lib)
        {
            return Path.Combine(OutputRoot, _archName, lib.Name, lib.Version);
        }
    }
}