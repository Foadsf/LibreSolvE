namespace LibreSolvE.Conformance;

/// <summary>
/// One test per COMPATIBILITY.md rule, each isolating the exact construct
/// rather than depending on which corpus file happens to exercise it. Every
/// "must reject" test is paired with a "must accept" control on the
/// legitimate form of the same thing -- a rejection test alone cannot tell
/// a real enforcement from a parser that rejects everything.
/// </summary>
public class GrammarRuleTests
{
    // --- COMPATIBILITY.md SS2: comments are only { } and " ", not // ---

    [Fact]
    public void SlashSlash_IsRejected()
    {
        var result = LseRunner.Run("x = 1 // not a real EES comment\n");
        Assert.False(result.Succeeded, "'//' should not lex as a comment -- COMPATIBILITY.md Rule 2.");
    }

    [Fact]
    public void BraceComment_IsAccepted()
    {
        var result = LseRunner.Run("x = 1 { this is a real EES comment }\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(1.0, result.Store!.GetVariable("x"), 10);
    }

    [Fact]
    public void QuoteComment_IsAccepted()
    {
        var result = LseRunner.Run("x = 1 \"this is also a real EES comment\"\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(1.0, result.Store!.GetVariable("x"), 10);
    }

    // --- COMPATIBILITY.md SS3: ':=' is invalid outside FUNCTION/PROCEDURE,
    //     and (grammar audit, PLAN.md Phase 1) those don't exist yet, so
    //     ':=' is currently invalid everywhere, not merely "wrong scope". ---

    [Fact]
    public void ColonEquals_AtTopLevel_IsRejected()
    {
        var result = LseRunner.Run("x := 1\n");
        Assert.False(result.Succeeded,
            "':=' must be rejected at the top level -- COMPATIBILITY.md Rule 3. " +
            "(FUNCTION/PROCEDURE bodies, where ':=' IS legal EES, are not yet implemented " +
            "in the grammar at all, so there is currently no context where ':=' should parse.)");
    }

    [Fact]
    public void Equals_AtTopLevel_IsAccepted()
    {
        var result = LseRunner.Run("x = 1\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(1.0, result.Store!.GetVariable("x"), 10);
    }

    // --- COMPATIBILITY.md SS5: PLOT is not an EES statement ---

    [Fact]
    public void Plot_IsCurrentlyAcceptedByTheGrammar()
    {
        // This is a KNOWN, DOCUMENTED violation (COMPATIBILITY.md Rule 5),
        // not a passing compatibility test -- PLOT_CMD is still a real
        // grammar rule (EesParser.g4: PlotStatement). Asserting the CURRENT
        // (wrong) behaviour here, rather than leaving it unstated, means
        // this test starts failing the moment someone fixes Rule 5 -- which
        // is the correct prompt to come back and flip this assertion and
        // its name, not a silent break.
        var result = LseRunner.Run("x = 1\nPLOT x\n");
        Assert.True(result.Succeeded,
            "If this now fails, COMPATIBILITY.md Rule 5 (PLOT is not an EES statement) has been " +
            "fixed -- update this test to assert rejection instead, and remove this comment.");
    }

    // --- CONVERT function (fixed in commit 3094213) ---

    [Fact]
    public void Convert_ProducesCorrectValue()
    {
        // Regression test for the bug found while building this harness:
        // CONVERT had no dispatch branch in EvaluateFunctionCall (fell
        // through to a stub that always threw), and once that was fixed,
        // ConvertUnits() itself failed because UnitsNet.QuantityValue isn't
        // IConvertible. Both are fixed; this pins the fix numerically.
        var result = LseRunner.Run("x = 10 * CONVERT('ft', 'm')\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(3.048, result.Store!.GetVariable("x"), 6);
    }

    [Fact]
    public void Convert_IncompatibleUnits_FailsTheWholeFile()
    {
        // Regression test for the defect this test originally found (see git
        // history for the full story): a statement whose evaluation threw
        // used to be silently dropped by StatementExecutor's per-statement
        // catch blocks, and because that then left zero remaining algebraic
        // unknowns, the file still reported overall success -- the variable
        // simply vanished with no error surfacing. Fixed via
        // StatementExecutor.HasErrors, which ExecuteExplicitAssignments and
        // ExecutePotentialAssignments now populate instead of only logging.
        var result = LseRunner.Run("x = 10 * CONVERT('ft', 'kg')\n");
        Assert.False(result.Succeeded, "Converting length to mass should fail the whole file, not silently drop the assignment.");
        Assert.NotNull(result.FailureMessage);
        Assert.Contains("not compatible", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }
}
