// LibreSolvE.Core/Evaluation/EquationSolver.cs
using LibreSolvE.Core.Ast;
using MathNet.Numerics; // For Vector<double>, Matrix<double>
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Differentiation; // For numerical differentiation
using MathNet.Numerics.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

public class EquationSolver
{
    private readonly VariableStore _variableStore;
    private readonly List<EquationNode> _equations;
    private readonly ExpressionEvaluatorVisitor _evaluator;
    private List<string> _variablesToSolve = new List<string>();
    // Store original values to restore after function evaluations
    private Dictionary<string, double> _originalValuesBackup = new Dictionary<string, double>();

    public EquationSolver(VariableStore variableStore, List<EquationNode> equations)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _equations = equations ?? throw new ArgumentNullException(nameof(equations));
        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, true); // Treat warnings as errors during solve

        IdentifyVariablesToSolve();
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
            default:
                Console.WriteLine($"Warning: Variable collection not implemented for node type {node?.GetType().Name}");
                break;
        }
    }

    // Helper to apply a vector of values to the variables being solved
    private void ApplyVariableValues(Vector<double> x)
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

    // Helper to restore variable values from backup
    private void RestoreVariableValues()
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

    // Define the model function for each equation
    private Vector<double> ModelFunction(Vector<double> parameters)
    {
        ApplyVariableValues(parameters); // Update store with current parameters
        Vector<double> residuals = Vector<double>.Build.Dense(_equations.Count);

        try
        {
            for (int i = 0; i < _equations.Count; i++)
            {
                residuals[i] = _evaluator.Evaluate(_equations[i].LeftHandSide) -
                               _evaluator.Evaluate(_equations[i].RightHandSide);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during model evaluation: {ex.Message}");
            residuals.MapInplace(_ => double.NaN); // Mark failure
        }
        finally
        {
            RestoreVariableValues(); // Restore original values
        }

        return residuals;
    }

    // --- Solver Logic ---
    public bool Solve()
    {
        if (_variablesToSolve.Count == 0)
        {
            Console.WriteLine("No variables identified to solve for.");
            // Optionally check if the existing equations are already satisfied
            return _equations.Count == 0;
        }

        int numVars = _variablesToSolve.Count;
        int numEqs = _equations.Count;

        // Initial guess vector
        Vector<double> initialGuess = Vector<double>.Build.Dense(numVars);
        Console.WriteLine("--- Initial Guess Vector ---");
        for (int i = 0; i < numVars; i++)
        {
            initialGuess[i] = _variableStore.GetVariable(_variablesToSolve[i]); // Uses default 1.0 if not set
            Console.WriteLine($"{_variablesToSolve[i]} = {initialGuess[i]}");
        }
        Console.WriteLine("----------------------------");

        try
        {
            Console.WriteLine("--- Starting Custom Numerical Solver ---");

            // Define a simpler objective function that computes sum of squared residuals
            Func<Vector<double>, double> objectiveFunc = parameters =>
            {
                ApplyVariableValues(parameters); // Update store with current parameters
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
                    RestoreVariableValues(); // Restore original values
                }

                return sumOfSquares;
            };

            // Use our own implementation of Nelder-Mead simplex algorithm
            // This avoids issues with MathNet.Numerics API version differences
            Vector<double> currentPoint = initialGuess;
            double currentValue = objectiveFunc(currentPoint);

            // Create simplex - a set of n+1 points for n-dimensional optimization
            int n = numVars;
            List<Vector<double>> simplex = new List<Vector<double>>(n + 1);
            List<double> values = new List<double>(n + 1);

            // First point is the initial guess
            simplex.Add(currentPoint);
            values.Add(currentValue);

            // Create the other n points of the simplex by varying each coordinate
            for (int i = 0; i < n; i++)
            {
                Vector<double> point = currentPoint.Clone();
                point[i] += (point[i] == 0) ? 0.1 : 0.1 * point[i]; // Vary coordinate by 10%
                simplex.Add(point);
                values.Add(objectiveFunc(point));
            }

            // Nelder-Mead parameters
            double alpha = 1.0;  // Reflection
            double gamma = 2.0;  // Expansion
            double rho = 0.5;    // Contraction
            double sigma = 0.5;  // Shrink

            int maxIterations = 1000;
            double tolerance = 1e-6;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Print progress every 10 iterations
                if (iteration % 10 == 0)
                {
                    Console.WriteLine($"Iteration {iteration}, Best value: {values.Min()}");
                }

                // Find indices of best, worst, and second worst points
                int bestIdx = 0;
                int worstIdx = 0;
                int secondWorstIdx = 0;

                for (int i = 0; i < n + 1; i++)
                {
                    if (values[i] < values[bestIdx]) bestIdx = i;
                    if (values[i] > values[worstIdx]) worstIdx = i;
                }

                for (int i = 0; i < n + 1; i++)
                {
                    if (i != worstIdx && (secondWorstIdx == worstIdx || values[i] > values[secondWorstIdx]))
                        secondWorstIdx = i;
                }

                // Check convergence - if all points are very close
                double maxDist = 0.0;
                for (int i = 0; i < n + 1; i++)
                {
                    for (int j = i + 1; j < n + 1; j++)
                    {
                        double dist = (simplex[i] - simplex[j]).L2Norm();
                        maxDist = Math.Max(maxDist, dist);
                    }
                }

                if (maxDist < tolerance || values[bestIdx] < tolerance)
                {
                    Console.WriteLine($"Converged at iteration {iteration}, Best value: {values[bestIdx]}");
                    currentPoint = simplex[bestIdx];
                    currentValue = values[bestIdx];
                    break;
                }

                // Calculate centroid of all points except worst
                Vector<double> centroid = Vector<double>.Build.Dense(n);
                for (int i = 0; i < n + 1; i++)
                {
                    if (i != worstIdx)
                        centroid += simplex[i];
                }
                centroid /= n;

                // Reflection
                Vector<double> reflection = centroid + alpha * (centroid - simplex[worstIdx]);
                double reflectionValue = objectiveFunc(reflection);

                if (reflectionValue < values[bestIdx])
                {
                    // Expansion
                    Vector<double> expansion = centroid + gamma * (reflection - centroid);
                    double expansionValue = objectiveFunc(expansion);

                    if (expansionValue < reflectionValue)
                    {
                        simplex[worstIdx] = expansion;
                        values[worstIdx] = expansionValue;
                    }
                    else
                    {
                        simplex[worstIdx] = reflection;
                        values[worstIdx] = reflectionValue;
                    }
                }
                else if (reflectionValue < values[secondWorstIdx])
                {
                    // Keep reflection
                    simplex[worstIdx] = reflection;
                    values[worstIdx] = reflectionValue;
                }
                else
                {
                    // Contraction
                    if (reflectionValue < values[worstIdx])
                    {
                        // Outside contraction
                        Vector<double> outsideContraction = centroid + rho * (reflection - centroid);
                        double outsideContractionValue = objectiveFunc(outsideContraction);

                        if (outsideContractionValue <= reflectionValue)
                        {
                            simplex[worstIdx] = outsideContraction;
                            values[worstIdx] = outsideContractionValue;
                        }
                        else
                        {
                            // Shrink
                            ShrinkSimplex(simplex, values, bestIdx, sigma, objectiveFunc);
                        }
                    }
                    else
                    {
                        // Inside contraction
                        Vector<double> insideContraction = centroid - rho * (reflection - centroid);
                        double insideContractionValue = objectiveFunc(insideContraction);

                        if (insideContractionValue < values[worstIdx])
                        {
                            simplex[worstIdx] = insideContraction;
                            values[worstIdx] = insideContractionValue;
                        }
                        else
                        {
                            // Shrink
                            ShrinkSimplex(simplex, values, bestIdx, sigma, objectiveFunc);
                        }
                    }
                }
            }

            // Get best solution
            int bestIndex = 0;
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] < values[bestIndex])
                    bestIndex = i;
            }

            Vector<double> solution = simplex[bestIndex];
            double finalValue = values[bestIndex];

            // Apply solution to variable store
            ApplyVariableValues(solution);

            // Calculate final residuals
            double sumSq = 0.0;
            for (int i = 0; i < _equations.Count; i++)
            {
                double lhs = _evaluator.Evaluate(_equations[i].LeftHandSide);
                double rhs = _evaluator.Evaluate(_equations[i].RightHandSide);
                double res = lhs - rhs;
                sumSq += res * res;
            }

            double finalResidualNorm = Math.Sqrt(sumSq);

            Console.WriteLine("--- Solver Finished ---");
            Console.WriteLine($"Final Objective Value: {finalValue}");
            Console.WriteLine($"Final Residual L2 Norm: {finalResidualNorm}");

            // Check convergence
            bool converged = finalResidualNorm < 1e-2; // More relaxed tolerance

            if (converged)
            {
                Console.WriteLine("--- Solution Converged, Updating Variable Store ---");
                // Don't restore, keep solution values
                _originalValuesBackup.Clear();
                return true;
            }
            else
            {
                Console.Error.WriteLine($"Solver did not converge sufficiently (Residual Norm: {finalResidualNorm})");
                Console.WriteLine("Final values (might be inaccurate):");
                _variableStore.PrintVariables();
                RestoreVariableValues(); // Restore for safety
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during solving process: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            RestoreVariableValues();
            return false;
        }
    }

    // Helper method for the Nelder-Mead algorithm
    private void ShrinkSimplex(List<Vector<double>> simplex, List<double> values, int bestIdx, double sigma,
                            Func<Vector<double>, double> objectiveFunc)
    {
        Vector<double> best = simplex[bestIdx];
        for (int i = 0; i < simplex.Count; i++)
        {
            if (i != bestIdx)
            {
                simplex[i] = best + sigma * (simplex[i] - best);
                values[i] = objectiveFunc(simplex[i]);
            }
        }
    }
}
