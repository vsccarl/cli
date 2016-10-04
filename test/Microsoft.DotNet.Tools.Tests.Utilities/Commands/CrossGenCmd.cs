// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.CrossGen.Outputs;

namespace Microsoft.DotNet.Tools.Test.Utilities
{
    public class CrossGenCmd : TestCommand
    {
        public string AppName { get; set; }
        public string AppRoot { get; set; }
        public string OutputDir { get; set; }
        public CrossGenOutputStructure OutputStructure { get; set; }
        public string CrossGenExe { get; set; }
        public bool GeneratePdb { get; set; }
        public string DiasymReaderLocation { get; set; }
        public bool OverwritingExistingHash { get; set; }

        public CrossGenCmd()
            : base("dotnet")
        {
        }

        public override CommandResult Execute(string args = "")
        {
            args = $"crossgen {args} {BuildArgs()}";
            return base.Execute(args);
        }

        public override Task<CommandResult> ExecuteAsync(string args)
        {
            throw new NotImplementedException();
        }

        public override CommandResult ExecuteWithCapturedOutput(string args = "")
        {
            args = $"crossgen {args} {BuildArgs()}";
            return base.ExecuteWithCapturedOutput(args);
        }

        private string BuildArgs()
        {
            var sb = new StringBuilder();

            if (AppName != null)
            {
                sb.Append("--appName ").Append(AppName);
            }

            if (AppRoot != null)
            {
                sb.Append(" --appRoot ").Append(AppRoot);
            }

            if (OutputDir != null)
            {
                sb.Append(" --output-directory ").Append(OutputDir);
            }

            if (OutputStructure != CrossGenOutputStructure.APP)
            {
                sb.Append(" --output-structure ").Append(OutputStructure);
            }

            if (CrossGenExe != null)
            {
                sb.Append(" --crossgen-executable ").Append(CrossGenExe);
            }

            if (GeneratePdb)
            {
                sb.Append(" --generate-symbols ");
            }

            if (DiasymReaderLocation != null)
            {
                sb.Append(" --diasymreader ").Append(DiasymReaderLocation);
            }

            if (OverwritingExistingHash)
            {
                sb.Append(" --overwrite-on-conflict");
            }

            return sb.ToString();
        }
    }
}
