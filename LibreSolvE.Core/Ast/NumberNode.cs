// LibreSolvE.Core/Ast/NumberNode.cs
namespace LibreSolvE.Core.Ast;

public class NumberNode : ExpressionNode
{
    public double Value { get; }

    public NumberNode(double value)
    {
        Value = value;
    }
    // Consider adding ToString() for debugging
    public override string ToString() => Value.ToString();
}
