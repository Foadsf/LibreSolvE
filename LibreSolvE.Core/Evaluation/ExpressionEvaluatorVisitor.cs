// LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Parsing; // Assuming EesParser is here for any direct use (though not typical in evaluator)
using System;
using System.Collections.Generic;
using System.Globalization;
using UnitsNet;
using UnitsNet.Units;
using System.Linq; // For LINQ methods if used in function implementations

namespace LibreSolvE.Core.Evaluation;

// Custom exception to signal that an expression isn't constant due to an undefined variable (for IsConstantValue)
public class VariableNotConstantException : InvalidOperationException
{
    public VariableNotConstantException(string message) : base(message) { }
}

public class ExpressionEvaluatorVisitor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly StatementExecutor? _statementExecutor;
    private bool _warningsAsErrors = false;

    private bool _evaluationUsedNonExplicitVariable = false; // This flag is crucial

    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, StatementExecutor? statementExecutor, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _statementExecutor = statementExecutor;
        _warningsAsErrors = warningsAsErrors;
    }

    public bool GetWarningsAsErrors() => _warningsAsErrors;
    public void SetWarningsAsErrors(bool value) => _warningsAsErrors = value;

    public void ResetEvaluationDependencyFlag()
    {
        _evaluationUsedNonExplicitVariable = false;
    }

    public bool DidEvaluationUseNonExplicitVariable()
    {
        return _evaluationUsedNonExplicitVariable;
    }

    public double Evaluate(ExpressionNode node)
    {
        switch (node)
        {
            case NumberNode num:
                return num.Value;
            case VariableNode varNode:
                // If this variable is NOT explicitly set by user OR solved by a previous step,
                // then this evaluation is relying on a guess or default.
                if (!_variableStore.IsExplicitlySet(varNode.Name) &&
                    !_variableStore.IsSolvedSet(varNode.Name))
                {
                    _evaluationUsedNonExplicitVariable = true;
                    // Log if in a generally strict mode (warningsAsErrors) but don't throw here for IsConstantValue logic
                    if (_warningsAsErrors)
                    {
                        // This warning is now more general if warningsAsErrors is true for other reasons
                        // Console.WriteLine($"Warning (Evaluator): Variable '{varNode.Name}' accessed using guess/default during strict evaluation.");
                    }
                }
                return _variableStore.GetVariable(varNode.Name); // Always returns a value (guess/default if not set)
            case BinaryOperationNode binOp:
                return EvaluateBinaryOperation(binOp);
            case FunctionCallNode funcCall:
                return EvaluateFunctionCall(funcCall);
            default:
                throw new NotImplementedException($"Evaluation not implemented for AST node type: {node?.GetType().Name}");
        }
    }

    private double EvaluateBinaryOperation(BinaryOperationNode binOp)
    {
        double leftVal = Evaluate(binOp.Left);   // These will set _evaluationUsedNonExplicitVariable if applicable
        double rightVal = Evaluate(binOp.Right); // to their respective sub-expressions.

        return binOp.Operator switch
        {
            BinaryOperator.Add => leftVal + rightVal,
            BinaryOperator.Subtract => leftVal - rightVal,
            BinaryOperator.Multiply => leftVal * rightVal,
            BinaryOperator.Divide => rightVal == 0
                                   ? throw new DivideByZeroException($"Division by zero: {binOp.Left} / {binOp.Right} (evaluating to {leftVal} / {rightVal})")
                                   : leftVal / rightVal,
            BinaryOperator.Power => Math.Pow(leftVal, rightVal),
            _ => throw new NotImplementedException($"Operator not implemented: {binOp.Operator}"),
        };
    }

    private double EvaluateFunctionCall(FunctionCallNode funcCall)
    {
        if (string.Equals(funcCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
        {
            // INTEGRAL means the expression is definitely not a simple constant for assignment.
            _evaluationUsedNonExplicitVariable = true;
            // If called outside IsConstantValue (i.e., _warningsAsErrors is false), it's an error.
            if (!_warningsAsErrors && _statementExecutor == null) // Check if we're in a context that can handle INTEGRAL
            {
                throw new InvalidOperationException("INTEGRAL function called outside of orchestrated ODE solving context.");
            }
            // For IsConstantValue, setting the flag is enough. Return a placeholder.
            // For actual execution by StatementExecutor, it won't call Evaluate on INTEGRAL this way.
            return 0;
        }
        // ... (CONVERT, CONVERTTEMP logic - ensure they also propagate _evaluationUsedNonExplicitVariable if their *value* argument does)
        else if (string.Equals(funcCall.FunctionName, "CONVERTTEMP", StringComparison.OrdinalIgnoreCase))
        {
            // ... (argument checks as before) ...
            double valueToConvert = Evaluate(funcCall.Arguments[2]); // This Evaluate call will set the flag if needed
            return ConvertTemperature(((StringLiteralNode)funcCall.Arguments[0]).Value, ((StringLiteralNode)funcCall.Arguments[1]).Value, valueToConvert);
        }
        else // Regular functions
        {
            double[] argValues = new double[funcCall.Arguments.Count];
            for (int i = 0; i < funcCall.Arguments.Count; i++)
            {
                argValues[i] = Evaluate(funcCall.Arguments[i]); // This will set the flag if any argument is non-explicit
            }
            return _functionRegistry.EvaluateFunction(funcCall.FunctionName, argValues);
        }
    }

    private double ConvertUnits(string fromUnitStr, string toUnitStr)
    {
        Console.WriteLine($"Debug: CONVERT Attempt: From='{fromUnitStr}', To='{toUnitStr}'");
        try
        {
            (Enum fromUnitEnum, QuantityInfo fromInfo) = UnitParser.ParseUnitString(fromUnitStr);
            (Enum toUnitEnum, QuantityInfo toInfo) = UnitParser.ParseUnitString(toUnitStr);

            if (fromInfo.Name != toInfo.Name && !(fromInfo.Name == "Dimensionless" || toInfo.Name == "Dimensionless"))
            {
                if (!fromInfo.BaseDimensions.Equals(toInfo.BaseDimensions))
                {
                    throw new ArgumentException($"Units are not compatible for conversion: '{fromUnitStr}' ({fromInfo.Name}, Dim: {fromInfo.BaseDimensions}) and '{toUnitStr}' ({toInfo.Name}, Dim: {toInfo.BaseDimensions}).");
                }
            }

            IQuantity fromQuantity = Quantity.From(1.0, fromUnitEnum);
            IQuantity toQuantity = fromQuantity.ToUnit(toUnitEnum);
            return Convert.ToDouble(toQuantity.Value, CultureInfo.InvariantCulture);
        }
        catch (UnitsNet.UnitNotFoundException ex) { throw new ArgumentException($"Unit error in CONVERT function: {ex.Message}", ex); }
        catch (Exception ex) { throw new InvalidOperationException($"Error during CONVERT from '{fromUnitStr}' to '{toUnitStr}'. Details: {ex.Message}", ex); }
    }

    private double ConvertTemperature(string fromUnitStr, string toUnitStr, double valueToConvert)
    {
        Console.WriteLine($"Debug: CONVERTTEMP Attempt: From='{fromUnitStr}', To='{toUnitStr}', Value={valueToConvert}");
        try
        {
            TemperatureUnit fromTempUnit = Temperature.ParseUnit(fromUnitStr, CultureInfo.InvariantCulture);
            TemperatureUnit toTempUnit = Temperature.ParseUnit(toUnitStr, CultureInfo.InvariantCulture);
            Temperature temp = Temperature.From(valueToConvert, fromTempUnit);
            return temp.ToUnit(toTempUnit).Value;
        }
        catch (UnitsNet.UnitNotFoundException ex) { throw new ArgumentException($"Temperature unit error in CONVERTTEMP: {ex.Message}", ex); }
        catch (Exception ex) { throw new InvalidOperationException($"Error during CONVERTTEMP from '{fromUnitStr}' to '{toUnitStr}' for value {valueToConvert}. Details: {ex.Message}", ex); }
    }

    // Optional: Placeholder for CheckUnitCompatibility if you re-introduce it
    // private void CheckUnitCompatibility(ExpressionNode leftNode, ExpressionNode rightNode) { /* ... */ }
    // private string GetNodeUnitString(ExpressionNode node) { /* ... */ }
}
