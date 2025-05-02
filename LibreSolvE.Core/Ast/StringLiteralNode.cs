// LibreSolvE.Core/Ast/StringLiteralNode.cs
namespace LibreSolvE.Core.Ast;

public class StringLiteralNode : ExpressionNode
{
    public string Value { get; } // The content inside the quotes

    public StringLiteralNode(string value)
    {
        // Remove surrounding quotes and unescape doubled quotes ('') -> (')
        if (value.Length >= 2 && value.StartsWith("'") && value.EndsWith("'"))
        {
            Value = value.Substring(1, value.Length - 2).Replace("''", "'");
        }
        else
        {
            Value = value; // Should not happen if lexer rule is correct
        }
    }
    public override string ToString() => $"'{Value.Replace("'", "''")}'"; // Re-escape for printing
}
