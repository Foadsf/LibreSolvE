// LibreSolvE.Core/Ast/EesFileNode.cs
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Ast;

public class EesFileNode : AstNode
{
    public List<StatementNode> Statements { get; } = new List<StatementNode>();

    public override string ToString() => string.Join("\n", Statements.Select(s => s.ToString()));
}
