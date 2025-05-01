// LibreSolvE.Core/Ast/BinaryOperationNode.cs
namespace LibreSolvE.Core.Ast;

public enum BinaryOperator { Add, Subtract, Multiply, Divide, Power }

public class BinaryOperationNode : ExpressionNode
{
    public ExpressionNode Left { get; }
    public BinaryOperator Operator { get; }
    public ExpressionNode Right { get; }

    public BinaryOperationNode(ExpressionNode left, BinaryOperator op, ExpressionNode right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
    public override string ToString() => $"({Left} {Operator} {Right})";
}
