// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using System;
using System.Collections.Generic;

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    private readonly VariableStore _variableStore;
    private readonly ExpressionEvaluatorVisitor _expressionEvaluator;
    // We will store equations separately for the solver later
    public List<EquationNode> Equations { get; } = new List<EquationNode>();

    // Track variables that need solving
    public HashSet<string> VariablesToSolve { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public StatementExecutor(VariableStore variableStore)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore);
    }

    public void Execute(EesFileNode fileNode)
    {
        Console.WriteLine("--- Executing Statements ---");
        // First pass: Process all assignments to set variable values
        foreach (var statement in fileNode.Statements)
        {
            if (statement is AssignmentNode assignNode)
            {
                ExecuteAssignment(assignNode);
            }
            else if (statement is EquationNode eqNode)
            {
                // Collect equations for later processing
                Console.WriteLine($"Debug: Found equation: {eqNode}");
                Equations.Add(eqNode);
            }
        }
        Console.WriteLine("--- Statement Execution Finished ---");
        // Store equations for later processing
        Console.WriteLine($"--- Found {Equations.Count} equations for solver ---");
    }

    private void ExecuteAssignment(AssignmentNode assignNode)
    {
        try
        {
            double value = _expressionEvaluator.Evaluate(assignNode.RightHandSide);
            Console.WriteLine($"Debug: Assigning {assignNode.Variable.Name} = {value}");
            _variableStore.SetVariable(assignNode.Variable.Name, value);
        }
        catch (Exception ex)
        {
            // Improve error reporting later (e.g., add line numbers)
            Console.WriteLine($"Error evaluating assignment for '{assignNode.Variable.Name}': {ex.Message}");
            // Let's continue with execution rather than throwing
            Console.WriteLine("Continuing execution...");
        }
    }

    // Placeholder for equation solving functionality
    public bool SolveEquations()
    {
        // Analyze the equations and variables
        AnalyzeSystem();

        if (Equations.Count == 0)
        {
            Console.WriteLine("No equations to solve.");
            return true;
        }

        Console.WriteLine($"--- Solving {Equations.Count} equations ---");
        Console.WriteLine("(Placeholder: Equation solving not yet implemented)");

        // TODO: Implement actual equation solving
        // For now, just return success
        return true;
    }

    // Analyze the equation system
    private void AnalyzeSystem()
    {
        // Find all variables that appear in the equations
        var varsInEquations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Identify known and unknown variables in each equation
        Console.WriteLine("\nEquation Analysis:");

        if (Equations.Count == 0)
        {
            Console.WriteLine("  No equations found to analyze.");
            return;
        }

        foreach (var eq in Equations)
        {
            Console.WriteLine($"\n  Equation: {eq}");

            // Count implicitly created variables in this equation
            _expressionEvaluator.ResetUndefinedVariableCount();
            try
            {
                // Evaluate LHS
                _expressionEvaluator.Evaluate(eq.LeftHandSide);
                // Evaluate RHS
                _expressionEvaluator.Evaluate(eq.RightHandSide);

                int unknownVarCount = _expressionEvaluator.GetUndefinedVariableCount();

                if (unknownVarCount == 0)
                {
                    Console.WriteLine("    Status: All variables have values - can check if satisfied");
                }
                else if (unknownVarCount == 1)
                {
                    Console.WriteLine("    Status: One unknown variable - can solve directly");
                }
                else
                {
                    Console.WriteLine($"    Status: {unknownVarCount} unknown variables - need solver");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Error analyzing equation: {ex.Message}");
            }
        }

        // Print the variables that need solving (implicitly created)
        var implicitVars = _variableStore.GetImplicitVariableNames();
        Console.WriteLine("\nVariables to solve for:");
        foreach (var varName in implicitVars)
        {
            Console.WriteLine($"  {varName}");
        }

        // Check if we have enough equations
        Console.WriteLine($"\nSystem has {Equations.Count} equations and {implicitVars.Count()} unknown variables");
        if (Equations.Count < implicitVars.Count())
        {
            Console.WriteLine("  WARNING: Underconstrained system - may have multiple solutions");
        }
        else if (Equations.Count > implicitVars.Count())
        {
            Console.WriteLine("  WARNING: Overconstrained system - may have no solution");
        }
        else
        {
            Console.WriteLine("  System has the same number of equations as unknowns");
        }
    }
}
