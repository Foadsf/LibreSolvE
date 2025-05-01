// LibreSolvE.Core/Ast/EquationNode.cs
namespace LibreSolvE.Core.Ast;

public class EquationNode : StatementNode
{
    public ExpressionNode LeftHandSide { get; }
    public ExpressionNode RightHandSide { get; }

    public EquationNode(ExpressionNode lhs, ExpressionNode rhs)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
    }
    public override string ToString() => $"{LeftHandSide} = {RightHandSide}";
}
