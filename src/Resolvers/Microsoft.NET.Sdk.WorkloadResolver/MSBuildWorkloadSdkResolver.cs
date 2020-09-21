﻿using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.NET.Sdk.WorkloadManifestReader;

#if NET
using Microsoft.DotNet.Cli;
#else
using Microsoft.DotNet.DotNetSdkResolver;
#endif

#nullable disable

namespace Microsoft.NET.Sdk.WorkloadResolver
{
    public class MSBuildWorkloadSdkResolver : SdkResolver
    {
        public override string Name => "Microsoft.DotNet.MSBuildWorkloadSdkResolver";

        public override int Priority => 4000;

        private IWorkloadManifestProvider _workloadManifestProvider;
        private IWorkloadResolver _workloadResolver;


#if NETFRAMEWORK
        private readonly NETCoreSdkResolver _sdkResolver;
#endif

        public MSBuildWorkloadSdkResolver()
        {
#if NETFRAMEWORK
            _sdkResolver = new NETCoreSdkResolver();
#endif
        }

        private void InitializeWorkloadResolver(SdkResolverContext context)
        {
            var dotnetRootPath = GetDotNetRoot(context);

            var sdkDirectory = GetSdkDirectory(context);
            //  TODO: Is the directory name OK, or should we read the .version file (we don't want to use the Cli.Utils library to read it on .NET Framework)
            var sdkVersion = Path.GetFileName(sdkDirectory);


            _workloadManifestProvider ??= new SdkDirectoryWorkloadManifestProvider(dotnetRootPath, sdkVersion);
            //  TODO: Fix namespace / type names so there's not a collision for WorkloadResolver
            _workloadResolver ??= new Microsoft.NET.Sdk.WorkloadManifestReader.WorkloadResolver(_workloadManifestProvider, dotnetRootPath);
        }

        public override SdkResult Resolve(SdkReference sdkReference, SdkResolverContext resolverContext, SdkResultFactory factory)
        {
            InitializeWorkloadResolver(resolverContext);

            if (sdkReference.Name.Equals("Microsoft.NET.SDK.WorkloadAutoImportPropsLocator", StringComparison.OrdinalIgnoreCase))
            {
                List<string> autoImportSdkPaths = new List<string>();
                foreach (var sdkPackInfo in _workloadResolver.GetInstalledWorkloadPacksOfKind(WorkloadPackKind.Sdk))
                {
                    string sdkPackSdkFolder = Path.Combine(sdkPackInfo.Path, "Sdk");
                    string autoImportPath = Path.Combine(sdkPackSdkFolder, "AutoImport.props");
                    if (File.Exists(autoImportPath))
                    {
                        autoImportSdkPaths.Add(sdkPackSdkFolder);
                    }
                }
                return factory.IndicateSuccess(autoImportSdkPaths, sdkReference.Version);
            }
            else if (sdkReference.Name == "TestSdk")
            {
                var propertiesToAdd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                propertiesToAdd["TestProperty1"] = "AOEU";
                propertiesToAdd["TestProperty2"] = "ASDF";

                Dictionary<string, SdkResultItem> itemsToAdd = new Dictionary<string, SdkResultItem>(StringComparer.OrdinalIgnoreCase);

                itemsToAdd["TestItem1"] = new SdkResultItem("TestItem1Value",
                    new Dictionary<string, string>()
                    { {"a", "b" } });

                itemsToAdd["TestItem2"] = new SdkResultItem("TestItem2Value",
                    new Dictionary<string, string>()
                    { {"c", "d" },
                      {"e", "f" }});

                return factory.IndicateSuccess(Enumerable.Empty<string>(),
                    sdkReference.Version,
                    propertiesToAdd,
                    itemsToAdd);
            }
            else
            {
                var sdkVersion = _workloadResolver.TryGetPackVersion(sdkReference.Name);
                if (sdkVersion != null)
                {
                    string workloadPackPath = GetWorkloadPackPath(resolverContext, sdkReference.Name, sdkVersion);
                    if (Directory.Exists(workloadPackPath))
                    {
                        return factory.IndicateSuccess(Path.Combine(workloadPackPath, "Sdk"), sdkReference.Version);
                    }
                    else
                    {
                        var itemsToAdd = new Dictionary<string, SdkResultItem>();
                        itemsToAdd.Add("MissingWorkloadPack",
                            new SdkResultItem(sdkReference.Name,
                                metadata: new Dictionary<string, string>()
                                {
                                    { "Version", sdkVersion }
                                }));

                        Dictionary<string, string> propertiesToAdd = new Dictionary<string, string>();
                        return factory.IndicateSuccess(Enumerable.Empty<string>(),
                            sdkReference.Version,
                            propertiesToAdd: propertiesToAdd,
                            itemsToAdd: itemsToAdd);
                    }
                }
            }
            return null;
        }

        private string GetSdkDirectory(SdkResolverContext context)
        {
#if NET
            var sdkDirectory = Path.GetDirectoryName(typeof(DotnetFiles).Assembly.Location);
            return sdkDirectory;

#else
            string dotnetExeDir = _sdkResolver.GetDotnetExeDirectory();
            string globalJsonStartDir = Path.GetDirectoryName(context.SolutionFilePath ?? context.ProjectFilePath);
            var sdkResolutionResult = _sdkResolver.ResolveNETCoreSdkDirectory(globalJsonStartDir, context.MSBuildVersion, context.IsRunningInVisualStudio, dotnetExeDir);

            return sdkResolutionResult.ResolvedSdkDirectory;
#endif

        }

        private string GetDotNetRoot(SdkResolverContext context)
        {
            var sdkDirectory = GetSdkDirectory(context);
            var dotnetRoot = Directory.GetParent(sdkDirectory).Parent.FullName;
            return dotnetRoot;
        }

        //  TODO: delete this method and use workload resolver for this functionality
        private string GetWorkloadPackPath(SdkResolverContext context, string packId, string packVersion)
        {
            var dotnetRoot = GetDotNetRoot(context);
            return Path.Combine(dotnetRoot, "packs", packId, packVersion);
        }
    }
}
