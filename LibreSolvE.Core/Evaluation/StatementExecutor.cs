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

    public StatementExecutor(VariableStore variableStore)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore);
    }

    public void Execute(EesFileNode fileNode)
    {
        Console.WriteLine("--- Executing Statements ---");
        foreach (var statement in fileNode.Statements)
        {
            ExecuteStatement(statement);
        }
        Console.WriteLine("--- Statement Execution Finished ---");
        // Store equations for later processing
        Console.WriteLine($"--- Found {Equations.Count} equations for solver ---");

    }

    private void ExecuteStatement(StatementNode statement)
    {
        switch (statement)
        {
            case AssignmentNode assignNode:
                ExecuteAssignment(assignNode);
                break;
            case EquationNode eqNode:
                // For now, just collect equations. We'll solve them later.
                Console.WriteLine($"Debug: Found equation: {eqNode}");
                Equations.Add(eqNode);
                break;
            // Add other statement types later (FUNCTION, MODULE, etc.)
            default:
                throw new NotImplementedException($"Execution not implemented for statement type: {statement.GetType().Name}");
        }
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
            // Decide whether to stop execution or continue
            // For now, let's rethrow to stop
            throw;
        }
    }
}
