// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.MsiInstallerTests.Framework;
using Microsoft.NET.Sdk.WorkloadManifestReader;

namespace Microsoft.DotNet.MsiInstallerTests
{
    public class WorkloadSetTests : VMTestBase
    {
        public WorkloadSetTests(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        public void DoesNotUseWorkloadSetsByDefault()
        {
            InstallSdk();

            VM.CreateRunCommand("dotnet", "workload", "update")
                .Execute()
                .Should()
                .Pass();

            var originalRollback = GetRollback();

            VM.CreateRunCommand("dotnet", "nuget", "add", "source", @"c:\SdkTesting\WorkloadSets")
                .WithDescription("Add WorkloadSets to NuGet.config")
                .Execute()
                .Should()
                .Pass();

            VM.CreateRunCommand("dotnet", "workload", "update")
                .Execute()
                .Should()
                .Pass();

            var newRollback = GetRollback();

            newRollback.ManifestVersions.Should().BeEquivalentTo(originalRollback.ManifestVersions);

        }

        void UpdateAndSwitchToWorkloadSetMode(out string updatedWorkloadVersion, out WorkloadSet rollbackAfterUpdate)
        {
            var originalWorkloadVersion = GetWorkloadVersion();
            originalWorkloadVersion.Should().StartWith("8.0.200-manifests.");

            VM.CreateRunCommand("dotnet", "workload", "update")
                .Execute()
                .Should()
                .Pass();

            rollbackAfterUpdate = GetRollback();
            updatedWorkloadVersion = GetWorkloadVersion();
            updatedWorkloadVersion.Should().StartWith("8.0.200-manifests.");
            updatedWorkloadVersion.Should().NotBe(originalWorkloadVersion);

            GetUpdateMode().Should().Be("manifests");

            VM.CreateRunCommand("dotnet", "workload", "config", "--update-mode", "workload-set")
                .WithDescription("Switch mode to workload-set")
                .Execute()
                .Should()
                .Pass();

            GetWorkloadVersion().Should().Be(updatedWorkloadVersion);

            GetUpdateMode().Should().Be("workload-set");
        }

        [Fact]
        public void UpdateWithWorkloadSets()
        {
            InstallSdk();

            UpdateAndSwitchToWorkloadSetMode(out string _, out WorkloadSet rollbackAfterUpdate);

            VM.CreateRunCommand("dotnet", "nuget", "add", "source", @"c:\SdkTesting\WorkloadSets")
                .WithDescription("Add WorkloadSets to NuGet.config")
                .Execute()
                .Should()
                .Pass();

            VM.CreateRunCommand("dotnet", "workload", "update")
                .Execute()
                .Should()
                .Pass();
            
            var newRollback = GetRollback();

            newRollback.ManifestVersions.Should().NotBeEquivalentTo(rollbackAfterUpdate.ManifestVersions);

            GetWorkloadVersion().Should().Be("8.0.201");
        }

        [Fact]
        public void UpdateInWorkloadSetModeWithNoAvailableWorkloadSet()
        {
            InstallSdk();

            UpdateAndSwitchToWorkloadSetMode(out string updatedWorkloadVersion, out WorkloadSet rollbackAfterUpdate);

            VM.CreateRunCommand("dotnet", "workload", "update")
                .Execute()
                .Should()
                .Pass();

            var newRollback = GetRollback();

            newRollback.ManifestVersions.Should().BeEquivalentTo(rollbackAfterUpdate.ManifestVersions);

            GetWorkloadVersion().Should().Be(updatedWorkloadVersion);
        }

        [Fact]
        public void WorkloadSetInstallationRecordIsWrittenCorrectly()
        {
            //  Should the workload set version or the package version be used in the registry?
            throw new NotImplementedException();
        }

        [Fact]
        public void TurnOffWorkloadSetUpdateMode()
        {
            //  If you have a workload set installed and then turn off workload set update mode, what should happen?
            //  - Update should update individual manifests
            //  - Resolver should ignore workload sets that are installed
            throw new NotImplementedException();
        }


        string GetWorkloadVersion()
        {
            var result = VM.CreateRunCommand("dotnet", "workload", "--version")
                .WithIsReadOnly(true)
                .Execute();

            result.Should().Pass();

            return result.StdOut;
        }

        string GetUpdateMode()
        {
            var result = VM.CreateRunCommand("dotnet", "workload", "config", "--update-mode")
                .WithIsReadOnly(true)
                .Execute();

            result.Should().Pass();

            return result.StdOut;
        }
    }
}
