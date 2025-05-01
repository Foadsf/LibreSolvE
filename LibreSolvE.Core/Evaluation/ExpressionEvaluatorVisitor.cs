// LibreSolvE.Core/Evaluation/ExpressionEvaluatorVisitor.cs
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Parsing; // Add this import for ANTLR-generated classes
using System;
using System.Globalization; // For parsing numbers consistently

namespace LibreSolvE.Core.Evaluation;

// The ExpressionEvaluatorVisitor is NOT a parser visitor!
// It's an AST visitor to evaluate expressions to double values
// We should NOT inherit from EesParserBaseVisitor<double>
public class ExpressionEvaluatorVisitor
{
    private readonly VariableStore _variableStore;

    public ExpressionEvaluatorVisitor(VariableStore variableStore)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
    }

    // --- Expressions ---
    // We visit the AST nodes directly, not ANTLR parse tree nodes
    public double Evaluate(ExpressionNode node)
    {
        switch (node)
        {
            case NumberNode num:
                return num.Value;
            case VariableNode var:
                // Look up the variable's value
                return _variableStore.GetVariable(var.Name);
            case BinaryOperationNode binOp:
                return EvaluateBinaryOperation(binOp);
            // case FunctionCallNode funcCall: // TODO: Add later
            //     return EvaluateFunctionCall(funcCall);
            default:
                throw new NotImplementedException($"Evaluation not implemented for AST node type: {node.GetType().Name}");
        }
    }

    private double EvaluateBinaryOperation(BinaryOperationNode binOp)
    {
        double leftVal = Evaluate(binOp.Left);
        double rightVal = Evaluate(binOp.Right);

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

    // TODO: Implement EvaluateFunctionCall later
}
