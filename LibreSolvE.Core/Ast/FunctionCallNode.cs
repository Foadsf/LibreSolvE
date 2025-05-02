// LibreSolvE.Core/Ast/FunctionCallNode.cs
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Ast;

/// <summary>
/// Represents a built-in or user-defined function call in the AST
/// </summary>
public class FunctionCallNode : ExpressionNode
{
    /// <summary>
    /// The name of the function being called
    /// </summary>
    public string FunctionName { get; }

    /// <summary>
    /// The list of argument expressions passed to the function
    /// </summary>
    public List<ExpressionNode> Arguments { get; }

    public FunctionCallNode(string functionName, List<ExpressionNode> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments ?? new List<ExpressionNode>();
    }

    public override string ToString()
    {
        string args = string.Join(", ", Arguments.Select(arg => arg.ToString()));
        return $"{FunctionName}({args})";
    }
}
