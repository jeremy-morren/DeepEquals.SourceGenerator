// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

namespace DeepEquals.SourceGenerator.Model;

/// <summary>A warning the generated files disable, and the comment that says why, written on its own pragma line.</summary>
internal readonly record struct SuppressedDiagnostic(string Id, string Reason);
