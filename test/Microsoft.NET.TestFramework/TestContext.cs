using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.NET.TestFramework.Commands;

namespace Microsoft.NET.TestFramework
{
    public class TestContext
    {
        //  Generally the folder the test DLL is in
        public string TestExecutionDirectory { get; set; }

        public string TestAssetsDirectory { get; set; }

        public string NuGetCachePath { get; set; }

        public string NuGetFallbackFolder { get; set; }

        public ToolsetInfo ToolsetUnderTest
        {
            get
            {
                return RepoInfo.ToolsetUnderTest;
            }
        }

        public static TestContext Current
        {
            get
            {
                return RepoInfo.TestExecutionInfo;
            }
        }

        public void AddTestEnvironmentVariables(SdkCommandSpec command)
        {
            //  Set NUGET_PACKAGES environment variable to match value from build.ps1
            command.Environment["NUGET_PACKAGES"] = NuGetCachePath;

            ToolsetUnderTest.AddTestEnvironmentVariables(command);
        }


    }
}
