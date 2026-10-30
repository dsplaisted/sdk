---
title: dotnetup runtime uninstall command
description: Command reference for removing tracked .NET runtime requirements.
ms.topic: reference
ms.date: 08/07/2026
---

# dotnetup runtime uninstall command

## Name

`dotnetup runtime uninstall` - Remove a runtime install spec or an installed runtime version.

## Synopsis

```console
dotnetup runtime uninstall <COMPONENT_SPEC> [options]
```

## Arguments

`COMPONENT_SPEC`

The stored runtime requirement to remove. Use
`component@version-or-channel` to select a component. A value without a
component selects the core .NET runtime. Matching command-line and migration
specifications are removed together. A runtime remains installed if another
requirement or an installed SDK still needs it.

Uninstall accepts one argument and matches standalone specs first. Otherwise, a
full installed version targets that installation. Use `runtime@11.0` or `11.0`
for the core runtime, not `11.0@core`. Channels without a matching standalone spec
are errors and are not resolved over the network.

## Options

| Option | Description |
| --- | --- |
| `--manifest-path <MANIFEST_PATH>` | Use a custom manifest file. |
| `--install-path <INSTALL_PATH>` | Select the installation root. |
| `--remove-install-specs` | For a physical-version target, remove affected specs and run normal garbage collection. With a matching standalone spec, remove only matching standalone specs. |
| `--keep-install-specs` | Remove only the named installation record and collect unused components without changing specs or other installation records. Invalid when a standalone spec matches. |
| `--interactive [true\|false]` | Enable the physical-version action prompt. Defaults to enabled outside CI when output is not redirected. |
| `--non-interactive` | Disable prompts. Physical removal with affected specs requires an explicit action option. |
| `-?`, `-h`, `--help` | Show command help. |

## Behavior

Matching-spec removal is immediate and runs normal garbage collection across the
root, including SDKs and other runtime components. Retained versions are explained
with their remaining requirements, using the `dotnetup list` columns and source labels.

For a physical version with affected specs, select removal of those specs,
removal without changing specs, or Cancel (the default). The two action options
are mutually exclusive and bypass that prompt, not other errors. Without affected
specs, only the named installation is removed. Keeping specs protects components
referenced by every surviving installation record; a runtime's files can remain
when another SDK still uses them. Later install or update commands may restore
versions required by retained specs.

Cancellation, revalidation, and partial-deletion recovery follow the same rules as
[`dotnetup sdk uninstall`](dotnetup-sdk-uninstall.md#behavior).

## Examples

```dotnetcli
dotnetup runtime uninstall 10.0
dotnetup runtime uninstall aspnetcore@10.0
dotnetup runtime uninstall runtime@10.0.5 --keep-install-specs --non-interactive
dotnetup runtime uninstall aspnetcore@10.0.5 --remove-install-specs --non-interactive
dotnetup runtime uninstall windowsdesktop@10.0 --install-path .\.dotnet
```
