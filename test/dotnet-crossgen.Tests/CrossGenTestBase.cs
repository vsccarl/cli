// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.Tools.CrossGen.Outputs;
using Microsoft.DotNet.Tools.Test.Utilities;
using Microsoft.Extensions.DependencyModel;
using NuGet.Versioning;

namespace Microsoft.DotNet.Tools.CrossGen.Tests
{
    public abstract class CrossGenTestBase : TestBase
    {
        private readonly string ExecutableExtension;
        /// <summary>
        /// NOTE: It's obvious but this test require write directory to the package cache directory
        /// Either check that $HOME/.nuget/packages is writable or set NUGET_PACKAGES to a writable location
        /// </summary>
        private readonly string NuGetPackagesRoot;

        public CrossGenTestBase()
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ExecutableExtension = isWindows ? ".exe" : "";

            NuGetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (NuGetPackagesRoot == null)
            {
                var homeDir = Environment.GetEnvironmentVariable(isWindows ? "UserProfile" : "HOME");
                NuGetPackagesRoot = Path.Combine(Path.Combine(homeDir, ".nuget", "packages"));
            }
        }

        protected void PerformTest(string appName, bool generatePdb, CrossGenOutputStructure outputStructure, ICollection<string> expectedChanges)
        {
            string outputsRoot, publishDir;
            PublishAsset(appName, out outputsRoot, out publishDir);

            var crossGenDir = Path.Combine(outputsRoot, "crossgen");
            DependencyContext depsFileContext, runtimeContext;
            GetDependencyContext(appName, publishDir, out depsFileContext, out runtimeContext);
            var crossGenCommand = new CrossGenCmd
            {
                AppName = appName,
                AppRoot = publishDir,
                OutputDir = crossGenDir,
                OutputStructure = outputStructure,    // CACHE option is not supported
                CrossGenExe = EnsureCrossGenExe(runtimeContext, appName),
                GeneratePdb = false,        // Not supported until coreclr jit 1.1 bit is picked up
                DiasymReaderLocation = null,
                OverwritingExistingHash = false
            };

            crossGenCommand
                .ExecuteWithCapturedOutput()
                .Should()
                .Pass();

            var outputDll = Path.Combine(crossGenDir, $"{appName}.dll");

            // First assumption, the app is runnable
            new TestCommand("dotnet")
                .ExecuteWithCapturedOutput(outputDll)
                .Should()
                .Pass()
                .And.HaveStdOutContaining("Default");

            // second, check if all assemblies are crossgen'd
            foreach (var lib in depsFileContext.RuntimeLibraries)
            {
                string assemblyRoot;
                switch (outputStructure)
                {
                    case CrossGenOutputStructure.APP:
                        assemblyRoot = crossGenDir;
                        break;
                    case CrossGenOutputStructure.CACHE:
                        assemblyRoot = Path.Combine(crossGenDir, lib.Name, lib.Version);
                        break;
                }










            }
        }

        private void PublishAsset(string appName, out string outputsRoot, out string publishDir)
        {
            var testInstance = TestAssetsManager.CreateTestInstance(appName);

            var testProjectDirectory = testInstance.TestRoot;

            new Restore3Command()
                .WithWorkingDirectory(testProjectDirectory)
                .Execute()
                .Should()
                .Pass();

            new Publish3Command()
                .WithWorkingDirectory(testProjectDirectory)
                .Execute("--framework netcoreapp1.0")
                .Should()
                .Pass();

            var configuration = Environment.GetEnvironmentVariable("CONFIGURATION") ?? "Debug";
            outputsRoot = Path.Combine(testProjectDirectory, "bin", configuration, "netcoreapp1.0");
            publishDir = Path.Combine(outputsRoot, "publish");

            var outputDll = Path.Combine(publishDir, $"{appName}.dll");

            new TestCommand("dotnet")
                .WithWorkingDirectory(publishDir)
                .ExecuteWithCapturedOutput(outputDll)
                .Should()
                .Pass();
        }

