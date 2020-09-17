// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using FluentAssertions;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.ProjectConstruction;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Generic;
using NuGet.Frameworks;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToGenerateSupportedTargetFrameworkAlias : SdkTest
    {
        public GivenThatWeWantToGenerateSupportedTargetFrameworkAlias(ITestOutputHelper log) : base(log)
        {}

        //  Current target framework            Property set            Expected list
        //  3.0/3.1/5.0/6.0                     n/a                     3.0/3.1/5.0/6.0
        //  5.0-windows/6.0-windows             n/a                     3.0/3.1/5.0-windows/6.0-windows
        //  3.0/3.1/5.0/6.0                     UseWindowsForms/WPF     3.0/3.1/5.0-windows/6.0-windows
        //  .NET Standard 2.0/2.1               n/a                     .NET Standard 1.0/1.1/2.0/2.1
        //  .NET Framework 4.8                  n/a                     lots of versions
        //  .NET Framework 4.8                  UseWindowsForms/WPF     lots of versions, no explicit Windows TargetPlatform

        [Theory]
        [InlineData("net5.0-windows")]
        [InlineData("net6.0-windows")]
        public void RetargetWindows(string currentTargetFramework)
        {
            TestTargetFrameworkAlias(currentTargetFramework, propertySetToTrue: null, new[]
                {
                    "netcoreapp1.0",
                    "netcoreapp1.1",
                    "netcoreapp2.0",
                    "netcoreapp2.1",
                    "netcoreapp3.0",
                    "netcoreapp3.1",
                    "net5.0-windows",
                    "net6.0-windows"
                });
        }

        private void TestTargetFrameworkAlias(string targetFramework, string propertySetToTrue, string[] expectedSupportedTargetFrameworkAliases)
        {
            TestProject testProject = new TestProject()
            {
                Name = "MockTargetFrameworkAliasItemGroup",
                IsSdkProject = true,
                IsExe = true,
                TargetFrameworks = targetFramework
            };

            if (!string.IsNullOrEmpty(propertySetToTrue))
            {
                testProject.AdditionalProperties[propertySetToTrue] = "True";
            }

            var testAsset = _testAssetsManager.CreateTestProject(testProject).WithProjectChanges(project =>
            {
                // Replace the default SupportedTargetFramework ItemGroup with our mock items
                var ns = project.Root.Name.Namespace;
                //var target = new XElement(ns + "Target",
                //    new XAttribute("Name", "OverwriteSupportedTargetFramework"),
                //    new XAttribute("BeforeTargets", "GenerateSupportedTargetFrameworkAlias"));

                //project.Root.Add(target);

                //var itemGroup = new XElement(ns + "ItemGroup");
                //target.Add(itemGroup);

                //var removeAll = new XElement(ns + "SupportedTargetFramework",
                //    new XAttribute("Remove", "@(SupportedTargetFramework)"));
                //itemGroup.Add(removeAll);

                //  Fake support for .NET 6
                if (NuGetFramework.Parse(targetFramework).Framework == ".NETCoreApp")
                {
                    var itemGroup = new XElement(ns + "ItemGroup");
                    project.Root.Add(itemGroup);

                    var mockTfm = new XElement(ns + "SupportedTargetFramework",
                                        new XAttribute("Include", ".NETCoreApp,Version=v6.0"));
                    itemGroup.Add(mockTfm);
                }
            });

            var getValuesCommand = new GetValuesCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name),
                targetFramework, "SupportedTargetFrameworkAlias", GetValuesCommand.ValueType.Item)
            {
                DependsOnTargets = "GenerateSupportedTargetFrameworkAlias"
            };
            getValuesCommand
                .Execute()
                .Should()
                .Pass();

            var values = getValuesCommand.GetValues();
            values.ShouldBeEquivalentTo(expectedSupportedTargetFrameworkAliases);
        }

        [Theory]
        [InlineData("", new string[] { ".NETCoreApp,Version=v3.1", ".NETCoreApp,Version=v5.0", ".NETStandard,Version=v2.1", ".NETFramework,Version=v4.7.2" }, new string[] { "netcoreapp3.1", "net5.0", "netstandard2.1", "net472" })] 
        [InlineData("Windows", new string[] { ".NETCoreApp,Version=v3.1", ".NETCoreApp,Version=v5.0" }, new string[] { "netcoreapp3.1", "net5.0-windows7.0" })]
        public void It_generates_supported_target_framework_alias_items(string targetPlatform, string[] mockSupportedTargetFramework, string[] expectedSupportedTargetFrameworkAlias)
        {
            var targetFramework = string.IsNullOrWhiteSpace(targetPlatform)? "net5.0" :  $"net5.0-{ targetPlatform }";
            TestProject testProject = new TestProject()
            {
                Name = "MockTargetFrameworkAliasItemGroup",
                IsSdkProject = true, 
                IsExe = true, 
                TargetFrameworks = targetFramework
            };

            var testAsset = _testAssetsManager.CreateTestProject(testProject).WithProjectChanges(project =>
            {
                // Replace the default SupportedTargetFramework ItemGroup with our mock items
                var ns = project.Root.Name.Namespace;
                var target = new XElement(ns + "Target",
                    new XAttribute("Name", "OverwriteSupportedTargetFramework"),
                    new XAttribute("BeforeTargets", "GenerateSupportedTargetFrameworkAlias"));

                project.Root.Add(target);

                var itemGroup = new XElement(ns + "ItemGroup");
                target.Add(itemGroup);

                var removeAll = new XElement(ns + "SupportedTargetFramework",
                    new XAttribute("Remove", "@(SupportedTargetFramework)"));
                itemGroup.Add(removeAll);

                foreach (var tfm in mockSupportedTargetFramework)
                {
                    var mockTfm = new XElement(ns + "SupportedTargetFramework",
                                        new XAttribute("Include", tfm));
                    itemGroup.Add(mockTfm);
                }
            });

            var getValuesCommand = new GetValuesCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name),
                targetFramework, "SupportedTargetFrameworkAlias", GetValuesCommand.ValueType.Item)
            {
                DependsOnTargets = "GenerateSupportedTargetFrameworkAlias"
            };
            getValuesCommand
                .Execute()
                .Should()
                .Pass();

            var values = getValuesCommand.GetValues();
            values.ShouldBeEquivalentTo(expectedSupportedTargetFrameworkAlias);
        }

        [WindowsOnlyTheory]
        [InlineData("netcoreapp3.1", "UseWpf")]
        [InlineData("netcoreapp3.1", "UseWindowsForms")]
        [InlineData("net5.0-windows", "UseWpf")]
        [InlineData("net5.0-windows", "UseWindowsForms")]
        public void It_generates_supported_target_framework_alias_items_with_target_platform(string targetFramework, string propertyName)
        {
            TestProject testProject = new TestProject()
            {
                Name = "TargetFrameworkAliasItemGroup",
                IsSdkProject = true,
                IsExe = true,
                TargetFrameworks = targetFramework
            };
            testProject.AdditionalProperties[propertyName] = "true";
            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var getValuesCommand = new GetValuesCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name),
                targetFramework, "SupportedTargetFrameworkAlias", GetValuesCommand.ValueType.Item)
            {
                DependsOnTargets = "GenerateSupportedTargetFrameworkAlias",
                MetadataNames = { "DisplayName" }
            };
            getValuesCommand
                .Execute()
                .Should()
                .Pass();

            var values = getValuesCommand.GetValuesWithMetadata();
            var net5Value = values.Where(value => value.value.Equals("net5.0-windows7.0"));
            net5Value.Should().NotBeNullOrEmpty();
            net5Value.FirstOrDefault().metadata.GetValueOrDefault("DisplayName").Should().Be(".NET 5.0");

            var net31Value = values.Where(value => value.value.Equals("netcoreapp3.1"));
            net31Value.Should().NotBeNullOrEmpty();
            net31Value.FirstOrDefault().metadata.GetValueOrDefault("DisplayName").Should().Be(".NET Core 3.1");
        }
    }
}
