// LibreSolvE.Core/Ast/AstNode.cs
namespace LibreSolvE.Core.Ast;

// Base interface or abstract class for all AST nodes
public abstract class AstNode
{
    /// <summary>1-based source line this node came from, or 0 if never set
    /// (e.g. a node synthesized by the visitor rather than parsed directly,
    /// such as UnaryMinusExpr's 0-minus-operand rewrite). Populated by
    /// AstBuilderVisitor from the ANTLR parse-tree context's Start token.
    /// Exists so a later diagnostic pass (e.g. Phase 4's dimensional-
    /// homogeneity check) can report which line an offending equation
    /// or expression came from -- nothing read this before it was added.</summary>
    public int Line { get; set; }
}
