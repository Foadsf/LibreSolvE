// LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Parsing;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnitsNet;
using UnitsNet.Units;

namespace LibreSolvE.Core.Evaluation;

public class ExpressionEvaluatorVisitor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private bool _warningsAsErrors = false;
    private int _undefinedVariableCount = 0;

    // Nullable reference to StatementExecutor for context (e.g., INTEGRAL needs it for table updates)
    private readonly StatementExecutor? _statementExecutor;

    public bool GetWarningsAsErrors() => _warningsAsErrors;
    public void SetWarningsAsErrors(bool value) => _warningsAsErrors = value;

    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, bool warningsAsErrors = false)
        : this(variableStore, functionRegistry, null, warningsAsErrors) // Call the main constructor
    {
    }

    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, StatementExecutor? statementExecutor, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _statementExecutor = statementExecutor; // Can be null
        _warningsAsErrors = warningsAsErrors;
    }

    public int GetUndefinedVariableCount() => _undefinedVariableCount;
    public void ResetUndefinedVariableCount() => _undefinedVariableCount = 0;

    public double Evaluate(ExpressionNode node)
    {
        switch (node)
        {
            case NumberNode num: return num.Value;
            case VariableNode var: return EvaluateVariable(var);
            case BinaryOperationNode binOp: return EvaluateBinaryOperation(binOp);
            case FunctionCallNode funcCall: return EvaluateFunctionCall(funcCall);
            default: throw new NotImplementedException($"Evaluation not implemented for AST node type: {node?.GetType().Name}");
        }
    }

    private double EvaluateVariable(VariableNode var)
    {
        bool wasExplicitlySet = _variableStore.IsExplicitlySet(var.Name);
        double value = _variableStore.GetVariable(var.Name); // GetVariable handles defaults/guesses

        // This check is for solving phase; during initial assignment, defaults are fine.
        if (!wasExplicitlySet && !_variableStore.HasGuessValue(var.Name) && _warningsAsErrors) // Only count as undefined if no guess and in solve mode
        {
            // Check if it's the independent variable of an active ODE solve, which is allowed to be "undefined" initially
            if (_statementExecutor != null && _statementExecutor.GetIntegralTableIndVarName() == var.Name)
            {
                // This is acceptable, its value is being set by the ODE solver loop
            }
            else
            {
                _undefinedVariableCount++;
                // Console.WriteLine($"Debug: Undefined var '{var.Name}' (value: {value}) during eval, warningsAsErrors={_warningsAsErrors}");
                // if (_warningsAsErrors) throw new InvalidOperationException($"Variable '{var.Name}' accessed before assignment during solve step.");
            }
        }
        return value;
    }

    private double EvaluateBinaryOperation(BinaryOperationNode binOp)
    {
        double leftVal = Evaluate(binOp.Left);
        double rightVal = Evaluate(binOp.Right);

        // Basic unit compatibility check (can be expanded)
        // if (binOp.Operator == BinaryOperator.Add || binOp.Operator == BinaryOperator.Subtract)
        // {
        //    CheckUnitCompatibility(binOp.Left, binOp.Right);
        // }

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
            // The INTEGRAL function should not be evaluated directly by this visitor
            // as a simple function call during the assignment phase if it's part of
            // a "y = INTEGRAL(...)" top-level statement.
            // Its execution is orchestrated by StatementExecutor.
            // If this is reached, it might be an INTEGRAL call nested inside another expression,
            // which is more complex and EES typically handles the top-level one as an ODE setup.
            // For now, let's assume top-level INTEGRAL calls are handled by StatementExecutor.
            // A call here could mean it's being evaluated prematurely or in an unsupported nested context.
            // EES returns 0 for Integral(..) if t_sim=0.
            // Let's return the current value of the dependent variable if already in store, or 0.
            // This behavior is to allow IsConstantValue to correctly identify "y=Integral" not as a const.

            // Argument 2 should be the independent variable (e.g. 't')
            // Argument 3 should be lower limit, Argument 4 upper limit
            if (funcCall.Arguments.Count >= 4)
            {
                var lowerLimitNode = funcCall.Arguments[2];
                var upperLimitNode = funcCall.Arguments[3];
                try
                {
                    double lowerLimit = Evaluate(lowerLimitNode);
                    double upperLimit = Evaluate(upperLimitNode);
                    if (Math.Abs(upperLimit - lowerLimit) < 1e-9) return 0; // EES behavior for integral over zero range
                }
                catch
                {
                    // issue evaluating limits, fall through
                }
            }
            // This is a placeholder. The actual ODE solving is handled by StatementExecutor.
            // Returning NaN or throwing an error here might be more appropriate to signal
            // that it shouldn't be evaluated like a regular function during constant folding.
            // For IsConstantValue check, this path makes Integral() non-constant.
            throw new InvalidOperationException("INTEGRAL function evaluation should be orchestrated by StatementExecutor for ODEs. It cannot be simply evaluated as a constant expression.");
        }
        else if (string.Equals(funcCall.FunctionName, "CONVERT", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count != 2) throw new ArgumentException($"Function '{funcCall.FunctionName}' requires exactly 2 arguments (FromUnitString, ToUnitString), but {funcCall.Arguments.Count} were provided.");
            if (funcCall.Arguments[0] is not StringLiteralNode fromArg) throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'm').");
            if (funcCall.Arguments[1] is not StringLiteralNode toArg) throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'ft').");
            return ConvertUnits(fromArg.Value, toArg.Value);
        }
        else if (string.Equals(funcCall.FunctionName, "CONVERTTEMP", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count != 3) throw new ArgumentException($"Function '{funcCall.FunctionName}' requires 3 arguments (FromUnitStr, ToUnitStr, Value).");
            if (funcCall.Arguments[0] is not StringLiteralNode fromArg) throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'C').");
            if (funcCall.Arguments[1] is not StringLiteralNode toArg) throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'K').");

            double valueToConvert = Evaluate(funcCall.Arguments[2]);
            return ConvertTemperature(fromArg.Value, toArg.Value, valueToConvert);
        }
        else // Handle Regular Mathematical/Other Functions
        {
            double[] argValues = new double[funcCall.Arguments.Count];
            for (int i = 0; i < funcCall.Arguments.Count; i++)
            {
                argValues[i] = Evaluate(funcCall.Arguments[i]);
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

            if (fromInfo.Name != toInfo.Name && !(fromInfo.Name == "Dimensionless" || toInfo.Name == "Dimensionless")) // Allow conversion to/from dimensionless
            {
                // A more robust check would be BaseDimensions
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

    // Basic unit compatibility check (can be expanded)
    private void CheckUnitCompatibility(ExpressionNode leftNode, ExpressionNode rightNode)
    {
        string leftUnitStr = GetNodeUnitString(leftNode);
        string rightUnitStr = GetNodeUnitString(rightNode);

        if (!string.IsNullOrEmpty(leftUnitStr) && !string.IsNullOrEmpty(rightUnitStr) &&
            !string.Equals(leftUnitStr, rightUnitStr, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                (_, QuantityInfo leftInfo) = UnitParser.ParseUnitString(leftUnitStr);
                (_, QuantityInfo rightInfo) = UnitParser.ParseUnitString(rightUnitStr);

                if (!leftInfo.BaseDimensions.Equals(rightInfo.BaseDimensions))
                {
                    string leftName = (leftNode as VariableNode)?.Name ?? $"({leftNode})";
                    string rightName = (rightNode as VariableNode)?.Name ?? $"({rightNode})";

                    string errorMsg = $"Unit mismatch: Cannot add/subtract '{leftName}' [{leftUnitStr}] and '{rightName}' [{rightUnitStr}] due to incompatible dimensions ({leftInfo.BaseDimensions} vs {rightInfo.BaseDimensions}).";
                    Console.Error.WriteLine($"Error: {errorMsg}");
                    // if (_warningsAsErrors) throw new InvalidOperationException(errorMsg);
                }
            }
            catch (UnitsNet.UnitNotFoundException ex)
            {
                Console.WriteLine($"Warning: Could not parse unit for compatibility check: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during unit compatibility check: {ex.Message}");
            }
        }
    }

    private string GetNodeUnitString(ExpressionNode node)
    {
        if (node is VariableNode varNode) return _variableStore.GetUnit(varNode.Name);
        // Could try to infer units for constants or other expressions later
        return string.Empty;
    }
}
