// LibreSolvE.Core/Ast/AssignmentNode.cs
namespace LibreSolvE.Core.Ast;

public class AssignmentNode : StatementNode
{
    public VariableNode Variable { get; } // LHS must be a simple variable
    public ExpressionNode RightHandSide { get; }

    public AssignmentNode(VariableNode variable, ExpressionNode rhs)
    {
        Variable = variable;
        RightHandSide = rhs;
    }
    public override string ToString() => $"{Variable.Name} = {RightHandSide}"; // Or use := if preferred
}
