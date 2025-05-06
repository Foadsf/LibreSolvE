// LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs - FINAL CORRECTED VERSION
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Parsing;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnitsNet; // Use the base namespace
using UnitsNet.Units; // Use the Units namespace for enums

namespace LibreSolvE.Core.Evaluation;

public class ExpressionEvaluatorVisitor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private bool _warningsAsErrors = false;
    private int _undefinedVariableCount = 0;

    private readonly StatementExecutor _statementExecutor;

    // Unit Parser instance - Use the static methods from UnitParser class
    // No need for _unitCache field here anymore

    // LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs

    // original constructor
    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _warningsAsErrors = warningsAsErrors;
        _statementExecutor = null; // No statement executor in this case
    }

    // constructor overload
    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, StatementExecutor statementExecutor, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _statementExecutor = statementExecutor ?? throw new ArgumentNullException(nameof(statementExecutor));
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
        // Logic remains the same
        bool wasExplicitlySet = _variableStore.IsExplicitlySet(var.Name);
        double value = _variableStore.GetVariable(var.Name);
        if (!wasExplicitlySet)
        {
            _undefinedVariableCount++;
            if (_warningsAsErrors) throw new InvalidOperationException($"Variable '{var.Name}' accessed before assignment during solve step.");
        }
        return value;
    }

    private double EvaluateBinaryOperation(BinaryOperationNode binOp)
    {
        // Logic remains the same
        double leftVal = Evaluate(binOp.Left);
        double rightVal = Evaluate(binOp.Right);
        if (binOp.Operator == BinaryOperator.Add || binOp.Operator == BinaryOperator.Subtract)
        {
            CheckUnitCompatibility(binOp.Left, binOp.Right);
        }
        return binOp.Operator switch
        {
            BinaryOperator.Add => leftVal + rightVal,
            BinaryOperator.Subtract => leftVal - rightVal,
            BinaryOperator.Multiply => leftVal * rightVal,
            BinaryOperator.Divide => rightVal == 0
                                   ? throw new DivideByZeroException($"Division by zero: {binOp.Left} / {binOp.Right}")
                                   : leftVal / rightVal,
            BinaryOperator.Power => Math.Pow(leftVal, rightVal),
            _ => throw new NotImplementedException($"Operator not implemented: {binOp.Operator}"),
        };
    }

    private double EvaluateFunctionCall(FunctionCallNode funcCall)
    {
        // --- Special Handling for CONVERT ---
        if (string.Equals(funcCall.FunctionName, "CONVERT", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count != 2) throw new ArgumentException($"Function '{funcCall.FunctionName}' requires exactly 2 arguments (FromUnitString, ToUnitString), but {funcCall.Arguments.Count} were provided.");

            // *** Ensure arguments are evaluated correctly - they should resolve to StringLiteralNodes ***
            // We need to change how arguments are processed for functions expecting literals vs values.
            // For now, we'll directly check the AST structure passed in funcCall.Arguments.

            if (funcCall.Arguments[0] is not StringLiteralNode fromArg)
            {
                throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'm'), but got {funcCall.Arguments[0].GetType().Name}.");
            }
            if (funcCall.Arguments[1] is not StringLiteralNode toArg)
            {
                throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'ft'), but got {funcCall.Arguments[1].GetType().Name}.");
            }

            // Use the .Value property of the StringLiteralNode
            string fromUnitStr = fromArg.Value;
            string toUnitStr = toArg.Value;

            // Call the existing helper which contains the UnitsNet logic
            return ConvertUnits(fromUnitStr, toUnitStr);
        }

        // --- Special Handling for CONVERTTEMP (Placeholder) ---
        else if (string.Equals(funcCall.FunctionName, "CONVERTTEMP", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count != 3)
            {
                throw new ArgumentException($"Function '{funcCall.FunctionName}' requires exactly 3 arguments (FromUnitString, ToUnitString, Value), but {funcCall.Arguments.Count} were provided.");
            }
            if (funcCall.Arguments[0] is not StringLiteralNode fromArg)
            {
                throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'C'), but got {funcCall.Arguments[0].GetType().Name}.");
            }
            if (funcCall.Arguments[1] is not StringLiteralNode toArg)
            {
                throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'K'), but got {funcCall.Arguments[1].GetType().Name}.");
            }

            string fromUnitStr = fromArg.Value;
            string toUnitStr = toArg.Value;
            double valueToConvert = Evaluate(funcCall.Arguments[2]); // Evaluate the third argument numerically

            Console.WriteLine($"Debug: CONVERTTEMP Attempt: From='{fromUnitStr}', To='{toUnitStr}', Value={valueToConvert}");

            try
            {
                // Parse the unit strings into TemperatureUnit enums
                TemperatureUnit fromTempUnit = Temperature.ParseUnit(fromUnitStr, CultureInfo.InvariantCulture);
                TemperatureUnit toTempUnit = Temperature.ParseUnit(toUnitStr, CultureInfo.InvariantCulture);

                // Perform the temperature conversion
                Temperature temp = Temperature.From(valueToConvert, fromTempUnit);
                double convertedValue = temp.ToUnit(toTempUnit).Value;

                return convertedValue;
            }
            catch (UnitsNet.UnitNotFoundException ex)
            {
                throw new ArgumentException($"Temperature unit error in CONVERTTEMP function: {ex.Message}", ex);
            }
            catch (ArgumentException ex) // Catch errors from ParseUnit if string is invalid
            {
                throw new ArgumentException($"Invalid temperature unit string in CONVERTTEMP function: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error during CONVERTTEMP from '{fromUnitStr}' to '{toUnitStr}' for value {valueToConvert}. Details: {ex.Message}", ex);
            }

        }
        // --- Special Handling for INTEGRAL ---
        else if (string.Equals(funcCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count < 3 || funcCall.Arguments.Count > 5)
            {
                throw new ArgumentException($"Function '{funcCall.FunctionName}' requires 3-5 arguments (Integrand, VarName, LowerLimit, UpperLimit, [StepSize])");
            }

            // Get the integrand variable name (should be a variable node)
            string integrandVarName;
            if (funcCall.Arguments[0] is VariableNode integrandVarNode)
            {
                integrandVarName = integrandVarNode.Name;
            }
            else
            {
                throw new ArgumentException($"First argument for '{funcCall.FunctionName}' must be a variable name");
            }

            // Get independent variable name (should be a variable node)
            string independentVarName;
            if (funcCall.Arguments[1] is VariableNode independentVarNode)
            {
                independentVarName = independentVarNode.Name;
            }
            else
            {
                throw new ArgumentException($"Second argument for '{funcCall.FunctionName}' must be a variable name");
            }

            // Evaluate lower limit
            double lowerLimit = Evaluate(funcCall.Arguments[2]);

            // Evaluate upper limit
            double upperLimit = Evaluate(funcCall.Arguments[3]);

            // Get optional step size (if provided)
            double stepSize = 0.0; // Default to adaptive step size
            if (funcCall.Arguments.Count >= 5)
            {
                stepSize = Evaluate(funcCall.Arguments[4]);
            }

            // Create and configure ODE solver
            var odeSolver = new OdeSolver(
                _variableStore,
                _functionRegistry,
                integrandVarName,
                independentVarName,
                lowerLimit,
                upperLimit,
                stepSize);

            // Solve the ODE
            return odeSolver.Solve();
        }
        // --- Special Handling for INTEGRAL ---
        else if (string.Equals(funcCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
        {
            if (funcCall.Arguments.Count < 3 || funcCall.Arguments.Count > 5)
            {
                throw new ArgumentException($"Function '{funcCall.FunctionName}' requires 3-5 arguments (Integrand, VarName, LowerLimit, UpperLimit, [StepSize])");
            }

            // Get the integrand variable name (should be a variable node)
            string integrandVarName;
            if (funcCall.Arguments[0] is VariableNode integrandVarNode)
            {
                integrandVarName = integrandVarNode.Name;
            }
            else
            {
                throw new ArgumentException($"First argument for '{funcCall.FunctionName}' must be a variable name");
            }

            // Get independent variable name (should be a variable node)
            string independentVarName;
            if (funcCall.Arguments[1] is VariableNode independentVarNode)
            {
                independentVarName = independentVarNode.Name;
            }
            else
            {
                throw new ArgumentException($"Second argument for '{funcCall.FunctionName}' must be a variable name");
            }

            // Evaluate lower limit
            double lowerLimit = Evaluate(funcCall.Arguments[2]);

            // Evaluate upper limit
            double upperLimit = Evaluate(funcCall.Arguments[3]);

            // Get optional step size (if provided)
            double stepSize = 0.0; // Default to adaptive step size
            if (funcCall.Arguments.Count >= 5)
            {
                stepSize = Evaluate(funcCall.Arguments[4]);
            }

            // Create and configure ODE solver
            var odeSolver = new OdeSolver(
                _variableStore,
                _functionRegistry,
                integrandVarName,
                independentVarName,
                lowerLimit,
                upperLimit,
                stepSize);

            // If we have a StatementExecutor reference, configure the solver with adaptive settings
            if (_statementExecutor != null)
            {
                _statementExecutor.ConfigureOdeSolver(odeSolver);
            }

            // Solve the ODE
            double result = odeSolver.Solve();

            // If we have a StatementExecutor reference, update the integral table
            if (_statementExecutor != null)
            {
                var results = odeSolver.GetResults();
                _statementExecutor.UpdateIntegralTable(independentVarName, integrandVarName, results.Times, results.Values);
            }

            return result;
        }
        // --- Handle Regular Mathematical/Other Functions ---
        else
        {
            // Evaluate all arguments first
            double[] argValues = new double[funcCall.Arguments.Count];
            for (int i = 0; i < funcCall.Arguments.Count; i++)
            {
                argValues[i] = Evaluate(funcCall.Arguments[i]);
            }

            // Call the function from the registry
            // EvaluateFunction already has exception handling
            return _functionRegistry.EvaluateFunction(funcCall.FunctionName, argValues);
        }
    }

    // Helper function containing the core CONVERT logic
    private double ConvertUnits(string fromUnitStr, string toUnitStr)
    {
        Console.WriteLine($"Debug: CONVERT Attempt: From='{fromUnitStr}', To='{toUnitStr}'");
        try
        {
            (Enum fromUnitEnum, QuantityInfo fromInfo) = UnitParser.ParseUnitString(fromUnitStr);
            (Enum toUnitEnum, QuantityInfo toInfo) = UnitParser.ParseUnitString(toUnitStr);

            if (fromInfo.Name != toInfo.Name)
            {
                throw new ArgumentException($"Units are not compatible for conversion: '{fromUnitStr}' ({fromInfo.Name}) and '{toUnitStr}' ({toInfo.Name}).");
            }

            IQuantity fromQuantity = Quantity.From(1.0, fromUnitEnum);
            IQuantity toQuantity = fromQuantity.ToUnit(toUnitEnum);
            return Convert.ToDouble(toQuantity.Value);
        }
        catch (UnitsNet.UnitNotFoundException ex) { throw new ArgumentException($"Unit error in CONVERT function: {ex.Message}", ex); }
        catch (Exception ex) { throw new InvalidOperationException($"Error during CONVERT from '{fromUnitStr}' to '{toUnitStr}'. Details: {ex.Message}", ex); }
    }

    // Helper for the temporary Variable-as-Unit workaround for CONVERT
    private double ConvertUnitsViaVariables(VariableNode fromVar, VariableNode toVar)
    {
        string fromUnitStr = fromVar.Name;
        string toUnitStr = toVar.Name;
        return ConvertUnits(fromUnitStr, toUnitStr); // Reuse main logic
    }


    private void CheckUnitCompatibility(ExpressionNode leftNode, ExpressionNode rightNode)
    {
        string leftUnitStr = GetNodeUnitString(leftNode);
        string rightUnitStr = GetNodeUnitString(rightNode);

        if (!string.IsNullOrEmpty(leftUnitStr) && !string.IsNullOrEmpty(rightUnitStr))
        {
            try
            {
                // *** FIXED: Use UnitParser.ParseUnitString ***
                (_, QuantityInfo leftInfo) = UnitParser.ParseUnitString(leftUnitStr);
                (_, QuantityInfo rightInfo) = UnitParser.ParseUnitString(rightUnitStr);

                // Compare BaseDimensions
                if (!leftInfo.BaseDimensions.Equals(rightInfo.BaseDimensions))
                {
                    string leftName = (leftNode as VariableNode)?.Name ?? $"({leftNode})";
                    string rightName = (rightNode as VariableNode)?.Name ?? $"({rightNode})";

                    var ex = new InvalidOperationException($"Unit mismatch: Cannot add/subtract '{leftName}' [{leftUnitStr}] ({leftInfo.Name}) and '{rightName}' [{rightUnitStr}] ({rightInfo.Name}) due to incompatible dimensions ({leftInfo.BaseDimensions} vs {rightInfo.BaseDimensions}).");
                    Console.Error.WriteLine($"Error: {ex.Message}");
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
        return string.Empty;
    }
}
