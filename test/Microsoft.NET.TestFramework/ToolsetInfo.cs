﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.NET.TestFramework.Commands;

namespace Microsoft.NET.TestFramework
{
    public class ToolsetInfo
    {
        public string CliVersion { get; set; }

        public string DotNetHostPath { get; set; }

        private string CliSdkPath
        {
            get
            {
                string dotnetRoot = Path.GetDirectoryName(DotNetHostPath);
                return Path.Combine(dotnetRoot, "sdk", CliVersion);
            }
        }

        public string SdksPath { get; set; }

        public string BuildExtensionsSdkPath { get; set; }

        public string BuildExtensionsMSBuildPath { get; set; }

        public void AddTestEnvironmentVariables(SdkCommandSpec command)
        {
            ////  Set NUGET_PACKAGES environment variable to match value from build.ps1
            //command.Environment["NUGET_PACKAGES"] = RepoInfo.NuGetCachePath;

            if (SdksPath != null)
            {
                command.Environment["MSBuildSDKsPath"] = SdksPath;
                command.Environment["DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"] = SdksPath;
            }

            command.Environment["NETCoreSdkBundledVersionsProps"] = Path.Combine(CliSdkPath, "Microsoft.NETCoreSdk.BundledVersions.props");

            // The following line can be removed once this file is integrated into MSBuild
            command.Environment["CustomAfterMicrosoftCommonTargets"] = Path.Combine(BuildExtensionsSdkPath,
                "msbuildExtensions-ver", "Microsoft.Common.targets", "ImportAfter", "Microsoft.NET.Build.Extensions.targets");

            command.Environment["MicrosoftNETBuildExtensionsTargets"] = Path.Combine(BuildExtensionsMSBuildPath, "Microsoft.NET.Build.Extensions.targets");
        }
    }
}
