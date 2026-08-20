using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Extracts PLOT commands embedded inside real EES comments.
///
/// COMPATIBILITY.md Rule 5: PLOT is not a real EES statement (zero hits in
/// the manual; EES plotting is GUI-driven), so it must live inside an
/// actual comment -- { } or " " -- to keep the containing file valid EES. A
/// real EES installation parses straight past a {$PLOT ...} comment as
/// inert text; only LibreSolvE needs to see inside it.
///
/// This mirrors UnitParser.ExtractUnitsFromSource exactly: a line-by-line
/// regex pre-scan of the raw source, run independently of and before the
/// ANTLR parse. That independence is required, not a style choice -- the
/// lexer deliberately puts comment content on a hidden channel
/// (EesLexer.g4: COMMENT_BRACE/COMMENT_QUOTE -> channel(HIDDEN)) that the
/// parser never inspects, so there is no grammar rule this could hook into.
///
/// Single-line only, matching EesLexer.g4's own COMMENT_BRACE limitation
/// (non-nested, "let's start with non-nested for simplicity" -- a {$PLOT ...}
/// spanning multiple lines is not supported by the underlying comment lexer
/// either, so this does not need to handle a case the grammar itself can't).
/// </summary>
public static class PlotDirectiveParser
{
    private static readonly Regex BraceDirective =
        new(@"\{\s*\$PLOT\b(?<args>.*?)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QuoteDirective =
        new(@"""\s*\$PLOT\b(?<args>.*?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns each extracted command reassembled with a leading "PLOT" so it
    /// matches the format PlottingService.CreatePlot already expects (it was
    /// built to parse strings starting with the literal PLOT_CMD text, which
    /// always began with that keyword).
    /// </summary>
    public static List<string> ExtractPlotCommands(string sourceText)
    {
        var commands = new List<string>();
        var lines = sourceText.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        foreach (var line in lines)
        {
            foreach (Match m in BraceDirective.Matches(line))
            {
                commands.Add("PLOT" + m.Groups["args"].Value);
            }
            foreach (Match m in QuoteDirective.Matches(line))
            {
                commands.Add("PLOT" + m.Groups["args"].Value);
            }
        }
        return commands;
    }
}
