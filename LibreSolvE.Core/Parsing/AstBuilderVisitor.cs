// LibreSolvE.Core/Parsing/AstBuilderVisitor.cs
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using LibreSolvE.Core.Ast;
using System;
using System.Linq;

namespace LibreSolvE.Core.Parsing;

public class AstBuilderVisitor : EesParserBaseVisitor<AstNode>
{
    public override AstNode VisitEesFile([NotNull] EesParser.EesFileContext context)
    {
        var fileNode = new EesFileNode();
        foreach (var statementContext in context.statement())
        {
            var statementNode = Visit(statementContext);
            if (statementNode is StatementNode stmt)
            {
                fileNode.Statements.Add(stmt);
            }
        }
        return fileNode;
    }

    public override AstNode VisitEquationStatement([NotNull] EesParser.EquationStatementContext context)
    {
        // Get the equation context and its expressions
        var eqContext = context.equation();
        ExpressionNode lhs = (ExpressionNode)Visit(eqContext.expression(0));
        ExpressionNode rhs = (ExpressionNode)Visit(eqContext.expression(1));
        return new EquationNode(lhs, rhs);
    }

    public override AstNode VisitAssignmentStatement([NotNull] EesParser.AssignmentStatementContext context)
    {
        // Get the assignment context, ID, and expression
        var assignContext = context.assignment();
        string varName = assignContext.ID().GetText();
        VariableNode variableNode = new VariableNode(varName);
        ExpressionNode rhs = (ExpressionNode)Visit(assignContext.expression());
        return new AssignmentNode(variableNode, rhs);
    }

    public override AstNode VisitMulDivExpr([NotNull] EesParser.MulDivExprContext context)
    {
        ExpressionNode left = (ExpressionNode)Visit(context.expression(0));
        ExpressionNode right = (ExpressionNode)Visit(context.expression(1));

        // Determine the operator type
        var op = context.GetChild(1);
        BinaryOperator binaryOp = op.GetText() == "*"
            ? BinaryOperator.Multiply
            : BinaryOperator.Divide;

        return new BinaryOperationNode(left, binaryOp, right);
    }

    public override AstNode VisitAddSubExpr([NotNull] EesParser.AddSubExprContext context)
    {
        ExpressionNode left = (ExpressionNode)Visit(context.expression(0));
        ExpressionNode right = (ExpressionNode)Visit(context.expression(1));

        // Determine the operator type
        var op = context.GetChild(1);
        BinaryOperator binaryOp = op.GetText() == "+"
            ? BinaryOperator.Add
            : BinaryOperator.Subtract;

        return new BinaryOperationNode(left, binaryOp, right);
    }

    public override AstNode VisitParenExpr([NotNull] EesParser.ParenExprContext context)
    {
        return Visit(context.expression());
    }

    public override AstNode VisitNumberAtom([NotNull] EesParser.NumberAtomContext context)
    {
        if (double.TryParse(context.NUMBER().GetText(), out double value))
        {
            return new NumberNode(value);
        }
        throw new ArgumentException($"Could not parse number: {context.NUMBER().GetText()}");
    }

    public override AstNode VisitVariableAtom([NotNull] EesParser.VariableAtomContext context)
    {
        return new VariableNode(context.ID().GetText());
    }

    protected override AstNode AggregateResult(AstNode aggregate, AstNode nextResult)
    {
        return nextResult ?? aggregate;
    }
}
