// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// One selectable value of a <see cref="FormField"/>.
/// </summary>
/// <param name="Title">The short value label shown both collapsed (as the field's value) and in the expanded picker.</param>
/// <param name="HelperText">Detailed helper/tooltip text shown for this value while the field is expanded.</param>
internal sealed record FieldChoice(string Title, string HelperText);
