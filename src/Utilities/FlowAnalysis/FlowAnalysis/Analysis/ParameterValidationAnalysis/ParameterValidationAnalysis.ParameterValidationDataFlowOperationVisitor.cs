﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.PointsToAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.ParameterValidationAnalysis
{
    using ParameterValidationAnalysisData = DictionaryAnalysisData<AbstractLocation, ParameterValidationAbstractValue>;

    internal partial class ParameterValidationAnalysis : ForwardDataFlowAnalysis<ParameterValidationAnalysisData, ParameterValidationAnalysisContext, ParameterValidationAnalysisResult, ParameterValidationBlockAnalysisResult, ParameterValidationAbstractValue>
    {
        /// <summary>
        /// Operation visitor to flow the location validation values across a given statement in a basic block.
        /// </summary>
        private sealed class ParameterValidationDataFlowOperationVisitor :
            AbstractLocationDataFlowOperationVisitor<ParameterValidationAnalysisData, ParameterValidationAnalysisContext, ParameterValidationAnalysisResult, ParameterValidationAbstractValue>
        {
            private readonly ImmutableDictionary<IParameterSymbol, SyntaxNode>.Builder _hazardousParameterUsageBuilderOpt;

            public ParameterValidationDataFlowOperationVisitor(ParameterValidationAnalysisContext analysisContext)
                : base(analysisContext)
            {
                Debug.Assert(analysisContext.OwningSymbol.Kind == SymbolKind.Method);
                Debug.Assert(analysisContext.PointsToAnalysisResultOpt != null);

                if (analysisContext.TrackHazardousParameterUsages)
                {
                    _hazardousParameterUsageBuilderOpt = ImmutableDictionary.CreateBuilder<IParameterSymbol, SyntaxNode>();
                }
            }

            public override int GetHashCode()
            {
                return HashUtilities.Combine(_hazardousParameterUsageBuilderOpt.GetHashCodeOrDefault(), base.GetHashCode());
            }

            public ImmutableDictionary<IParameterSymbol, SyntaxNode> HazardousParameterUsages
            {
                get
                {
                    Debug.Assert(_hazardousParameterUsageBuilderOpt != null);
                    return _hazardousParameterUsageBuilderOpt.ToImmutable();
                }
            }

            protected override ParameterValidationAbstractValue GetAbstractDefaultValue(ITypeSymbol type) => ValueDomain.Bottom;

            protected override bool HasAnyAbstractValue(ParameterValidationAnalysisData data) => data.Count > 0;

            protected override ParameterValidationAbstractValue GetAbstractValue(AbstractLocation location)
                => CurrentAnalysisData.TryGetValue(location, out var value) ? value : ValueDomain.Bottom;

            protected override void ResetCurrentAnalysisData() => ResetAnalysisData(CurrentAnalysisData);

            private bool IsTrackedLocation(AbstractLocation location) =>
                location.SymbolOpt is IParameterSymbol parameter && parameter.Type.IsReferenceType && parameter.ContainingSymbol == OwningSymbol;

            private bool IsNotOrMaybeValidatedLocation(AbstractLocation location) =>
                CurrentAnalysisData.TryGetValue(location, out var value) &&
                (value == ParameterValidationAbstractValue.NotValidated || value == ParameterValidationAbstractValue.MayBeValidated);

            protected override void StopTrackingAbstractValue(AbstractLocation location) => CurrentAnalysisData.Remove(location);

            protected override void SetAbstractValue(AbstractLocation location, ParameterValidationAbstractValue value)
            {
                if (IsTrackedLocation(location))
                {
                    CurrentAnalysisData[location] = value;
                }
            }

            protected override void SetAbstractValueForAssignment(IOperation target, IOperation assignedValueOperation, ParameterValidationAbstractValue assignedValue, bool mayBeAssignment = false)
            {
                // We are only tracking default parameter locations.
            }

            protected override void SetAbstractValueForTupleElementAssignment(AnalysisEntity tupleElementEntity, IOperation assignedValueOperation, ParameterValidationAbstractValue assignedValue)
            {
                // We are only tracking default parameter locations.
            }

            protected override void SetAbstractValueForArrayElementInitializer(IArrayCreationOperation arrayCreation, ImmutableArray<AbstractIndex> indices, ITypeSymbol elementType, IOperation initializer, ParameterValidationAbstractValue value)
            {
                // We are only tracking default parameter locations.
            }

            protected override void SetValueForParameterPointsToLocationOnEntry(IParameterSymbol parameter, PointsToAbstractValue pointsToAbstractValue)
            {
                if (pointsToAbstractValue.Kind == PointsToAbstractValueKind.KnownLocations)
                {
                    var value = HasValidatedNotNullAttribute(parameter) ? ParameterValidationAbstractValue.Validated : ParameterValidationAbstractValue.NotValidated;
                    SetAbstractValue(pointsToAbstractValue.Locations, value);
                }
            }

            private static bool HasValidatedNotNullAttribute(IParameterSymbol parameter)
                => parameter.GetAttributes().Any(attr => attr.AttributeClass.Name.Equals("ValidatedNotNullAttribute", StringComparison.OrdinalIgnoreCase));

            protected override void EscapeValueForParameterPointsToLocationOnExit(IParameterSymbol parameter, AnalysisEntity analysisEntity, ImmutableHashSet<AbstractLocation> escapedLocations)
            {
                // Mark parameters as validated if they are non-null at all non-exception return paths and null at one of the unhandled throw operations.
                var notValidatedLocations = escapedLocations.Where(IsNotOrMaybeValidatedLocation);
                if (notValidatedLocations.Any())
                {
                    if (TryGetNullAbstractValueAtCurrentBlockEntry(analysisEntity, out NullAbstractValue nullAbstractValue) &&
                        nullAbstractValue == NullAbstractValue.NotNull &&
                        TryGetMergedNullAbstractValueAtUnhandledThrowOperationsInGraph(analysisEntity, out NullAbstractValue mergedValueAtUnhandledThrowOperations) &&
                        mergedValueAtUnhandledThrowOperations != NullAbstractValue.NotNull)
                    {
                        SetAbstractValue(notValidatedLocations, ParameterValidationAbstractValue.Validated);
                    }
                }
            }

            private IEnumerable<AbstractLocation> GetNotValidatedLocations(IOperation operation)
            {
                var pointsToLocation = GetPointsToAbstractValue(operation);
                return pointsToLocation.Locations.Where(IsNotOrMaybeValidatedLocation);
            }

            private static bool IsHazardousIfNull(IOperation operation)
            {
                if (operation.Kind == OperationKind.ConditionalAccessInstance)
                {
                    return false;
                }

                switch (operation.Parent)
                {
                    case IMemberReferenceOperation memberReference:
                        return memberReference.Instance == operation;

                    case IArrayElementReferenceOperation arrayElementReference:
                        return arrayElementReference.ArrayReference == operation;

                    case IInvocationOperation invocation:
                        return invocation.Instance == operation;
                }

                return false;
            }

            private void HandlePotentiallyHazardousOperation(IOperation operation, IEnumerable<AbstractLocation> nonValidatedLocations)
            {
                Debug.Assert(_hazardousParameterUsageBuilderOpt != null);

                if (GetNullAbstractValue(operation) == NullAbstractValue.NotNull)
                {
                    // We are sure the value is non-null, so cannot be hazardous.
                    return;
                }

                HandleHazardousOperation(operation.Syntax, nonValidatedLocations);
            }

            private void HandleHazardousOperation(SyntaxNode syntaxNode, IEnumerable<AbstractLocation> nonValidatedLocations)
            {
                Debug.Assert(_hazardousParameterUsageBuilderOpt != null);

                foreach (var location in nonValidatedLocations)
                {
                    Debug.Assert(IsNotOrMaybeValidatedLocation(location));

                    var parameter = (IParameterSymbol)location.SymbolOpt;
                    if (!_hazardousParameterUsageBuilderOpt.TryGetValue(parameter, out SyntaxNode currentSyntaxNode) ||
                        syntaxNode.SpanStart < currentSyntaxNode.SpanStart)
                    {
                        _hazardousParameterUsageBuilderOpt[parameter] = syntaxNode;
                    }
                }
            }

            protected override ParameterValidationAnalysisData MergeAnalysisData(ParameterValidationAnalysisData value1, ParameterValidationAnalysisData value2)
                => ParameterValidationAnalysisDomainInstance.Merge(value1, value2);
            protected override ParameterValidationAnalysisData GetClonedAnalysisData(ParameterValidationAnalysisData analysisData)
                => GetClonedAnalysisDataHelper(analysisData);
            public override ParameterValidationAnalysisData GetEmptyAnalysisData()
                => GetEmptyAnalysisDataHelper();
            protected override ParameterValidationAnalysisData GetExitBlockOutputData(ParameterValidationAnalysisResult analysisResult)
                => GetClonedAnalysisDataHelper(analysisResult.ExitBlockOutput.Data);
            protected override bool Equals(ParameterValidationAnalysisData value1, ParameterValidationAnalysisData value2)
                => EqualsHelper(value1, value2);

            #region Visit overrides
            public override ParameterValidationAbstractValue Visit(IOperation operation, object argument)
            {
                var value = base.Visit(operation, argument);
                if (operation != null)
                {
                    if (_hazardousParameterUsageBuilderOpt != null &&
                        IsHazardousIfNull(operation))
                    {
                        var notValidatedLocations = GetNotValidatedLocations(operation);
                        HandlePotentiallyHazardousOperation(operation, notValidatedLocations);
                    }
                }

                return value;
            }

            public override ParameterValidationAbstractValue VisitObjectCreation(IObjectCreationOperation operation, object argument)
            {
                var value = base.VisitObjectCreation(operation, argument);
                ProcessRegularInvocationOrCreation(operation.Constructor, operation.Arguments, operation);
                return value;
            }

            public override ParameterValidationAbstractValue VisitInvocation_NonLambdaOrDelegateOrLocalFunction(
                IMethodSymbol targetMethod,
                IOperation visitedInstance,
                ImmutableArray<IArgumentOperation> visitedArguments,
                bool invokedAsDelegate,
                IOperation originalOperation,
                ParameterValidationAbstractValue defaultValue)
            {
                var value = base.VisitInvocation_NonLambdaOrDelegateOrLocalFunction(targetMethod, visitedInstance, visitedArguments, invokedAsDelegate, originalOperation, defaultValue);
                ProcessRegularInvocationOrCreation(targetMethod, visitedArguments, originalOperation);
                return value;
            }

            public override ParameterValidationAbstractValue VisitInvocation_Lambda(
                IFlowAnonymousFunctionOperation lambda,
                ImmutableArray<IArgumentOperation> visitedArguments,
                IOperation originalOperation,
                ParameterValidationAbstractValue defaultValue)
            {
                var value = base.VisitInvocation_Lambda(lambda, visitedArguments, originalOperation, defaultValue);
                ProcessLambdaOrLocalFunctionInvocation(lambda.Symbol, originalOperation);
                return value;
            }

            public override ParameterValidationAbstractValue VisitInvocation_LocalFunction(
                IMethodSymbol localFunction,
                ImmutableArray<IArgumentOperation> visitedArguments,
                IOperation originalOperation,
                ParameterValidationAbstractValue defaultValue)
            {
                var value = base.VisitInvocation_LocalFunction(localFunction, visitedArguments, originalOperation, defaultValue);
                ProcessLambdaOrLocalFunctionInvocation(localFunction, originalOperation);
                return value;
            }

            private void ProcessRegularInvocationOrCreation(IMethodSymbol targetMethod, ImmutableArray<IArgumentOperation> arguments, IOperation operation)
            {
                Debug.Assert(!targetMethod.IsLambdaOrLocalFunctionOrDelegate());

                if (targetMethod.ContainingType.SpecialType == SpecialType.System_String)
                {
                    if (targetMethod.IsStatic &&
                        targetMethod.Name.StartsWith("IsNull", StringComparison.Ordinal) &&
                        targetMethod.Parameters.Length == 1 &&
                        arguments.Length == 1)
                    {
                        // string.IsNullOrXXX check.
                        SetAbstractValue(GetNotValidatedLocations(arguments[0]), ParameterValidationAbstractValue.Validated);
                    }
                }
                else if (targetMethod.Parameters.Length > 0 &&
                         arguments.Length > 0 &&
                         WellKnownTypeProvider.Exception != null &&
                         targetMethod.ContainingType.DerivesFrom(WellKnownTypeProvider.Exception))
                {
                    // FxCop compat: special cases handled by FxCop.
                    //  1. First argument of type System.Runtime.Serialization.SerializationInfo to System.Exception.GetObjectData or its override is validated.
                    //  2. First argument of type System.Runtime.Serialization.SerializationInfo to constructor of System.Exception or its subtype is validated.
                    if (targetMethod.Parameters[0].Type == WellKnownTypeProvider.SerializationInfo)
                    {
                        switch (targetMethod.MethodKind)
                        {
                            case MethodKind.Ordinary:
                                if (targetMethod.Name.Equals("GetObjectData", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetAbstractValue(GetNotValidatedLocations(arguments[0]), ParameterValidationAbstractValue.Validated);
                                }
                                break;

                            case MethodKind.Constructor:
                                SetAbstractValue(GetNotValidatedLocations(arguments[0]), ParameterValidationAbstractValue.Validated);
                                break;
                        }
                    }
                }
                else if (_hazardousParameterUsageBuilderOpt != null &&
                         !targetMethod.IsExternallyVisible() &&
                         TryGetInterproceduralAnalysisResult(operation, out var invokedMethodAnalysisResult))
                {
                    // Check if this private/interal method that has hazardous usages of non-validated argument.
                    Debug.Assert(!targetMethod.IsVirtual && !targetMethod.IsOverride);

                    var hazardousParameterUsagesInInvokedMethod = invokedMethodAnalysisResult.HazardousParameterUsages;
                    if (hazardousParameterUsagesInInvokedMethod.Count > 0)
                    {
                        foreach (var argument in arguments)
                        {
                            var notValidatedLocations = GetNotValidatedLocations(argument);
                            foreach (var location in notValidatedLocations)
                            {
                                var parameter = (IParameterSymbol)location.SymbolOpt;
                                if (hazardousParameterUsagesInInvokedMethod.ContainsKey(parameter))
                                {
                                    HandlePotentiallyHazardousOperation(argument, notValidatedLocations);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            private void ProcessLambdaOrLocalFunctionInvocation(IMethodSymbol targetMethod, IOperation invocation)
            {
                Debug.Assert(targetMethod.MethodKind == MethodKind.LambdaMethod || targetMethod.MethodKind == MethodKind.LocalFunction);

                // Lambda and local function invocations can access captured variables.
                if (_hazardousParameterUsageBuilderOpt != null &&
                    TryGetInterproceduralAnalysisResult(invocation, out var invokedMethodAnalysisResult))
                {
                    var notValidatedLocations = CurrentAnalysisData.Keys.Where(IsNotOrMaybeValidatedLocation);
                    if (notValidatedLocations.Any())
                    {
                        var hazardousParameterUsagesInInvokedMethod = invokedMethodAnalysisResult.HazardousParameterUsages;
                        foreach (var kvp in hazardousParameterUsagesInInvokedMethod)
                        {
                            var parameter = kvp.Key;
                            var syntaxNode = kvp.Value;
                            if (!_hazardousParameterUsageBuilderOpt.ContainsKey(parameter))
                            {
                                HandleHazardousOperation(syntaxNode, notValidatedLocations.Where(l => l.SymbolOpt == parameter));
                            }
                        }
                    }
                }
            }

            #endregion
        }
    }
}
