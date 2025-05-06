// LibreSolvE.Core/Parsing/BetterErrorListener.cs
using Antlr4.Runtime;
using System;
using System.IO;
using System.Text;
using LibreSolvE.Core.Parsing;

namespace LibreSolvE.Core.Parsing;

/// <summary>
/// Provides better error messages for ANTLR parsing errors
/// </summary>
public class BetterErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    // Implementation for parser errors
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        // Get the input text around the error
        string input = ((Parser)recognizer).InputStream.ToString() ?? "";
        string[] lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string errorLine = line <= lines.Length ? lines[line - 1] : "";

        // Format a helpful error message
        StringBuilder errorMessage = new StringBuilder();
        errorMessage.AppendLine($"Syntax error at line {line}:{charPositionInLine} near '{offendingSymbol?.Text ?? "<EOF>"}': {msg}");
        errorMessage.AppendLine($"  {errorLine}");
        errorMessage.Append(' ', charPositionInLine + 2);
        errorMessage.AppendLine("^");

        if (offendingSymbol != null)
        {
            errorMessage.AppendLine($"Offending token: '{offendingSymbol.Text}'");
        }

        throw new ParsingException(errorMessage.ToString(), e);
    }

    // Implementation for lexer errors
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        // Get the input text around the error
        string input = recognizer.InputStream.ToString() ?? "";
        string[] lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string errorLine = line <= lines.Length ? lines[line - 1] : "";

        // Format a helpful error message
        StringBuilder errorMessage = new StringBuilder();
        errorMessage.AppendLine($"Lexer error at line {line}:{charPositionInLine}: {msg}");
        errorMessage.AppendLine($"  {errorLine}");
        errorMessage.Append(' ', charPositionInLine + 2);
        errorMessage.AppendLine("^");

        throw new ParsingException(errorMessage.ToString(), e);
    }
}
