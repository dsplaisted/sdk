---
title: Repository SDK requirements with dotnetup
description: Learn how dotnetup reads global.json and tracks repository SDK requirements.
ms.topic: conceptual
ms.date: 08/07/2026
---

# Repository SDK requirements with dotnetup

`dotnetup` uses `global.json` to associate an SDK requirement with a
repository or directory tree. From the current directory, it searches for
`global.json` and then searches each parent directory until it finds one.

## Install from global.json

Run an SDK install command without a channel:

```dotnetcli
dotnetup install
```

If the nearest `global.json` has an `sdk.version`, `dotnetup` derives an
install specification from the version and `rollForward` value. It records
the full path to the file as the specification source. If no `global.json` is
found, or it has no `sdk.version`, the command uses the `latest` channel.
Unreadable files or invalid SDK settings are reported as errors.

## rollForward mapping

`dotnetup` maps `global.json` SDK selection policies to updateable channels:

| `rollForward` value | Dotnetup channel for SDK `10.0.103` |
| --- | --- |
| Omitted, `patch`, or `latestPatch` | `10.0.1xx` |
| `feature` or `latestFeature` | `10.0` |
| `minor` or `latestMinor` | `10` |
| `major` or `latestMajor` | `latest` |
| `disable` | Exactly `10.0.103` |

Dotnetup always chooses the latest eligible SDK in that scope, never an SDK
older than `sdk.version`. For example, `10.0.103` with an omitted `rollForward`
or `patch` can update to `10.0.105`, but not `10.0.202`. `disable` requires the
exact version currently specified in the file.

`allowPrerelease: false` excludes preview SDKs unless `sdk.version` itself is
a prerelease. When omitted, prerelease SDKs are eligible. This also applies
to repository requirements that map to `latest`; the standalone `latest`
channel continues to select stable releases.

Dotnetup selects the greatest matching version regardless of support phase.
Maintenance and end-of-life releases remain eligible, but an older release
does not take precedence over a newer matching version.

Install and update read the current file to choose an available SDK. List and
garbage collection use the same rules to choose among installed SDKs. Garbage
collection keeps the selected SDK and removes other SDKs unless another
requirement needs them.

This is dotnetup's SDK management policy, not the .NET host's SDK selection
algorithm. Dotnetup treats the non-`latest` policies as their `latest` counterparts
without rewriting `rollForward` in the file. The `dotnet` host still applies its
own rules when running a command.

## Installation path from global.json

If `sdk.paths` contains an entry, `dotnetup` uses the first path. A relative
path is resolved from the directory that contains `global.json`.

Installation-path precedence is:

1. `--install-path`.
1. The first `sdk.paths` entry in the nearest `global.json`.
1. The default dotnetup-managed .NET installation root.

## Keep repository files current

Pass `--update-global-json` to replace `sdk.version` with the concrete SDK
version that was installed or updated:

```dotnetcli
dotnetup install --update-global-json
dotnetup sdk update --update-global-json
```

Only global.json-sourced SDK specifications are updated by the update
workflow. The modifier preserves the other JSON properties, formatting, and
detected text encoding.

## Remove a repository requirement

Update `sdk.version` to change a repository's SDK requirement. To remove the
requirement, remove `sdk.version` or delete `global.json` if the file is no longer
needed. Dotnetup picks up these changes during garbage collection and removes
unused installations.

You can also remove the tracked registration explicitly using its channel:

```dotnetcli
dotnetup sdk uninstall 10.0.1xx --source globaljson
```

Use `dotnetup list` to find the `global.json` path.

If `global.json` cannot be read or contains an invalid SDK requirement, dotnetup
reports a warning and uses the last successfully resolved requirement for update
and garbage collection. The saved requirement includes the channel, minimum SDK
version, and prerelease eligibility. `dotnetup list` labels this fallback as
`last known` and shows the read error. Listing does not modify the saved requirement.

Update continues processing all requirements and does not fail solely because a
repository uses this fallback. Even with `--update-global-json`, it does not
rewrite a file that could not be read. A subsequent successful read replaces the
saved requirement. Missing files and files with no SDK version still deactivate
the registration instead of using the fallback.

Older registrations might have only a recorded channel, without the complete
requirement. If such a file cannot be read, update reports a failure for that
repository but continues processing other requirements. Garbage collection skips
that installation root, with a warning, rather than guessing which SDKs are safe
to remove.

## See also

- [Manage repository SDK requirements](../usecases/install-with-global-json.md)
- [`global.json` overview](https://learn.microsoft.com/dotnet/core/tools/global-json)
- [`dotnetup sdk install`](../reference/dotnetup-sdk-install.md)