        private void GetDependencyContext(string appName, string publishDir, out DependencyContext depsFileContext, out DependencyContext runtimeContext)
        {
            var runtimeConfigFile = Path.Combine(publishDir, $"{appName}.runtimeconfig.json");

            RuntimeConfig runtimeConfig = null;
            if (File.Exists(runtimeConfigFile))
            {
                runtimeConfig = new RuntimeConfig(runtimeConfigFile);
            }

            var isPortable = runtimeConfig != null && runtimeConfig.IsPortable;

            using (var reader = new DependencyContextJsonReader())
            {
                var appDepsFile = Path.Combine(publishDir, $"{appName}.deps.json");
                using (var fstream = new FileStream(appDepsFile, FileMode.Open))
                {
                    depsFileContext = reader.Read(fstream);
                }

                if (isPortable)
                {
                    var sharedFrameworkDir = LocateSharedFramework(runtimeConfig.Framework);
                    var sharedFrameworkDepsFile = Path.Combine(sharedFrameworkDir, $"{runtimeConfig.Framework.Name}.deps.json");
                    using (var fstream = new FileStream(sharedFrameworkDepsFile, FileMode.Open))
                    {
                        var sharedFrameworkContext = reader.Read(fstream);
                        runtimeContext = depsFileContext.Merge(sharedFrameworkContext);
                    }
                }
                else
                {
                    runtimeContext = null;
                }
            }
        }

        private string EnsureCrossGenExe(DependencyContext context, string appName)
        {
            var rid = context.Target.Runtime;
            var coreclrPkg = FindCoreClrPkg(context, rid);
            if (coreclrPkg == null)
            {
                var runtimeFallback = context.RuntimeGraph.Single(g => g.Runtime == rid);
                foreach (var fallback in runtimeFallback.Fallbacks)
                {
                    coreclrPkg = FindCoreClrPkg(context, fallback);
                    if (coreclrPkg != null)
                    {
                        break;
                    }
                }
            }

            var crossGenExe = LocateCrossGenExe(NuGetPackagesRoot, coreclrPkg.Name, coreclrPkg.Version);
            if (crossGenExe == null)
            {
                // Now, we assume the standalone app depends on a coreclr matches whatever app we are testing
                var testInstance = TestAssetsManager.CreateTestInstance("AppWithOSSpecificDependency.Standalone");
                var testProjectDirectory = testInstance.TestRoot;
                new Restore3Command()
                    .WithWorkingDirectory(testProjectDirectory)
                    .Execute($" -- packages {NuGetPackagesRoot}")
                    .Should()
                    .Pass();

                crossGenExe = LocateCrossGenExe(NuGetPackagesRoot, coreclrPkg.Name, coreclrPkg.Version);

                if (crossGenExe == null)
                {
                    throw new Exception($"Unable to install crossgen.exe");
                }
            }

            return crossGenExe;
        }

        private string LocateCrossGenExe(string root, string coreclrPkgName, string version)
        {
            var candidate = Path.Combine(root, coreclrPkgName, version, "tools", $"crossgen{ExecutableExtension}");
            return File.Exists(candidate) ? candidate : null;
        }

        private string LocateSharedFramework(RuntimeConfigFramework framework)
        {
            var dotnetHome = Path.GetDirectoryName(new Muxer().MuxerPath);
            var shareFrameworksDir = Path.Combine(dotnetHome, "shared", framework.Name);

            if (!Directory.Exists(shareFrameworksDir))
            {
                throw new Exception($"Shared framework {shareFrameworksDir} does not exist");
            }

            var version = framework.Version;
            var exactMatch = Path.Combine(shareFrameworksDir, version);
            if (Directory.Exists(exactMatch))
            {
                return exactMatch;
            }
            else
            {
                Reporter.Verbose.WriteLine($"Cannot find shared framework in: {exactMatch}, trying to auto roll forward.");
                return AutoRollForward(shareFrameworksDir, version);
            }
        }

        private string AutoRollForward(string root, string version)
        {
            var targetVersion = NuGetVersion.Parse(version);
            var candidateNames = Directory.GetDirectories(root).Select(d => Path.GetFileName(d));

            string bestMatch = null;
            NuGetVersion bestMatchVersion = null;
            foreach (var candidateName in candidateNames)
            {
                var currentVersion = NuGetVersion.Parse(candidateName);
                if (currentVersion.Major == targetVersion.Major &&
                    currentVersion.Minor == targetVersion.Minor &&
                    currentVersion.CompareTo(targetVersion) > 0)
                {
                    if (bestMatchVersion == null || currentVersion.CompareTo(bestMatchVersion) < 0)
                    {
                        bestMatchVersion = currentVersion;
                        bestMatch = candidateName;
                    }
                }
            }

            if (bestMatch == null)
            {
                throw new Exception($"Unable to find shared framework candidate in {root}. Base version: {version}, available [{string.Join(", ", candidateNames)}]");
            }

            Reporter.Output.WriteLine($"Shared framework {bestMatch} would be used to crossgen.");

            return Path.Combine(root, bestMatch);
        }

        private RuntimeLibrary FindCoreClrPkg(DependencyContext context, string rid)
        {
            return context.RuntimeLibraries.SingleOrDefault(l => l.Name == $"runtime.{rid}.Microsoft.NETCore.Runtime.CoreCLR");
        }
    }
}
