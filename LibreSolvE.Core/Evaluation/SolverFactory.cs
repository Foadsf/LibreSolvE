// LibreSolvE.Core/Evaluation/SolverFactory.cs
using System;
using System.Collections.Generic;
using System.Linq;
using LibreSolvE.Core.Ast;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Factory class for creating numerical solvers
/// </summary>
public class SolverFactory
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly List<EquationNode> _equations;
    private readonly List<string> _variablesToSolve;
    private readonly ExpressionEvaluatorVisitor _evaluator;
    private readonly SolverSettings _settings;

    // Store original values to restore after function evaluations
    private readonly Dictionary<string, double> _originalValuesBackup = new Dictionary<string, double>();

    public SolverFactory(
        VariableStore variableStore,
        FunctionRegistry functionRegistry,
        List<EquationNode> equations,
        List<string> variablesToSolve,
        SolverSettings settings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _equations = equations ?? throw new ArgumentNullException(nameof(equations));
        _variablesToSolve = variablesToSolve ?? throw new ArgumentNullException(nameof(variablesToSolve));
        _settings = settings ?? new SolverSettings();

        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, true);
    }

    /// <summary>
    /// Create a solver based on the specified settings
    /// </summary>
    public object CreateSolver()
    {
        switch (_settings.SolverType)
        {
            case SolverType.NelderMead:
                return CreateNelderMeadSolver();
            case SolverType.LevenbergMarquardt:
                return CreateLevenbergMarquardtSolver();
            default:
                throw new ArgumentException($"Unsupported solver type: {_settings.SolverType}");
        }
    }

    /// <summary>
    /// Create objective function for equation solving
    /// </summary>
    public IObjectiveFunction CreateObjectiveFunction()
    {
        // Define objective function
        Func<Vector<double>, double> objectiveFunc = parameters =>
        {
            ApplyVariableValues(parameters);
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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during evaluation: {ex.Message}");
                sumOfSquares = double.MaxValue; // Mark failure with a large value
            }
            finally
            {
                RestoreVariableValues();
            }

            return sumOfSquares;
        };

        return ObjectiveFunction.Value(objectiveFunc);
    }

    /// <summary>
    /// Create initial guess vector for optimization
    /// </summary>
    public Vector<double> CreateInitialGuessVector()
    {
        Vector<double> initialGuess = Vector<double>.Build.Dense(_variablesToSolve.Count);

        Console.WriteLine("--- Initial Guess Vector ---");
        for (int i = 0; i < _variablesToSolve.Count; i++)
        {
            string varName = _variablesToSolve[i];
            double guessValue;

            // Check if there's a guess value set in the solver settings
            if (_settings.HasGuessValue(varName))
            {
                guessValue = _settings.GetGuessValue(varName);
            }
            // Otherwise check the variable store for a guess
            else if (_variableStore.HasGuessValue(varName))
            {
                guessValue = _variableStore.GetGuessValue(varName);
            }
            // Use default if no guess is available
            else
            {
                guessValue = 1.0;
            }

            initialGuess[i] = guessValue;
            Console.WriteLine($"{varName} = {guessValue}");
        }
        Console.WriteLine("----------------------------");

        return initialGuess;
    }

    /// <summary>
    /// Create a Nelder-Mead simplex optimizer
    /// </summary>
    private NelderMeadSimplex CreateNelderMeadSolver()
    {
        return new NelderMeadSimplex(1e-10, _settings.MaxIterations);
    }

    /// <summary>
    /// Create a Levenberg-Marquardt optimizer
    /// </summary>
    private LevenbergMarquardtMinimizer CreateLevenbergMarquardtSolver()
    {
        // Create and return the LM solver with default settings
        double gradientTolerance = _settings.Tolerance;  // Default: 1e-8
        double stepTolerance = _settings.Tolerance;      // Default: 1e-8
        double functionTolerance = _settings.Tolerance;  // Default: 1e-8
        int maxIterations = _settings.MaxIterations;     // Default: 1000

        return new LevenbergMarquardtMinimizer(
            gradientTolerance,
            stepTolerance,
            functionTolerance,
            maxIterations);
    }

    /// <summary>
    /// Apply variable values to the variable store
    /// </summary>
    public void ApplyVariableValues(Vector<double> x)
    {
        _originalValuesBackup.Clear();
        for (int i = 0; i < _variablesToSolve.Count; i++)
        {
            string varName = _variablesToSolve[i];
            if (_variableStore.HasVariable(varName)) // Backup existing value
            {
                _originalValuesBackup[varName] = _variableStore.GetVariable(varName);
            }
            else
            {
                // Mark that this variable didn't exist before applying
                _originalValuesBackup[varName] = double.NaN; // Use NaN as a sentinel
            }
            _variableStore.SetVariable(varName, x[i]);
        }
    }

    /// <summary>
    /// Restore variable values from backup
    /// </summary>
    public void RestoreVariableValues()
    {
        foreach (var kvp in _originalValuesBackup)
        {
            if (double.IsNaN(kvp.Value))
            {
                // This variable didn't exist before, potentially remove it?
                // For now, just leave its last calculated value in the store.
                // A more robust system might track creation/deletion.
            }
            else
            {
                _variableStore.SetVariable(kvp.Key, kvp.Value);
            }
        }
        _originalValuesBackup.Clear(); // Clear backup after restoring
    }
}
