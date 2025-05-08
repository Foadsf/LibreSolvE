// LibreSolvE.Core/Evaluation/EquationSolver.cs
using LibreSolvE.Core.Ast;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Differentiation;
using MathNet.Numerics.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

public class EquationSolver
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly List<EquationNode> _equations;
    private readonly ExpressionEvaluatorVisitor _evaluator;
    private readonly SolverSettings _solverSettings;
    private List<string> _variablesToSolve = new List<string>();

    public EquationSolver(VariableStore variableStore, List<EquationNode> equations)
        : this(variableStore, new FunctionRegistry(), equations)
    {
    }

    public EquationSolver(VariableStore variableStore, FunctionRegistry functionRegistry, List<EquationNode> equations)
        : this(variableStore, functionRegistry, equations, new SolverSettings())
    {
    }

    public EquationSolver(VariableStore variableStore, FunctionRegistry functionRegistry, List<EquationNode> equations, SolverSettings settings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _equations = equations ?? throw new ArgumentNullException(nameof(equations));
        _solverSettings = settings ?? new SolverSettings();
        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, true); // Treat warnings as errors during solve

        IdentifyVariablesToSolve();
    }

    public EquationSolver(VariableStore variableStore, FunctionRegistry functionRegistry, List<EquationNode> equations, SolverSettings settings, List<string> forcedVariablesToSolve)
    : this(variableStore, functionRegistry, equations, settings)
    {
        _variablesToSolve = forcedVariablesToSolve ?? throw new ArgumentNullException(nameof(forcedVariablesToSolve));

        Console.WriteLine($"--- Solver using forced variable list ({_variablesToSolve.Count}): ---");
        if (_variablesToSolve.Count > 0) Console.WriteLine(string.Join(", ", _variablesToSolve));
        Console.WriteLine("--------------------------------------------------");
    }

    private void IdentifyVariablesToSolve()
    {
        var allVarsInEquations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eq in _equations)
        {
            CollectVariables(eq.LeftHandSide, allVarsInEquations);
            CollectVariables(eq.RightHandSide, allVarsInEquations);
        }

        _variablesToSolve = allVarsInEquations
                            .Where(v => !_variableStore.IsExplicitlySet(v))
                            .OrderBy(v => v)
                            .ToList();

        Console.WriteLine($"--- Solver identified {_variablesToSolve.Count} variables to solve for: ---");
        if (_variablesToSolve.Count > 0) Console.WriteLine(string.Join(", ", _variablesToSolve));
        Console.WriteLine("--------------------------------------------------");

        if (_equations.Count != _variablesToSolve.Count)
        {
            Console.WriteLine($"Warning: System has {_equations.Count} equations and {_variablesToSolve.Count} unknowns.");
            // LevenbergMarquardt can handle non-square systems (least-squares fit)
        }
    }

    private void CollectVariables(AstNode node, HashSet<string> variables)
    {
        switch (node)
        {
            case VariableNode varNode:
                variables.Add(varNode.Name);
                break;
            case BinaryOperationNode binOp:
                CollectVariables(binOp.Left, variables);
                CollectVariables(binOp.Right, variables);
                break;
            case NumberNode: break;
            case FunctionCallNode funcCall:
                // Collect variables in function arguments
                foreach (var arg in funcCall.Arguments)
                {
                    CollectVariables(arg, variables);
                }
                break;
            default:
                Console.WriteLine($"Warning: Variable collection not implemented for node type {node?.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// Solve the system of equations
    /// </summary>
    public bool Solve()
    {
        if (_variablesToSolve.Count == 0)
        {
            Console.WriteLine("No variables identified to solve for.");
            // Optionally check if the existing equations are already satisfied
            return _equations.Count == 0;
        }

        try
        {
            Console.WriteLine($"--- Starting {_solverSettings.SolverType} Solver ---");

            // Create a solver factory
            var solverFactory = new SolverFactory(
                _variableStore,
                _functionRegistry,
                _equations,
                _variablesToSolve,
                _solverSettings);

            if (_solverSettings.SolverType == SolverType.NelderMead)
            {
                return SolveWithNelderMead(solverFactory);
            }
            else if (_solverSettings.SolverType == SolverType.LevenbergMarquardt)
            {
                return SolveWithLevenbergMarquardt(solverFactory);
            }
            else
            {
                throw new NotImplementedException($"Solver type not implemented: {_solverSettings.SolverType}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during solving process: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return false;
        }
    }

    private bool SolveWithNelderMead(SolverFactory solverFactory)
    {
        // Get the solver from the factory
        var nelderMead = (NelderMeadSimplex)solverFactory.CreateSolver();

        // Create objective function
        var objective = solverFactory.CreateObjectiveFunction();

        // Create initial guess
        Vector<double> initialGuess = solverFactory.CreateInitialGuessVector();

        // Solve
        var result = nelderMead.FindMinimum(objective, initialGuess);
        var solution = result.MinimizingPoint;
        var finalValue = result.FunctionInfoAtMinimum.Value;

        // Apply solution to variable store
        // solverFactory.ApplyVariableValues(solution);
        ApplySolvedVariableValues(solution);

        // Calculate final residuals
        double sumSq = CalculateSumSquaredResiduals(solution); // Pass solution vector
        double finalResidualNorm = Math.Sqrt(sumSq);

        Console.WriteLine("--- Solver Finished ---");
        Console.WriteLine($"Final Objective Value: {finalValue}");
        Console.WriteLine($"Final Residual L2 Norm: {finalResidualNorm}");

        // Check convergence (use a smaller tolerance for actual convergence check)
        bool converged = finalResidualNorm < _solverSettings.Tolerance;

        if (converged)
        {
            Console.WriteLine("--- Solution Converged ---");
            // Values are already set via ApplySolvedVariableValues
            return true;
        }
        else
        {
            Console.Error.WriteLine($"Solver did not converge sufficiently (Residual Norm: {finalResidualNorm})");
            Console.WriteLine("Final values (might be inaccurate):");
            // Apply the non-converged solution for inspection if desired
            // ApplySolvedVariableValues(solution); // Or leave the store as it was before solve started
            _variableStore.PrintVariables();
            return false;
        }
    }

    // Helper to apply solved values
    private void ApplySolvedVariableValues(Vector<double> x)
    {
        for (int i = 0; i < _variablesToSolve.Count; i++)
        {
            string varName = _variablesToSolve[i];
            _variableStore.SetSolvedVariable(varName, x[i]); // Use the new method
        }
    }

    // Helper to calculate residuals using a specific parameter vector
    private double CalculateSumSquaredResiduals(Vector<double> parameters)
    {
        // Temporarily apply values, calculate, restore
        var factory = new SolverFactory(_variableStore, _functionRegistry, _equations, _variablesToSolve, _solverSettings);
        factory.ApplyVariableValues(parameters);
        double sumOfSquares = 0.0;
        try
        {
            for (int i = 0; i < _equations.Count; i++)
            {
                double lhs = _evaluator.Evaluate(_equations[i].LeftHandSide);
                double rhs = _evaluator.Evaluate(_equations[i].RightHandSide);
                double residual = lhs - rhs;
                sumOfSquares += residual * residual;
            }
        }
        finally
        {
            factory.RestoreVariableValues();
        }
        return sumOfSquares;
    }

    private bool SolveWithLevenbergMarquardt(SolverFactory solverFactory)
    {
        try
        {
            // Since fully implementing Levenberg-Marquardt is complex and requires more adaptation,
            // we'll use Nelder-Mead as a fallback for now
            Console.WriteLine("Warning: Levenberg-Marquardt solver not fully implemented yet, falling back to Nelder-Mead");
            return SolveWithNelderMead(solverFactory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in Levenberg-Marquardt solver: {ex.Message}");
            Console.WriteLine("Falling back to Nelder-Mead solver...");
            return SolveWithNelderMead(solverFactory);
        }
    }

    /// <summary>
    /// Create a model function for the Levenberg-Marquardt solver
    /// </summary>
    private IObjectiveModel CreateModelFunction(SolverFactory solverFactory)
    {
        // PLACEHOLDER - this is not actually implemented correctly
        // We would need to create a proper implementation of IObjectiveModel
        // But for now we'll use a simplified approach
        throw new NotImplementedException("LM solver ModelFunction not fully implemented");
    }
}
