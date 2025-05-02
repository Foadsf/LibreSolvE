// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly SolverSettings _solverSettings;
    private readonly ExpressionEvaluatorVisitor _expressionEvaluator;
    public List<EquationNode> EquationsToSolve { get; } = new List<EquationNode>();

    public StatementExecutor(VariableStore variableStore)
        : this(variableStore, new FunctionRegistry())
    {
    }

    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry)
        : this(variableStore, functionRegistry, new SolverSettings())
    {
    }

    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry, SolverSettings solverSettings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _solverSettings = solverSettings ?? new SolverSettings();
        // WarningsAsErrors = false: Allow undefined vars during initial scan/assignment execution
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, false);
    }

    // Helper to check if an expression can be evaluated to a constant *now*
    private bool IsConstantValue(ExpressionNode node, out double value)
    {
        value = 0;
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            value = _expressionEvaluator.Evaluate(node);
            // It's constant if evaluation succeeded AND no undefined variables were hit
            return _expressionEvaluator.GetUndefinedVariableCount() == 0;
        }
        catch
        {
            return false; // Evaluation failed (e.g., div by zero) or feature not implemented
        }
    }

    public void Execute(EesFileNode fileNode)
    {
        Console.WriteLine("--- Pre-processing Statements ---");
        EquationsToSolve.Clear();
        var potentialAssignments = new List<EquationNode>();
        var otherEquations = new List<EquationNode>();

        // Separate potential assignments (Var = ConstExpr) from other equations
        foreach (var statement in fileNode.Statements)
        {
            if (statement is AssignmentNode explicitAssign) // Handle := assignments directly
            {
                Console.WriteLine($"Debug: Found explicit assignment: {explicitAssign.Variable.Name}");
                ExecuteExplicitAssignment(explicitAssign); // Execute immediately
            }
            else if (statement is EquationNode eqNode)
            {
                // Check if it's of the form Var = ConstantValue
                if (eqNode.LeftHandSide is VariableNode varNode && IsConstantValue(eqNode.RightHandSide, out _))
                {
                    Console.WriteLine($"Debug: Found potential assignment equation: {eqNode}");
                    potentialAssignments.Add(eqNode);
                }
                // Check if it's ConstantValue = Var (less common but possible)
                else if (eqNode.RightHandSide is VariableNode varNodeRhs && IsConstantValue(eqNode.LeftHandSide, out _))
                {
                    Console.WriteLine($"Debug: Found potential assignment equation (reversed): {eqNode}");
                    // Treat as assignment Var = Const for processing
                    potentialAssignments.Add(new EquationNode(varNodeRhs, eqNode.LeftHandSide));
                }
                else
                {
                    Console.WriteLine($"Debug: Found potential equation to solve: {eqNode}");
                    otherEquations.Add(eqNode); // Likely needs solving
                }
            }
            else
            {
                Console.WriteLine($"Warning: Skipping unknown statement type: {statement.GetType().Name}");
            }
        }

        // Execute potential assignments (Var = ConstExpr)
        Console.WriteLine("--- Processing Potential Assignments ---");
        foreach (var eqNode in potentialAssignments)
        {
            // We already know RHS is constant, LHS is VariableNode from checks above
            var variableNode = (VariableNode)eqNode.LeftHandSide;
            if (IsConstantValue(eqNode.RightHandSide, out double value)) // Re-evaluate to get value
            {
                Console.WriteLine($"Assigning {variableNode.Name} = {value} (from equation)");
                _variableStore.SetVariable(variableNode.Name, value);
            }
            else
            {
                // Should not happen based on initial check, but handle defensively
                Console.WriteLine($"Warning: Could not evaluate RHS for potential assignment: {eqNode}. Treating as equation.");
                otherEquations.Add(eqNode);
            }
        }

        // Remaining equations are those that need the solver
        EquationsToSolve.AddRange(otherEquations);

        Console.WriteLine("--- Statement Processing Finished ---");
        Console.WriteLine($"--- Collected {EquationsToSolve.Count} equations for solver ---");
    }

    // Executes explicit := assignments
    private void ExecuteExplicitAssignment(AssignmentNode assignNode)
    {
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            double value = _expressionEvaluator.Evaluate(assignNode.RightHandSide);
            Console.WriteLine($"Assigning {assignNode.Variable.Name} := {value} (explicit)");
            _variableStore.SetVariable(assignNode.Variable.Name, value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error evaluating explicit assignment for '{assignNode.Variable.Name}': {ex.Message}");
            // Decide whether to halt
        }
    }

    public bool SolveEquations()
    {
        if (EquationsToSolve.Count == 0)
        {
            Console.WriteLine("No equations identified for solver.");
            return true;
        }

        // Pass only the equations that need solving, along with the solver settings
        var solver = new EquationSolver(_variableStore, _functionRegistry, EquationsToSolve, _solverSettings);
        bool success = solver.Solve();

        return success;
    }
}
