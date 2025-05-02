// LibreSolvE.Core/Evaluation/UnitParser.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using LibreSolvE.Core.Parsing;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Utility class to extract units from comments in variable assignments and equations
/// </summary>
public class UnitParser
{
    // Regular expression to match units in square brackets
    private static readonly Regex UnitRegex = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);

    // Regular expression to match units in comments
    private static readonly Regex CommentUnitRegex = new Regex(@"//.*\[([^\]]+)\]|\{.*\[([^\]]+)\]|\"".*\[([^\]]+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Extract units from a token stream
    /// </summary>
    /// <param name="tokens">The token stream to parse</param>
    /// <returns>Dictionary mapping variable names to their units</returns>
    public static Dictionary<string, string> ExtractUnitsFromTokenStream(CommonTokenStream tokens)
    {
        var variableUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Reset token stream to beginning
        tokens.Reset();

        var currentVariable = string.Empty;
        var foundAssignment = false;

        // Process tokens in sequence
        while (tokens.LA(1) != TokenConstants.EOF)
        {
            var token = tokens.LT(1);
            tokens.Consume();

            // Look for variable identifiers
            if (token.Type == EesLexer.ID)
            {
                currentVariable = token.Text;
                foundAssignment = false;
            }
            // Look for assignment or equation operators
            else if ((token.Type == EesLexer.EQ || token.Type == EesLexer.ASSIGN) && !string.IsNullOrEmpty(currentVariable))
            {
                foundAssignment = true;
            }
            // Look for units in square brackets
            else if (token.Type == EesLexer.LBRACK && foundAssignment)
            {
                // Collect text until we find a closing bracket
                var unitText = new List<string>();
                while (tokens.LA(1) != TokenConstants.EOF && tokens.LA(1) != EesLexer.RBRACK)
                {
                    var unitToken = tokens.LT(1);
                    tokens.Consume();
                    unitText.Add(unitToken.Text);
                }

                // Skip the closing bracket
                if (tokens.LA(1) == EesLexer.RBRACK)
                {
                    tokens.Consume();
                }

                // Store the unit for the current variable
                variableUnits[currentVariable] = string.Join("", unitText);
            }
            // Look for units in comments
            else if ((token.Type == EesLexer.COMMENT_SLASH ||
                     token.Type == EesLexer.COMMENT_BRACE ||
                     token.Type == EesLexer.COMMENT_QUOTE) &&
                    foundAssignment)
            {
                var comment = token.Text;
                var match = UnitRegex.Match(comment);

                if (match.Success)
                {
                    variableUnits[currentVariable] = match.Groups[1].Value;
                }
            }

            // Reset if we encounter a semicolon or line break
            if (token.Type == EesLexer.SEMI || token.Text.Contains("\n"))
            {
                currentVariable = string.Empty;
                foundAssignment = false;
            }
        }

        return variableUnits;
    }

    /// <summary>
    /// Extract units from the source text directly
    /// </summary>
    /// <param name="sourceText">The source code text to parse</param>
    /// <returns>Dictionary mapping variable names to their units</returns>
    public static Dictionary<string, string> ExtractUnitsFromSourceText(string sourceText)
    {
        var variableUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Create a lexer to tokenize the source
        var inputStream = new AntlrInputStream(sourceText);
        var lexer = new EesLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);

        return ExtractUnitsFromTokenStream(tokenStream);
    }

    /// <summary>
    /// Apply extracted units to variables in the VariableStore
    /// </summary>
    /// <param name="variableStore">The variable store to update</param>
    /// <param name="units">Dictionary mapping variable names to their units</param>
    public static void ApplyUnitsToVariableStore(VariableStore variableStore, Dictionary<string, string> units)
    {
        foreach (var kvp in units)
        {
            variableStore.SetUnit(kvp.Key, kvp.Value);
        }
    }
}
