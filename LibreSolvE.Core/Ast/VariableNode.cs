// LibreSolvE.Core/Ast/VariableNode.cs
namespace LibreSolvE.Core.Ast;

public class VariableNode : ExpressionNode
{
    public string Name { get; }

    public VariableNode(string name)
    {
        Name = name;
    }
    public override string ToString() => Name;
}
