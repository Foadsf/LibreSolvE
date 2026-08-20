using LibreSolvE.Core.Ast;

namespace LibreSolvE.Conformance;

/// <summary>
/// AstNode.Line was added specifically so a future diagnostic pass (Phase 4's
/// dimensional-homogeneity check) can report which line an offending
/// equation came from. A claim like that needs a test that checks the exact
/// line number on the exact node, not merely "it's non-zero" -- these parse
/// a real multi-line source and walk to specific nodes.
/// </summary>
public class LineTrackingTests
{
    [Fact]
    public void EquationNode_ReportsItsOwnLine_NotLine1ForEveryStatement()
    {
        // The adversarial case this guards against: a bug that stamps every
        // node with the FIRST token's line (e.g. accidentally reusing one
        // captured context) would still make "Line > 0" tests pass while
        // being useless for diagnostics. Three statements, three distinct
        // expected lines, checked individually.
        var file = LseRunner.ParseToAst("L = 1\n\ng = 9.81\n\nh = L * g\n");
        Assert.Equal(3, file.Statements.Count);

        var eq1 = Assert.IsType<EquationNode>(file.Statements[0]);
        Assert.Equal(1, eq1.Line);

        var eq2 = Assert.IsType<EquationNode>(file.Statements[1]);
        Assert.Equal(3, eq2.Line);

        var eq3 = Assert.IsType<EquationNode>(file.Statements[2]);
        Assert.Equal(5, eq3.Line);
    }

    [Fact]
    public void NestedExpressionNodes_EachReportTheirOwnLine()
    {
        // A dimension mismatch is usually inside a sub-expression, not the
        // whole equation -- so the BinaryOperationNode and its VariableNode
        // operands need correct lines too, not just the top-level equation.
        var file = LseRunner.ParseToAst("x = 1\ny = 2\nz = x + y\n");
        var eq = Assert.IsType<EquationNode>(file.Statements[2]);
        Assert.Equal(3, eq.Line);

        var rhs = Assert.IsType<BinaryOperationNode>(eq.RightHandSide);
        Assert.Equal(3, rhs.Line);

        var left = Assert.IsType<VariableNode>(rhs.Left);
        Assert.Equal("x", left.Name);
        Assert.Equal(3, left.Line);
    }

    [Fact]
    public void FunctionCallNode_ReportsItsLine()
    {
        var file = LseRunner.ParseToAst("\n\nx = SIN(1)\n");
        var eq = Assert.IsType<EquationNode>(file.Statements[0]);
        Assert.Equal(3, eq.Line);

        var call = Assert.IsType<FunctionCallNode>(eq.RightHandSide);
        Assert.Equal("SIN", call.FunctionName);
        Assert.Equal(3, call.Line);
    }

    [Fact]
    public void BlankAndCommentLines_DoNotShiftLineNumbers()
    {
        // Comments and blank lines are on the HIDDEN channel but still
        // consume real lines -- ANTLR's own Line counting handles this
        // (it counts every newline in the character stream, not just
        // visible-channel tokens), but this is the test that would catch
        // a regression if that assumption ever broke.
        var file = LseRunner.ParseToAst("{ comment on line 1 }\n\" comment on line 2 \"\nx = 1\n");
        var eq = Assert.IsType<EquationNode>(file.Statements[0]);
        Assert.Equal(3, eq.Line);
    }
}
