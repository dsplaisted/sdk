// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Deployment.DotNet.Releases;
using Microsoft.Dotnet.Installation;
using Microsoft.Dotnet.Installation.Internal;
using Microsoft.DotNet.NativeWrapper;
using Spectre.Console;
using SpectreAnsiConsole = Spectre.Console.AnsiConsole;

namespace Microsoft.DotNet.Tools.Bootstrapper;

internal class ArchiveInstallationValidator : IInstallationValidator
{
    private const string HostFxrRuntimeProperty = "HOSTFXR_PATH";
    private static readonly Dictionary<InstallComponent, string> RuntimeMonikerByComponent = new()
    {
        [InstallComponent.Runtime] = "Microsoft.NETCore.App",
        [InstallComponent.ASPNETCore] = "Microsoft.AspNetCore.App",
        [InstallComponent.WindowsDesktop] = "Microsoft.WindowsDesktop.App"
    };

    public bool Validate(DotnetInstall install)
    {
        string? installRoot = install.InstallRoot.Path;
        SpectreAnsiConsole.WriteLine("Validating install at: " + installRoot);
        if (string.IsNullOrEmpty(installRoot))
        {
            return false;
        }

        string dotnetMuxerPath = Path.Combine(installRoot, DotnetupUtilities.GetDotnetExeName());
        if (!File.Exists(dotnetMuxerPath))
        {
            SpectreAnsiConsole.MarkupLine($"[red]Dotnet muxer not found at: {dotnetMuxerPath}[/]");
            return false;
        }

        string resolvedVersion = install.Version.ToString();
        if (!ValidateComponentLayout(installRoot, resolvedVersion, install.Component))
        {
            SpectreAnsiConsole.MarkupLine($"[red]Component layout validation failed[/]");
            return false;
        }

        if (!ValidateWithHostFxr(installRoot, install.Version, install.Component))
        {
            SpectreAnsiConsole.MarkupLine($"[red]Host FXR validation failed[/]");
            return false;
        }

        // We should also validate whether the host is the maximum version or higher than all installed versions.

        return true;
    }

    private static bool ValidateComponentLayout(string installRoot, string resolvedVersion, InstallComponent component)
    {
        if (component == InstallComponent.SDK)
        {
            string sdkDirectory = Path.Combine(installRoot, "sdk", resolvedVersion);
            return Directory.Exists(sdkDirectory);
        }

        if (RuntimeMonikerByComponent.TryGetValue(component, out string? runtimeMoniker))
        {
            string runtimeDirectory = Path.Combine(installRoot, "shared", runtimeMoniker, resolvedVersion);
            return Directory.Exists(runtimeDirectory);
        }

        return false;
    }

