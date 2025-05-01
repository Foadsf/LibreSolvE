// LibreSolvE.Core/Parsing/AstBuilderVisitor.cs
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using LibreSolvE.Core.Ast;
using System;
using System.Globalization; // For consistent number parsing
using System.Linq;

namespace LibreSolvE.Core.Parsing;

public class AstBuilderVisitor : EesParserBaseVisitor<AstNode>
{
    public override AstNode VisitEesFile([NotNull] EesParser.EesFileContext context)
    {
        var fileNode = new EesFileNode();
        foreach (var statementContext in context.statement())
        {
            var statementNode = Visit(statementContext); // Visit the statement alternative
            if (statementNode is StatementNode stmt)
            {
                fileNode.Statements.Add(stmt);
            }
            // Log if a statement parse didn't yield a StatementNode?
            // else if (statementNode != null) { Console.Error.WriteLine($"Warning: Visiting statement resulted in non-statement node: {statementNode.GetType()}"); }

        }
        return fileNode;
    }

    // --- Statement Level Visitors ---
    // These methods correspond to the # labels on the statement rule alternatives

    public override AstNode VisitEquationStatement([NotNull] EesParser.EquationStatementContext context)
    {
        // This context directly contains the 'equation' rule node. Visit it.
        return Visit(context.equation());
    }

    public override AstNode VisitAssignmentStatement([NotNull] EesParser.AssignmentStatementContext context)
    {
        // This context directly contains the 'assignment' rule node. Visit it.
        return Visit(context.assignment());
    }

    // --- Rule Level Visitors ---
    // These methods correspond to the actual rules and access the labeled elements

    public override AstNode VisitEquation([NotNull] EesParser.EquationContext context)
    {
        // Access labeled elements directly from the EquationContext
        ExpressionNode lhs = (ExpressionNode)Visit(context.lhs);
        ExpressionNode rhs = (ExpressionNode)Visit(context.rhs);
        return new EquationNode(lhs, rhs);
    }

    public override AstNode VisitAssignment([NotNull] EesParser.AssignmentContext context)
    {
        // Access labeled elements directly from the AssignmentContext
        string varName = context.variable.Text; // Use .Text property of the Token
        VariableNode variableNode = new VariableNode(varName);
        ExpressionNode rhs = (ExpressionNode)Visit(context.rhs);
        return new AssignmentNode(variableNode, rhs);
    }

    // --- Expression Visitors (Using labels where appropriate) ---

    public override AstNode VisitMulDivExpr([NotNull] EesParser.MulDivExprContext context)
    {
        ExpressionNode left = (ExpressionNode)Visit(context.left); // Use label
        ExpressionNode right = (ExpressionNode)Visit(context.right); // Use label
        BinaryOperator op = context.op.Type == EesLexer.MUL ? BinaryOperator.Multiply : BinaryOperator.Divide; // Use label
        return new BinaryOperationNode(left, op, right);
    }

    public override AstNode VisitAddSubExpr([NotNull] EesParser.AddSubExprContext context)
    {
        ExpressionNode left = (ExpressionNode)Visit(context.left); // Use label
        ExpressionNode right = (ExpressionNode)Visit(context.right); // Use label
        BinaryOperator op = context.op.Type == EesLexer.PLUS ? BinaryOperator.Add : BinaryOperator.Subtract; // Use label
        return new BinaryOperationNode(left, op, right);
    }

    public override AstNode VisitParenExpr([NotNull] EesParser.ParenExprContext context)
    {
        return Visit(context.expression()); // Visit inner expression
    }

    // --- Atom Visitors ---

    public override AstNode VisitNumberAtom([NotNull] EesParser.NumberAtomContext context)
    {
        // Use InvariantCulture for reliable decimal parsing regardless of system locale
        if (double.TryParse(context.NUMBER().GetText(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
        {
            return new NumberNode(value);
        }
        // Consider more specific error handling or returning an ErrorNode
        throw new FormatException($"Could not parse number: {context.NUMBER().GetText()}");
    }

    public override AstNode VisitVariableAtom([NotNull] EesParser.VariableAtomContext context)
    {
        return new VariableNode(context.ID().GetText());
    }

    // --- Default Aggregation ---
    protected override AstNode AggregateResult(AstNode aggregate, AstNode nextResult)
    {
        // Default behavior is usually sufficient for visitors building specific nodes
        return nextResult ?? aggregate;
    }
}
