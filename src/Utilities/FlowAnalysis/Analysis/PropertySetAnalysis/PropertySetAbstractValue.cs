﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Analyzer.Utilities.FlowAnalysis.Analysis.PropertySetAnalysis
{
    internal enum PropertySetAbstractValue
    {
        /// <summary>
        /// Doesn't matter.
        /// </summary>
        Unknown,

        /// <summary>
        /// Not flagged for badness.
        /// </summary>
        Unflagged,

        /// <summary>
        /// Flagged for badness.
        /// </summary>
        Flagged,

        /// <summary>
        /// Maybe flagged.
        /// </summary>
        MaybeFlagged,
    }
}
