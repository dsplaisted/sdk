using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Perf.Tests
{
    public class RestorePerf : SdkTest
    {
        public RestorePerf(ITestOutputHelper log) : base(log)
        {
        }

        [Theory]
        [InlineData("console")]
        [InlineData("mvc")]
        public void New(string projectType)
        {
            var testDir = _testAssetsManager.CreateTestDirectory(identifier: projectType);
            var command = GetNewCommand(testDir, projectType);
            var perfTest = new PerfTest();
            perfTest.ScenarioName = projectType;
            perfTest.TestName = "new --no-restore";
            perfTest.ProcessToMeasure = command.GetProcessStartInfo();
            perfTest.TestFolder = testDir.Path;
            perfTest.GetBinLog = false;
            perfTest.GetPerformanceSummary = false;
            perfTest.Run();
        }

        [Theory]
        [InlineData("console")]
        [InlineData("mvc")]
        public void NewWithRestore(string projectType)
        {
            var testDir = _testAssetsManager.CreateTestDirectory(identifier: projectType);
            var command = GetNewCommand(testDir, projectType, restore: true);
            var perfTest = new PerfTest();
            perfTest.ScenarioName = projectType;
            perfTest.TestName = "new";
            perfTest.ProcessToMeasure = command.GetProcessStartInfo();
            perfTest.TestFolder = testDir.Path;
            perfTest.GetBinLog = false;
            perfTest.GetPerformanceSummary = false;
            perfTest.Run();
        }

        [Theory]
        [InlineData("console")]
        [InlineData("mvc")]
        public void Restore(string projectType)
        {
            var testDir = _testAssetsManager.CreateTestDirectory(identifier: projectType);
            var newCommand = GetNewCommand(testDir, projectType);
            newCommand.Execute().Should().Pass();

            var command = GetRestoreCommand(testDir);
            var perfTest = new PerfTest();
            perfTest.ScenarioName = projectType;
            perfTest.TestName = "restore";
            perfTest.ProcessToMeasure = command.GetProcessStartInfo();
            perfTest.TestFolder = testDir.Path;
            perfTest.Run();
        }

        [Theory]
        [InlineData("console")]
        [InlineData("mvc")]
        public void NoopRestore(string projectType)
        {
            var testDir = _testAssetsManager.CreateTestDirectory(identifier: projectType);
            var newCommand = GetNewCommand(testDir, projectType);
            newCommand.Execute().Should().Pass();

            var command = GetRestoreCommand(testDir);
            command.Execute().Should().Pass();

            var perfTest = new PerfTest();
            perfTest.ScenarioName = projectType;
            perfTest.TestName = "restore (no-op)";
            perfTest.ProcessToMeasure = command.GetProcessStartInfo();
            perfTest.TestFolder = testDir.Path;
            perfTest.Run();
        }

        [Theory]
        [InlineData("console")]
        [InlineData("mvc")]

        public void Build(string projectType)
        {
            var testDir = _testAssetsManager.CreateTestDirectory(identifier: projectType);
            var newCommand = GetNewCommand(testDir, projectType);
            newCommand.Execute().Should().Pass();

            var restoreCommand = GetRestoreCommand(testDir);
            restoreCommand.Execute().Should().Pass();

            var command = GetBuildCommand(testDir);
            var perfTest = new PerfTest();
            perfTest.ScenarioName = projectType;
            perfTest.TestName = "build --no-restore";
            perfTest.ProcessToMeasure = command.GetProcessStartInfo();
            perfTest.TestFolder = testDir.Path;
            perfTest.Run();
        }

        private TestCommand GetNewCommand(TestDirectory testDirectory, string projectType, bool restore=false)
        {
            var newCommand = new DotnetCommand(Log);
            newCommand.Arguments = new List<string>()
            {
                "new",
                projectType
            };
            if  (!restore)
            {
                newCommand.Arguments.Add("--no-restore");
            }
            newCommand.WorkingDirectory = testDirectory.Path;
            return newCommand;
        }

        private TestCommand GetRestoreCommand(TestDirectory testDirectory)
        {
            //return new RestoreCommand(Log, testDirectory.Path);
            return new DotnetCommand(Log)
            {
                Arguments = new List<string>()
                {
                    "restore"
                },
                WorkingDirectory = testDirectory.Path
            };
        }

        private TestCommand GetBuildCommand(TestDirectory testDirectory)
        {
            //return new BuildCommand(Log, testDirectory.Path);
            return new DotnetCommand(Log)
            {
                Arguments = new List<string>()
                {
                    "build",
                    "--no-restore"
                },
                WorkingDirectory = testDirectory.Path
            };
        }
    }
}
