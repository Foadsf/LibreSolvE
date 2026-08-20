using LibreSolvE.Core.Evaluation;

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
    public void BarePlot_AtTopLevel_IsRejected()
    {
        // PLOT_CMD removed from the grammar entirely: 'PLOT' now lexes as a
        // plain ID, which does not start a valid assignment or equation, so
        // this is a genuine parser-level syntax error -- matching real EES,
        // which has no PLOT statement at all (zero hits in the manual).
        var result = LseRunner.Run("x = 1\nPLOT x\n");
        Assert.False(result.Succeeded, "A bare top-level PLOT should be rejected -- COMPATIBILITY.md Rule 5.");
    }

    [Fact]
    public void CommentEmbeddedPlot_IsExtractedAndDoesNotBreakParsing()
    {
        // The LibreSolvE-only form: {$PLOT ...} is, to the ANTLR grammar, a
        // completely ordinary { } comment (EesLexer.g4 COMMENT_BRACE) -- the
        // same bytes a real EES installation would also parse straight past
        // as inert text. PlotDirectiveParser pulls the command out of the
        // raw source independently of the parse, so the file still solves.
        var result = LseRunner.Run("x = 1 {$PLOT x}\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(1.0, result.Store!.GetVariable("x"), 10);
    }

    [Fact]
    public void PlotDirectiveParser_ExtractsBraceAndQuoteForms()
    {
        var fromBrace = PlotDirectiveParser.ExtractPlotCommands("x = 1 {$PLOT t, x WITH TITLE \"Foo\"}\n");
        Assert.Single(fromBrace);
        Assert.Equal("PLOT t, x WITH TITLE \"Foo\"", fromBrace[0]);

        // The quote form uses EES's OTHER real comment delimiter (" "). Its
        // own WITH TITLE "..." argument can't also use double quotes without
        // prematurely closing the comment, so single quotes are used there --
        // a real, if awkward, constraint of picking the quote-comment form.
        var fromQuote = PlotDirectiveParser.ExtractPlotCommands("x = 1 \"$PLOT t, x WITH TITLE 'Foo'\"\n");
        Assert.Single(fromQuote);
        Assert.Equal("PLOT t, x WITH TITLE 'Foo'", fromQuote[0]);
    }

    // --- COMPATIBILITY.md SS4: real inline units, `T=50 [C]`, unquoted ---

    [Fact]
    public void BareUnitAnnotation_IsAccepted_AndDoesNotAlterTheValue()
    {
        // The 2025 LibreSolvE design used a quoted-comment form, "[C]", which
        // parses (comments may be any quoted string) but is not how EES
        // itself carries units. Real EES uses this unquoted form, confirmed
        // on two independent modern sources predating the only manual this
        // project has (COMPATIBILITY.md SS4). Before this fix it was a
        // genuine parse error: bare '[' ']' lexed as visible LBRACK/RBRACK
        // tokens the parser never consumed.
        var result = LseRunner.Run("T = 50 [C]\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        // The unit must be display-only, never folded into the arithmetic --
        // COMPATIBILITY.md's explicit commitment ("do not carry units into
        // the numeric solve"). 50, not some converted value.
        Assert.Equal(50.0, result.Store!.GetVariable("T"), 10);
    }

    [Fact]
    public void BareUnitAnnotation_IsRecordedForDisplay()
    {
        // UnitParser.ExtractUnitsFromSource already regex-scanned raw source
        // text for '[...]' before this fix -- it never distinguished quoted
        // from bare brackets. The only thing that changed here is the
        // LEXER now also accepts the bare form instead of choking on it, so
        // this asserts the two halves (extraction, parsing) actually agree.
        var result = LseRunner.Run("T = 50 [C]\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.True(result.Store!.HasUnit("T"));
        Assert.Equal("C", result.Store!.GetUnit("T"));
    }

    [Fact]
    public void BareUnitAnnotation_DoesNotBreakAFollowingEquation()
    {
        // A unit annotation must be transparent to everything after it on
        // the line/file, not just parse in isolation -- the same standard
        // BraceComment_IsAccepted holds real comments to.
        var result = LseRunner.Run("T = 50 [C]\nx = T + 1\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(51.0, result.Store!.GetVariable("x"), 10);
    }

    [Fact]
    public void QuotedCommentUnit_StillWorks()
    {
        // The original design's form must keep working -- it is still
        // legal EES (any quoted string is a comment), and the entire
        // existing corpus (001/002/003/006) uses it. This is the
        // regression control for the ID-rule simplification alongside this
        // change (dropping the dead, unused trailing-LBRACK option).
        var result = LseRunner.Run("T = 50 \"[C]\"\n");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(50.0, result.Store!.GetVariable("T"), 10);
        Assert.Equal("C", result.Store!.GetUnit("T"));
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
