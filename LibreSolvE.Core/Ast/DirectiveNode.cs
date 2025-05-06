// LibreSolvE.Core/Ast/DirectiveNode.cs
using System;

namespace LibreSolvE.Core.Ast;

/// <summary>
/// Represents a directive statement in the AST such as $IntegralTable or $IntegralAutoStep
/// </summary>
public class DirectiveNode : StatementNode
{
    public string DirectiveText { get; }

    public DirectiveNode(string directiveText)
    {
        DirectiveText = directiveText ?? throw new ArgumentNullException(nameof(directiveText));
    }

    public override string ToString() => DirectiveText;
}
