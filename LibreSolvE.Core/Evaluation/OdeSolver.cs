// LibreSolvE.Core/Evaluation/OdeSolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using LibreSolvE.Core.Ast;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Provides functionality for solving Ordinary Differential Equations (ODEs) using various numerical methods.
/// </summary>
public class OdeSolver
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly ExpressionEvaluatorVisitor _evaluator;
    private readonly string _integrandVarName;
    private readonly string _independentVarName;
    private double _stepSize;
    private double _lowerLimit;
    private double _upperLimit;
    private bool _useAdaptiveStepSize;

    // Settings for adaptive step size algorithm
    private bool _varyStepSize = true;
    private int _minSteps = 5;
    private int _maxSteps = 2000;
    private double _reduceThreshold = 1e-1;
    private double _increaseThreshold = 1e-3;

    // Results storage
    private readonly List<double> _timeValues = new List<double>();
    private readonly List<double> _resultValues = new List<double>();

    public OdeSolver(
        VariableStore variableStore,
        FunctionRegistry functionRegistry,
        string integrandVarName,
        string independentVarName,
        double lowerLimit,
        double upperLimit,
        double stepSize = 0.0)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _integrandVarName = integrandVarName ?? throw new ArgumentNullException(nameof(integrandVarName));
        _independentVarName = independentVarName ?? throw new ArgumentNullException(nameof(independentVarName));
        _lowerLimit = lowerLimit;
        _upperLimit = upperLimit;
        _stepSize = stepSize;
        _useAdaptiveStepSize = stepSize <= 0.0;

        // Create evaluator for expressions
        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, false);
    }

    /// <summary>
    /// Configure adaptive step size parameters
    /// </summary>
    public void ConfigureAdaptiveStepSize(bool varyStepSize, int minSteps, int maxSteps, double reduceThreshold, double increaseThreshold)
    {
        _varyStepSize = varyStepSize;
        _minSteps = Math.Max(1, minSteps);
        _maxSteps = Math.Max(_minSteps + 1, maxSteps);
        _reduceThreshold = Math.Max(1e-10, reduceThreshold);
        _increaseThreshold = Math.Max(1e-12, Math.Min(_reduceThreshold / 10, increaseThreshold));
    }

    /// <summary>
    /// Solve the ODE using numerical integration from lower to upper limit
    /// </summary>
    public double Solve()
    {
        // Clear previous results
        _timeValues.Clear();
        _resultValues.Clear();

        // Get initial condition at lower limit
        double initialValue = GetValueAtIndependentVar(_lowerLimit);

        // Use the appropriate method
        if (_useAdaptiveStepSize)
        {
            return SolveWithAdaptiveStep(initialValue);
        }
        else
        {
            return SolveWithFixedStep(initialValue);
        }
    }

    /// <summary>
    /// Solve using a fixed step size (similar to Heun's method/RK2)
    /// </summary>
    private double SolveWithFixedStep(double initialValue)
    {
        double t = _lowerLimit;
        double y = initialValue;
        int steps = CalculateSteps();

        _timeValues.Add(t);
        _resultValues.Add(y);

        double stepSize = (_upperLimit - _lowerLimit) / steps;

        // Perform integration using Heun's method (predictor-corrector)
        for (int i = 0; i < steps; i++)
        {
            // Predictor step (Euler)
            double dydt = GetSlope(t, y);
            double yPredictor = y + dydt * stepSize;

            // Corrector step
            double tNext = t + stepSize;
            double dydtNext = GetSlope(tNext, yPredictor);

            // Final value using average of slopes
            y = y + (dydt + dydtNext) * stepSize / 2.0;
            t = tNext;

            _timeValues.Add(t);
            _resultValues.Add(y);
        }

        return y; // Return final value
    }

    /// <summary>
    /// Solve using adaptive step size
    /// </summary>
    private double SolveWithAdaptiveStep(double initialValue)
    {
        double t = _lowerLimit;
        double y = initialValue;

        _timeValues.Add(t);
        _resultValues.Add(y);

        // Initial step size - estimate based on range or use default
        double stepSize = (_upperLimit - _lowerLimit) / Math.Max(10, _minSteps);
        int steps = 0;

        // Continue until we reach the upper limit or exceed max steps
        while (t < _upperLimit && steps < _maxSteps)
        {
            // Ensure we don't step beyond the upper limit
            if (t + stepSize > _upperLimit)
            {
                stepSize = _upperLimit - t;
            }

            // Use 4th order Runge-Kutta method for improved accuracy
            double k1 = GetSlope(t, y);
            double k2 = GetSlope(t + stepSize/2, y + k1*stepSize/2);
            double k3 = GetSlope(t + stepSize/2, y + k2*stepSize/2);
            double k4 = GetSlope(t + stepSize, y + k3*stepSize);

            // Compute the 4th order approximation
            double yNext = y + (k1 + 2*k2 + 2*k3 + k4) * stepSize / 6.0;

            // Also compute a 2nd order approximation for error estimation
            double yNextLow = y + (k1 + k4) * stepSize / 2.0;

            // Estimate error
            double error = Math.Abs(yNext - yNextLow);
            double relError = (Math.Abs(y) > 1e-10) ? error / Math.Abs(y) : error;

            // Adjust step size if needed
            if (_varyStepSize)
            {
                if (relError > _reduceThreshold && stepSize > (_upperLimit - _lowerLimit) / _maxSteps)
                {
                    // Error too large, reduce step size and try again
                    stepSize *= 0.5;
                    continue;
                }
                else if (relError < _increaseThreshold && steps > 0)
                {
                    // Error small enough, increase step size for next step
                    stepSize *= 1.5;
                }
            }

            // Accept the step
            t += stepSize;
            y = yNext;
            steps++;

            _timeValues.Add(t);
            _resultValues.Add(y);
        }

        return y; // Return final value
    }

    /// <summary>
    /// Get the slope (derivative) at a specific point by evaluating the integrand expression
    /// </summary>
    private double GetSlope(double t, double y)
    {
        // Set independent variable
        _variableStore.SetVariable(_independentVarName, t);

        // If integrand depends on the result variable, set it too
        if (_variableStore.HasVariable(_integrandVarName))
        {
            double oldValue = _variableStore.GetVariable(_integrandVarName);
            _variableStore.SetVariable(_integrandVarName, y);

            // Solve for derivative
            double slope = 0.0;
            try
            {
                // The slope is determined by solving the state equations
                // Force EES to resolve the equations with current values
                _variableStore.ResolveEquations();

                // Get the derivative value
                slope = _variableStore.GetVariable("dydt"); // Assuming this is the derivative variable

                // Reset the integrand variable to its original value
                _variableStore.SetVariable(_integrandVarName, oldValue);

                return slope;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error evaluating slope at t={t}, y={y}: {ex.Message}");
                throw;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Calculate the number of steps for fixed step size integration
    /// </summary>
    private int CalculateSteps()
    {
        double range = Math.Abs(_upperLimit - _lowerLimit);
        double steps = range / _stepSize;
        return Math.Max(_minSteps, (int)Math.Ceiling(steps));
    }

    /// <summary>
    /// Get value of the result variable at a specific point of independent variable
    /// </summary>
    private double GetValueAtIndependentVar(double t)
    {
        // Set independent variable
        _variableStore.SetVariable(_independentVarName, t);

        // If this is initial condition, use a default value
        if (!_variableStore.HasVariable(_integrandVarName))
        {
            return 0.0; // Default initial value
        }

        return _variableStore.GetVariable(_integrandVarName);
    }

    /// <summary>
    /// Get the integration results
    /// </summary>
    public (List<double> Times, List<double> Values) GetResults()
    {
        return (_timeValues, _resultValues);
    }
}
