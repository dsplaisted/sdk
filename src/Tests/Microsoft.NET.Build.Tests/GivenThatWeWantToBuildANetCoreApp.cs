// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.DependencyModel;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToBuildANetCoreApp : SdkTest
    {
        public GivenThatWeWantToBuildANetCoreApp(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        public void It_restores_only_ridless_tfm()
        {
            var testAsset = _testAssetsManager
                .CopyTestAsset("HelloWorld")
                .WithSource()
                .Restore(Log);

            var getValuesCommand = new GetValuesCommand(Log, testAsset.TestRoot,
                "netcoreapp1.1", "TargetDefinitions", GetValuesCommand.ValueType.Item)
            {
                DependsOnTargets = "RunResolvePackageDependencies",
            };

            getValuesCommand
                .Execute()
                .Should()
                .Pass();

            // When RuntimeIdentifier is not specified, the assets file
            // should only contain one target with no RIDs
            var targetDefs = getValuesCommand.GetValues();
            targetDefs.Count.Should().Be(1);
            targetDefs.Should().Contain(".NETCoreApp,Version=v1.1");
        }

        [Fact]
        public void It_runs_the_app_from_the_output_folder()
        {
            RunAppFromOutputFolder("RunFromOutputFolder", false, false);
        }

        [Fact]
        public void It_runs_a_rid_specific_app_from_the_output_folder()
        {
            RunAppFromOutputFolder("RunFromOutputFolderWithRID", true, false);
        }

        [Fact]
        public void It_runs_the_app_with_conflicts_from_the_output_folder()
        {
            RunAppFromOutputFolder("RunFromOutputFolderConflicts", false, true);
        }

        [Fact]
        public void It_runs_a_rid_specific_app_with_conflicts_from_the_output_folder()
        {
            RunAppFromOutputFolder("RunFromOutputFolderWithRIDConflicts", true, true);
        }

        private void RunAppFromOutputFolder(string testName, bool useRid, bool includeConflicts)
        {
            var targetFramework = "netcoreapp2.0";
            var runtimeIdentifier = useRid ? EnvironmentInfo.GetCompatibleRid(targetFramework) : null;

            TestProject project = new TestProject()
            {
                Name = testName,
                IsSdkProject = true,
                TargetFrameworks = targetFramework,
                RuntimeIdentifier = runtimeIdentifier,
                IsExe = true,
            };

            string outputMessage = $"Hello from {project.Name}!";

            project.SourceFiles["Program.cs"] = @"
using System;
public static class Program
{
    public static void Main()
    {
        TestConflictResolution();
        Console.WriteLine(""" + outputMessage + @""");
    }
" + ConflictResolutionAssets.ConflictResolutionTestMethod + @"
}
";
            var testAsset = _testAssetsManager.CreateTestProject(project, project.Name)
                .WithProjectChanges(p =>
                {
                    if (includeConflicts)
                    {
                        var ns = p.Root.Name.Namespace;

                        var itemGroup = new XElement(ns + "ItemGroup");
                        p.Root.Add(itemGroup);

                        foreach (var dependency in ConflictResolutionAssets.ConflictResolutionDependencies)
                        {
                            itemGroup.Add(new XElement(ns + "PackageReference",
                                new XAttribute("Include", dependency.Item1),
                                new XAttribute("Version", dependency.Item2)));
                        }
                    }
                })
                .Restore(Log, project.Name);

            string projectFolder = Path.Combine(testAsset.Path, project.Name);

            var buildCommand = new BuildCommand(Log, projectFolder);

            buildCommand
                .Execute()
                .Should()
                .Pass();

            string outputFolder = buildCommand.GetOutputDirectory(project.TargetFrameworks, runtimeIdentifier: runtimeIdentifier ?? "").FullName;

            Command.Create(TestContext.Current.ToolsetUnderTest.DotNetHostPath, new[] { Path.Combine(outputFolder, project.Name + ".dll") })
                .CaptureStdOut()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining(outputMessage);

        }

        [Fact]
        public void It_trims_conflicts_from_the_deps_file()
        {
            TestProject project = new TestProject()
            {
                Name = "NetCore2App",
                TargetFrameworks = "netcoreapp2.0",
                IsExe = true,
                IsSdkProject = true
            };

            project.SourceFiles["Program.cs"] = @"
using System;
public static class Program
{
    public static void Main()
    {
        TestConflictResolution();
        Console.WriteLine(""Hello, World!"");
    }
" + ConflictResolutionAssets.ConflictResolutionTestMethod + @"
}
";

            var testAsset = _testAssetsManager.CreateTestProject(project)
                .WithProjectChanges(p =>
                {
                    var ns = p.Root.Name.Namespace;

                    var itemGroup = new XElement(ns + "ItemGroup");
                    p.Root.Add(itemGroup);

                    foreach (var dependency in ConflictResolutionAssets.ConflictResolutionDependencies)
                    {
                        itemGroup.Add(new XElement(ns + "PackageReference",
                            new XAttribute("Include", dependency.Item1),
                            new XAttribute("Version", dependency.Item2)));
                    }

                })
                .Restore(Log, project.Name);

            string projectFolder = Path.Combine(testAsset.Path, project.Name);

            var buildCommand = new BuildCommand(Log, projectFolder);

            buildCommand
                .Execute()
                .Should()
                .Pass();

            string outputFolder = buildCommand.GetOutputDirectory(project.TargetFrameworks).FullName;

            using (var depsJsonFileStream = File.OpenRead(Path.Combine(outputFolder, $"{project.Name}.deps.json")))
            {
                var dependencyContext = new DependencyContextJsonReader().Read(depsJsonFileStream);
                dependencyContext.Should()
                    .OnlyHaveRuntimeAssemblies("", project.Name)
                    .And
                    .HaveNoDuplicateRuntimeAssemblies("")
                    .And
                    .HaveNoDuplicateNativeAssets(""); ;
            }
        }

        [Fact]
        public void There_are_no_conflicts_when_targeting_netcoreapp_1_1()
        {
            var testProject = new TestProject()
            {
                Name = "NetCoreApp1.1_Conflicts",
                TargetFrameworks = "netcoreapp1.1",
                IsSdkProject = true,
                IsExe = true
            };

            var testAsset = _testAssetsManager.CreateTestProject(testProject, testProject.Name)
                .Restore(Log, testProject.Name);

            var buildCommand = new BuildCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));

            buildCommand
                .Execute("/v:normal")
                .Should()
                .Pass()
                .And
                .NotHaveStdOutMatching("Encountered conflict", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void It_publishes_package_satellites_correctly(bool crossTarget)
        {
            var testProject = new TestProject()
            {
                Name = "AppUsingPackageWithSatellites",
                TargetFrameworks = "netcoreapp2.0",
                IsSdkProject = true,
                IsExe = true
            };

            if (crossTarget)
            {
                testProject.Name += "_cross";
            }

            testProject.PackageReferences.Add(new TestPackageReference("Humanizer.Core.fr", "2.2.0"));
            testProject.PackageReferences.Add(new TestPackageReference("Humanizer.Core.pt", "2.2.0"));

            var testAsset = _testAssetsManager.CreateTestProject(testProject, testProject.Name)
                .WithProjectChanges(project =>
                {
                    if (crossTarget)
                    {
                        var ns = project.Root.Name.Namespace;
                        var propertyGroup = project.Root.Elements(ns + "PropertyGroup").First();
                        propertyGroup.Element(ns + "TargetFramework").Name += "s";
                    }
                })
                .Restore(Log, testProject.Name);

            var publishCommand = new PublishCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));
            publishCommand
                .Execute("/v:normal", $"/p:TargetFramework={testProject.TargetFrameworks}")
                .Should()
                .Pass()
                .And
                .NotHaveStdOutMatching("Encountered conflict", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ;

            var outputDirectory = publishCommand.GetOutputDirectory(testProject.TargetFrameworks);
            outputDirectory.Should().NotHaveFile("Humanizer.resources.dll");
            outputDirectory.Should().HaveFile(Path.Combine("fr", "Humanizer.resources.dll"));
        }

        [Fact]
        public void It_uses_lowercase_form_of_the_target_framework_for_the_output_path()
        {
            var testProject = new TestProject()
            {
                Name = "OutputPathCasing",
                TargetFrameworks = "igored",
                IsSdkProject = true,
                IsExe = true
            };

            string[] extraArgs = new[] { "/p:TargetFramework=NETCOREAPP1.1" };

            var testAsset = _testAssetsManager.CreateTestProject(testProject, testProject.Name)
                .Restore(Log, testProject.Name, extraArgs);

            var buildCommand = new BuildCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));

            buildCommand
                .Execute(extraArgs)
                .Should()
                .Pass();

            string outputFolderWithConfiguration = Path.Combine(buildCommand.ProjectRootPath, "bin", "Debug");

            Directory.GetDirectories(outputFolderWithConfiguration)
                .Select(Path.GetFileName)
                .Should()
                .BeEquivalentTo("netcoreapp1.1");

            string intermediateFolderWithConfiguration = Path.Combine(buildCommand.GetBaseIntermediateDirectory().FullName, "Debug");

            Directory.GetDirectories(intermediateFolderWithConfiguration)
                .Select(Path.GetFileName)
                .Should()
                .BeEquivalentTo("netcoreapp1.1");
        }
    }
}
