// LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Parsing; // Assuming grammar files are here if needed, maybe not needed directly now
using System;
using System.Globalization; // Keep for number parsing
using UnitsNet; // Use the base namespace
using UnitsNet.Units; // Use the Units namespace for enums

namespace LibreSolvE.Core.Evaluation;

public class ExpressionEvaluatorVisitor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly bool _warningsAsErrors = false;
    private int _undefinedVariableCount = 0;

    // This reference allows the evaluator to potentially call back to the executor
    // for more complex scenarios in the future (like nested function calls needing state),
    // but it's not strictly used in the current simple evaluation logic *within this class*.
    // It *is* used when this visitor is created *by* the OdeSolver.
    private readonly StatementExecutor? _statementExecutor;

    // Constructor used by StatementExecutor for general evaluation
    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _warningsAsErrors = warningsAsErrors;
        _statementExecutor = null; // Not needed for direct statement execution evaluation
    }

    // Constructor used internally by OdeSolver (or potentially other future components)
    // that might need access back to the main execution flow
    public ExpressionEvaluatorVisitor(VariableStore variableStore, FunctionRegistry functionRegistry, StatementExecutor statementExecutor, bool warningsAsErrors = false)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _statementExecutor = statementExecutor; // Store the executor reference if provided
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
            // StringLiteralNode is not directly evaluated to double, handle in specific contexts (like CONVERT args)
            case StringLiteralNode: throw new InvalidOperationException("Cannot directly evaluate a string literal node to a numerical value.");
            default: throw new NotImplementedException($"Evaluation not implemented for AST node type: {node?.GetType().Name}");
        }
    }

    private double EvaluateVariable(VariableNode var)
    {
        bool wasExplicitlySet = _variableStore.IsExplicitlySet(var.Name);
        // Use GetVariable which handles default/guess values internally
        double value = _variableStore.GetVariable(var.Name);

        // Check if the variable was *not* explicitly set before this access
        if (!_variableStore.IsExplicitlySet(var.Name) && !wasExplicitlySet)
        {
            // Only count as undefined if it wasn't set before AND wasn't set by GetVariable just now
            // (GetVariable sets it if it uses a default/guess)
            // A better check might be needed, but this is a start
            if (!_variableStore.IsExplicitlySet(var.Name))
            {
                _undefinedVariableCount++;
                if (_warningsAsErrors) throw new InvalidOperationException($"Variable '{var.Name}' accessed before assignment during critical evaluation step.");
            }
        }
        return value;
    }

    private double EvaluateBinaryOperation(BinaryOperationNode binOp)
    {
        double leftVal = Evaluate(binOp.Left);
        double rightVal = Evaluate(binOp.Right);

        // Optional: Unit checking for Add/Subtract can go here if implemented
        // CheckUnitCompatibility(binOp.Left, binOp.Right);

        return binOp.Operator switch
        {
            BinaryOperator.Add => leftVal + rightVal,
            BinaryOperator.Subtract => leftVal - rightVal,
            BinaryOperator.Multiply => leftVal * rightVal,
            BinaryOperator.Divide => rightVal == 0
                                   ? throw new DivideByZeroException($"Division by zero encountered evaluating: {binOp}")
                                   : leftVal / rightVal,
            BinaryOperator.Power => Math.Pow(leftVal, rightVal),
            _ => throw new NotImplementedException($"Operator not implemented: {binOp.Operator}"),
        };
    }

    private double EvaluateFunctionCall(FunctionCallNode funcCall)
    {
        // --- Special Handling for functions requiring string literals ---
        if (string.Equals(funcCall.FunctionName, "CONVERT", StringComparison.OrdinalIgnoreCase))
        {
            return HandleConvertFunction(funcCall);
        }
        else if (string.Equals(funcCall.FunctionName, "CONVERTTEMP", StringComparison.OrdinalIgnoreCase))
        {
            return HandleConvertTempFunction(funcCall);
        }
        // --- INTEGRAL is NOT handled here - it's handled by StatementExecutor ---
        // The StatementExecutor identifies y = INTEGRAL(...) and starts the OdeSolver.
        // If INTEGRAL appears elsewhere (which shouldn't happen in valid EES), it would error here.
        else if (string.Equals(funcCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The INTEGRAL function should only appear on the right-hand side of an equation/assignment handled by the Statement Executor.");
        }
        // --- Handle Regular Mathematical/Other Functions ---
        else
        {
            // Evaluate all arguments numerically first
            double[] argValues = new double[funcCall.Arguments.Count];
            for (int i = 0; i < funcCall.Arguments.Count; i++)
            {
                // Catch errors during argument evaluation
                try
                {
                    argValues[i] = Evaluate(funcCall.Arguments[i]);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error evaluating argument {i + 1} for function '{funcCall.FunctionName}': {ex.Message}", ex);
                }
            }

            // Call the function from the registry (EvaluateFunction has its own try-catch)
            return _functionRegistry.EvaluateFunction(funcCall.FunctionName, argValues);
        }
    }

    // --- Helper for CONVERT ---
    private double HandleConvertFunction(FunctionCallNode funcCall)
    {
        if (funcCall.Arguments.Count != 2) throw new ArgumentException($"Function '{funcCall.FunctionName}' requires exactly 2 string arguments (FromUnit, ToUnit).");

        if (funcCall.Arguments[0] is not StringLiteralNode fromArg)
            throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'm').");
        if (funcCall.Arguments[1] is not StringLiteralNode toArg)
            throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'ft').");

        string fromUnitStr = fromArg.Value;
        string toUnitStr = toArg.Value;

        Console.WriteLine($"Debug: CONVERT Attempt: From='{fromUnitStr}', To='{toUnitStr}'");
        try
        {
            (Enum fromUnitEnum, QuantityInfo fromInfo) = UnitParser.ParseUnitString(fromUnitStr);
            (Enum toUnitEnum, QuantityInfo toInfo) = UnitParser.ParseUnitString(toUnitStr);

            if (fromInfo.Name != toInfo.Name)
            {
                throw new ArgumentException($"Units are not compatible for conversion: '{fromUnitStr}' ({fromInfo.Name}) and '{toUnitStr}' ({toInfo.Name}).");
            }

            IQuantity fromQuantity = Quantity.From(1.0, fromUnitEnum); // Get factor for 1 unit
            IQuantity toQuantity = fromQuantity.ToUnit(toUnitEnum);
            return Convert.ToDouble(toQuantity.Value); // Return the conversion factor
        }
        catch (UnitsNet.UnitNotFoundException ex) { throw new ArgumentException($"Unit error in CONVERT function: {ex.Message}", ex); }
        catch (ArgumentException ex) { throw new ArgumentException($"Argument error in CONVERT function: {ex.Message}", ex); } // Catch specific ArgumentExceptions too
        catch (Exception ex) { throw new InvalidOperationException($"Error during CONVERT from '{fromUnitStr}' to '{toUnitStr}'. Details: {ex.Message}", ex); }
    }

    // --- Helper for CONVERTTEMP ---
    private double HandleConvertTempFunction(FunctionCallNode funcCall)
    {
        if (funcCall.Arguments.Count != 3)
            throw new ArgumentException($"Function '{funcCall.FunctionName}' requires exactly 3 arguments (FromUnitString, ToUnitString, Value).");
        if (funcCall.Arguments[0] is not StringLiteralNode fromArg)
            throw new ArgumentException($"Argument 1 for '{funcCall.FunctionName}' must be a string literal (e.g., 'C').");
        if (funcCall.Arguments[1] is not StringLiteralNode toArg)
            throw new ArgumentException($"Argument 2 for '{funcCall.FunctionName}' must be a string literal (e.g., 'K').");

        string fromUnitStr = fromArg.Value;
        string toUnitStr = toArg.Value;
        double valueToConvert = Evaluate(funcCall.Arguments[2]); // Evaluate the third argument numerically

        Console.WriteLine($"Debug: CONVERTTEMP Attempt: From='{fromUnitStr}', To='{toUnitStr}', Value={valueToConvert}");

        try
        {
            TemperatureUnit fromTempUnit = Temperature.ParseUnit(fromUnitStr, CultureInfo.InvariantCulture);
            TemperatureUnit toTempUnit = Temperature.ParseUnit(toUnitStr, CultureInfo.InvariantCulture);

            Temperature temp = Temperature.From(valueToConvert, fromTempUnit);
            double convertedValue = temp.ToUnit(toTempUnit).Value;

            return convertedValue;
        }
        catch (UnitsNet.UnitNotFoundException ex) { throw new ArgumentException($"Temperature unit error in CONVERTTEMP: {ex.Message}", ex); }
        catch (ArgumentException ex) { throw new ArgumentException($"Invalid temperature unit string in CONVERTTEMP: {ex.Message}", ex); }
        catch (Exception ex) { throw new InvalidOperationException($"Error during CONVERTTEMP from '{fromUnitStr}' to '{toUnitStr}' for value {valueToConvert}. Details: {ex.Message}", ex); }
    }

    // --- Unit Checking (Placeholder) ---
    // private void CheckUnitCompatibility(ExpressionNode leftNode, ExpressionNode rightNode)
    // {
    //     // Implementation would go here, using UnitParser and VariableStore
    // }

    // private string GetNodeUnitString(ExpressionNode node)
    // {
    //     // Implementation would go here
    //     return string.Empty;
    // }
}
