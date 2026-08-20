using Antlr4.Runtime;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Parsing; // EesLexer/EesParser are ANTLR-generated into
                                // this namespace, not a separate .Grammar one

namespace LibreSolvE.Conformance;

/// <summary>
/// Result of running one .lse source through the full LibreSolvE pipeline.
/// Deliberately mirrors ProcessFileCore's outcome shape (Program.cs) rather
/// than inventing a parallel classification, so a harness pass/fail means
/// the same thing the CLI's exit code means.
/// </summary>
public sealed record LseRunResult(
    bool ParseSucceeded,
    bool SolveSucceeded,
    string? FailureMessage,
    VariableStore? Store)
{
    /// <summary>True only if the file parsed AND the algebraic system solved
    /// -- the same condition ProcessFileCore uses to set exitCode = 0.</summary>
    public bool Succeeded => ParseSucceeded && SolveSucceeded;
}

/// <summary>
/// Runs a .lse source string through the same Core pipeline Program.cs's
/// ProcessFileCore uses (lex -> parse -> AST -> StatementExecutor -> solve),
/// minus the CLI-only concerns (markdown formatting, plot file export,
/// Serilog). Exists so the conformance tests exercise the real production
/// code path instead of a second, potentially-drifting reimplementation.
/// </summary>
public static class LseRunner
{
    public static LseRunResult Run(string sourceText)
    {
        try
        {
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(sourceText);
            var commentPlotCommands = PlotDirectiveParser.ExtractPlotCommands(sourceText);

            var inputStream = new AntlrInputStream(sourceText);
            var lexer = new EesLexer(inputStream);
            var tokenStream = new CommonTokenStream(lexer);
            var parser = new EesParser(tokenStream);
            var errorListener = new BetterErrorListener();
            parser.RemoveErrorListeners();
            lexer.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);
            lexer.AddErrorListener(errorListener);

            var parseTree = parser.eesFile();

            var astBuilder = new AstBuilderVisitor();
            AstNode rootAstNode = astBuilder.VisitEesFile(parseTree);
            if (rootAstNode is not EesFileNode fileNode)
            {
                return new LseRunResult(false, false, "AST root was not EesFileNode", null);
            }

            var variableStore = new VariableStore();
            var functionRegistry = new FunctionRegistry();
            var solverSettings = new SolverSettings { SolverType = SolverType.NelderMead };
            UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

            var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
            executor.Execute(fileNode, commentPlotCommands);
            bool algebraicSolveSuccess = executor.SolveRemainingAlgebraicEquations();
            bool solveSuccess = algebraicSolveSuccess && !executor.HasErrors;

            string? failureMessage = solveSuccess
                ? null
                : executor.HasErrors
                    ? string.Join("; ", executor.ExecutionErrors)
                    : "Solver did not converge";

            return new LseRunResult(true, solveSuccess, failureMessage, variableStore);
        }
        catch (ParsingException pEx)
        {
            // This is the outcome that matters most for the compatibility
            // contract: a construct COMPATIBILITY.md says is invalid EES
            // must land here, not fall through to a successful solve.
            return new LseRunResult(false, false, pEx.Message, null);
        }
        catch (Exception ex)
        {
            // Any other exception (evaluation error, unimplemented function,
            // etc.) is still a failure to run the file -- report it as such
            // rather than letting it crash the test process, since a
            // corpus-wide harness needs one bad file to fail its own case,
            // not the whole run.
            return new LseRunResult(true, false, $"{ex.GetType().Name}: {ex.Message}", null);
        }
    }
}
