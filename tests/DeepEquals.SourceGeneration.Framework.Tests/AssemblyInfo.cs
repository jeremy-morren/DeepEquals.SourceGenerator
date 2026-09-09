// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using Xunit;

// Hash tests set the process-wide seed; the pool seam is process-wide too. Run test classes one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
