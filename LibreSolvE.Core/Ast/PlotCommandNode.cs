using System;

namespace LibreSolvE.Core.Ast;

/// <summary>
/// Represents a plot command in the AST
/// </summary>
public class PlotCommandNode : StatementNode
{
    public string CommandText { get; }

    public PlotCommandNode(string commandText)
    {
        CommandText = commandText ?? throw new ArgumentNullException(nameof(commandText));
    }

    public override string ToString() => CommandText;
}