    private bool ValidateWithHostFxr(string installRoot, ReleaseVersion resolvedVersion, InstallComponent component)
    {
        try
        {
            var locator = new Microsoft.Extensions.DotNetPInvokeLocator();
            var installationInfo = locator.GetInstallationInfoAsync("/", installRoot, CancellationToken.None).GetAwaiter().GetResult();

            if (!installationInfo.IsSuccess)
            {
                SpectreAnsiConsole.MarkupLine($"[red]Error getting installation info{Markup.Escape(installationInfo.ErrorMessage ?? "")}[/]");
                if (installationInfo.Exception != null)
                {
                    SpectreAnsiConsole.MarkupLine($"[red]Exception: {Markup.Escape(installationInfo.Exception.ToString())}[/]");
                }
                return false;
            }

            // Print comprehensive installation info
            SpectreAnsiConsole.MarkupLine("[green]Installation Info Details:[/]");
            SpectreAnsiConsole.WriteLine($"  Success: {installationInfo.IsSuccess}");
            
            

            if (installationInfo.Data != null)
            {
                SpectreAnsiConsole.WriteLine($"  Host: {installationInfo.Data.Host}");
                SpectreAnsiConsole.WriteLine($"  Root: {installationInfo.Data.DotNetRoot}");


                // Print SDK information
                if (installationInfo.Data.Sdks != null)
                {
                    SpectreAnsiConsole.WriteLine($"  SDKs ({installationInfo.Data.Sdks.Count} found):");
                    foreach (var sdk in installationInfo.Data.Sdks)
                    {
                        SpectreAnsiConsole.WriteLine($"    - Version: {sdk.Version}, Path: {sdk.Path}");
                    }
                }
                else
                {
                    SpectreAnsiConsole.WriteLine("  SDKs: None found");
                }

                // Print Framework/Runtime information
                if (installationInfo.Data.Frameworks != null)
                {
                    SpectreAnsiConsole.WriteLine($"  Frameworks/Runtimes ({installationInfo.Data.Frameworks.Count} found):");
                    foreach (var framework in installationInfo.Data.Frameworks)
                    {
                        SpectreAnsiConsole.WriteLine($"    - Name: {framework.Name}, Version: {framework.Version}, Path: {framework.Path}");
                    }
                }
                else
                {
                    SpectreAnsiConsole.WriteLine("  Frameworks/Runtimes: None found");
                }
            }
            else
            {
                SpectreAnsiConsole.WriteLine("  Data: null");
            }

            

            //var environmentInfo = HostFxrWrapper.getInfo(installRoot);

            if (component == InstallComponent.SDK)
            {

                // foreach (var sdk in environmentInfo.SdkInfo)
                // {
                //     SpectreAnsiConsole.WriteLine($"Found SDK: Version={sdk.Version}, Path={sdk.Path}");
                // }

                if (installationInfo?.Data?.Sdks != null)
                {
                    SpectreAnsiConsole.WriteLine($"{installationInfo.Data.Sdks.Count} SDKs found in installation info:");
                    foreach (var sdk in installationInfo.Data.Sdks)
                    {
                        SpectreAnsiConsole.WriteLine($"Found SDK: Version={sdk.Version}, Path={sdk.Path}");
                    }
                }
                else
                {
                    SpectreAnsiConsole.WriteLine("No SDKs found in installation info.");
                }

                string expectedPath = Path.Combine(installRoot, "sdk", resolvedVersion.ToString());

                return installationInfo?.Data?.Sdks?.Any(sdk =>
                    string.Equals(sdk.Version.ToString(), resolvedVersion.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    DotnetupUtilities.PathsEqual(sdk.Path, expectedPath)) ?? false;

                
                // return environmentInfo.SdkInfo.Any(sdk =>
                //     string.Equals(sdk.Version.ToString(), resolvedVersion.ToString(), StringComparison.OrdinalIgnoreCase) &&
                //     DotnetupUtilities.PathsEqual(sdk.Path, expectedPath));
            }

            if (!RuntimeMonikerByComponent.TryGetValue(component, out string? runtimeMoniker))
            {
                return false;
            }

            string expectedRuntimePath = Path.Combine(installRoot, "shared", runtimeMoniker, resolvedVersion.ToString());
            
            return installationInfo?.Data?.Frameworks?.Any(runtime =>
                string.Equals(runtime.Name, runtimeMoniker, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(runtime.Version.ToString(), resolvedVersion.ToString(), StringComparison.OrdinalIgnoreCase) &&
                DotnetupUtilities.PathsEqual(runtime.Path, expectedRuntimePath)) ?? false;

            // return environmentInfo.RuntimeInfo.Any(runtime =>
            //     string.Equals(runtime.Name, runtimeMoniker, StringComparison.OrdinalIgnoreCase) &&
            //     string.Equals(runtime.Version.ToString(), resolvedVersion.ToString(), StringComparison.OrdinalIgnoreCase) &&
            //     DotnetupUtilities.PathsEqual(runtime.Path, expectedRuntimePath));
        }
        catch (Exception ex)
        {
            SpectreAnsiConsole.MarkupLine($"[red]Host FXR validation encountered an error: {Markup.Escape(ex.ToString())}[/]");
            return false;
        }
    }
}
