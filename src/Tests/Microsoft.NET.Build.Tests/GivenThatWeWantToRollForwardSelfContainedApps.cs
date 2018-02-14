using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToRollForwardSelfContainedApps : SdkTest
    {
        public GivenThatWeWantToRollForwardSelfContainedApps(ITestOutputHelper log) : base(log)
        {
        }

        [Theory]
        //  TargetFramework, RuntimeFrameworkVersion, ExpectedPackageVersion, ExpectedRuntimeFrameworkVersion
        [InlineData("netcoreapp1.0", null, "1.0.5", "1.0.5")]
        [InlineData("netcoreapp1.0", "1.0.0", "1.0.0", "1.0.0")]
        [InlineData("netcoreapp1.0", "1.0.3", "1.0.3", "1.0.3")]
        [InlineData("netcoreapp1.1", null, "1.1.2", "1.1.2")]
        [InlineData("netcoreapp1.1", "1.1.0", "1.1.0", "1.1.0")]
        [InlineData("netcoreapp1.1.1", null, "1.1.1", "1.1.1")]
        public void It_targets_the_right_shared_framework(string targetFramework, string runtimeFrameworkVersion,
            string expectedPackageVersion, string expectedRuntimeVersion)
        {
            string testIdentifier = "SharedFrameworkTargeting_" + string.Join("_", targetFramework, runtimeFrameworkVersion ?? "null");

            It_targets_the_right_framework(testIdentifier, targetFramework, runtimeFrameworkVersion,
                selfContained: false, isExe: true,
                expectedPackageVersion: expectedPackageVersion, expectedRuntimeVersion: expectedRuntimeVersion);
        }

        //  Test behavior when implicit version differs for framework-dependent and self-contained apps
        [Theory]
        [InlineData("netcoreapp1.0", false, true, "1.0.5")]
        [InlineData("netcoreapp1.0", true, true, TestContext.ImplicitRuntimeFrameworkVersionForSelfContainedNetCoreApp1_0)]
        [InlineData("netcoreapp1.0", false, false, "1.0.5")]
        [InlineData("netcoreapp1.1", false, true, "1.1.2")]
        [InlineData("netcoreapp1.1", true, true, TestContext.ImplicitRuntimeFrameworkVersionForSelfContainedNetCoreApp1_1)]
        [InlineData("netcoreapp1.1", false, false, "1.1.2")]
        [InlineData("netcoreapp2.0", false, true, "2.0.0")]
        [InlineData("netcoreapp2.0", true, true, TestContext.ImplicitRuntimeFrameworkVersionForSelfContainedNetCoreApp2_0)]
        [InlineData("netcoreapp2.0", false, false, "2.0.0")]
        public void It_targets_the_right_framework_depending_on_output_type(string targetFramework, bool selfContained, bool isExe, string expectedFrameworkVersion)
        {
            string testIdentifier = "Framework_targeting_" + targetFramework + "_" + (isExe ? "App_" : "Lib_") + (selfContained ? "SelfContained" : "FrameworkDependent");

            It_targets_the_right_framework(testIdentifier, targetFramework, null, selfContained, isExe, expectedFrameworkVersion, expectedFrameworkVersion);
        }

        private void It_targets_the_right_framework(
            string testIdentifier,
            string targetFramework,
            string runtimeFrameworkVersion,
            bool selfContained,
            bool isExe,
            string expectedPackageVersion,
            string expectedRuntimeVersion,
            string extraMSBuildArguments = null)
        {
            string runtimeIdentifier = null;
            if (selfContained)
            {
                runtimeIdentifier = EnvironmentInfo.GetCompatibleRid(targetFramework);
            }

            var testProject = new TestProject()
            {
                Name = "FrameworkTargetTest",
                TargetFrameworks = targetFramework,
                RuntimeFrameworkVersion = runtimeFrameworkVersion,
                IsSdkProject = true,
                IsExe = isExe,
                RuntimeIdentifier = runtimeIdentifier
            };

            var extraArgs = extraMSBuildArguments?.Split(' ') ?? Array.Empty<string>();

            var testAsset = _testAssetsManager.CreateTestProject(testProject, testIdentifier)
                .Restore(Log, testProject.Name, extraArgs);

            var buildCommand = new BuildCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));

            buildCommand
                .Execute(extraArgs)
                .Should()
                .Pass();

            var targetingInfo = GetProjectTargetingInfo(testProject, buildCommand);

            if (isExe)
            {
                
                if (selfContained)
                {
                    targetingInfo.DepsJsonNetCoreAppVersion.Should().Be(expectedPackageVersion);
                }
                else
                {
                    targetingInfo.RuntimeConfigRuntimeFrameworkVersion.Should().Be(expectedRuntimeVersion);
                }

                // can't use Path.Combine on segments with an illegal `|` character
                var expectedPath = $"{Path.Combine(GetUserProfile(), ".dotnet", "store")}{Path.DirectorySeparatorChar}|arch|{Path.DirectorySeparatorChar}|tfm|";
                targetingInfo.DevRuntimeConfigAdditionalProbingPaths.Should().Contain(expectedPath);
            }

            LockFile lockFile = LockFileUtilities.GetLockFile(Path.Combine(buildCommand.ProjectRootPath, "obj", "project.assets.json"), NullLogger.Instance);

            targetingInfo.AssetsFileNetCoreAppVersion.Should().Be(expectedPackageVersion);
        }

        class ProjectTargetingInfo
        {
            public string RuntimeConfigRuntimeFrameworkVersion { get; set; }
            public string DepsJsonNetCoreAppVersion { get; set; }
            public List<string> DevRuntimeConfigAdditionalProbingPaths { get; set; }
            public string AssetsFileNetCoreAppVersion { get; set; }
        }

        ProjectTargetingInfo GetProjectTargetingInfo(TestProject testProject, MSBuildCommand msbuildCommand)
        {
            ProjectTargetingInfo targetingInfo = new ProjectTargetingInfo();

            bool selfContained = testProject.RuntimeIdentifier != null;

            var outputDirectory = msbuildCommand.GetOutputDirectory(testProject.TargetFrameworks, runtimeIdentifier: testProject.RuntimeIdentifier);
            if (testProject.IsExe)
            {
                if (selfContained)
                {
                    //  Self-contained apps have Microsoft.NETCore.App listed in their deps.json
                    outputDirectory.Should().HaveFile(testProject.Name + ".deps.json");
                    string depsJsonFile = Path.Combine(outputDirectory.FullName, testProject.Name + ".deps.json");
                    string depsJsonContents = File.ReadAllText(depsJsonFile);
                    JObject depsJson = JObject.Parse(depsJsonContents);
                    var netCoreAppLibraries = depsJson["libraries"].Cast<JProperty>().Select(o => o.Name)
                        .Select(name =>
                        {
                            var parts = name.Split('/');
                            return (name: parts[0], version: parts[1]);
                        })
                        .Where(library => library.name == "Microsoft.NETCore.App");
                    netCoreAppLibraries.Should().HaveCount(1, "Microsoft.NETCore.App should be listed once in the libraries section of the deps.json file");

                    targetingInfo.DepsJsonNetCoreAppVersion = netCoreAppLibraries.Single().version;
                }
                else
                {
                    //  Shared framework apps write a framework version to the runtimeconfig
                    string runtimeConfigFile = Path.Combine(outputDirectory.FullName, testProject.Name + ".runtimeconfig.json");
                    string runtimeConfigContents = File.ReadAllText(runtimeConfigFile);
                    JObject runtimeConfig = JObject.Parse(runtimeConfigContents);

                    string actualRuntimeFrameworkVersion = ((JValue)runtimeConfig["runtimeOptions"]["framework"]["version"]).Value<string>();
                    targetingInfo.RuntimeConfigRuntimeFrameworkVersion = actualRuntimeFrameworkVersion;
                }

                var runtimeconfigDevFileName = testProject.Name + ".runtimeconfig.dev.json";
                outputDirectory.Should()
                        .HaveFile(runtimeconfigDevFileName);

                string devruntimeConfigContents = File.ReadAllText(Path.Combine(outputDirectory.FullName, runtimeconfigDevFileName));
                JObject devruntimeConfig = JObject.Parse(devruntimeConfigContents);

                var additionalProbingPaths = ((JArray)devruntimeConfig["runtimeOptions"]["additionalProbingPaths"]).Values<string>();
                targetingInfo.DevRuntimeConfigAdditionalProbingPaths = additionalProbingPaths.ToList();
            }

            LockFile lockFile = LockFileUtilities.GetLockFile(Path.Combine(msbuildCommand.ProjectRootPath, "obj", "project.assets.json"), NullLogger.Instance);

            var target = lockFile.GetTarget(NuGetFramework.Parse(testProject.TargetFrameworks), null);
            var netCoreAppLibrary = target.Libraries.Single(l => l.Name == "Microsoft.NETCore.App");
            targetingInfo.AssetsFileNetCoreAppVersion = netCoreAppLibrary.Version.ToString();

            return targetingInfo;
        }

        private static string GetUserProfile()
        {
            string userDir;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                userDir = "USERPROFILE";
            }
            else
            {
                userDir = "HOME";
            }

            return Environment.GetEnvironmentVariable(userDir);
        }
    }
}
