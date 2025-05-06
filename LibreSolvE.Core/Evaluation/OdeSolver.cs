// LibreSolvE.Core/Evaluation/OdeSolver.cs
using System;
using System.Collections.Generic; // Keep using System.Collections.Generic for flexibility
using System.Linq;
using System.Globalization;
using LibreSolvE.Core.Ast;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Provides functionality for solving Ordinary Differential Equations (ODEs) using various numerical methods.
/// Relies on the StatementExecutor to record results.
/// </summary>
public class OdeSolver
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly ExpressionEvaluatorVisitor _evaluator;
    private readonly string _integrandVarName;     // Name of the derivative variable (e.g., "dydt")
    private readonly string _dependentVarName;     // Name of the variable being integrated (e.g., "y")
    private readonly string _independentVarName;   // Name of the integration variable (e.g., "t")
    private readonly double _lowerLimit;
    private readonly double _upperLimit;
    private readonly double _stepSize;             // If > 0, use fixed step; otherwise adaptive
    private readonly bool _useAdaptiveStepSize;

    // *** NEW: Reference to the executor for recording results ***
    private readonly StatementExecutor _statementExecutor;

    // Settings for adaptive step size algorithm
    private bool _varyStepSize = true;
    private int _minSteps = 5;
    private int _maxSteps = 2000;
    private double _reduceThreshold = 1e-1;
    private double _increaseThreshold = 1e-3;


    // *** MODIFIED CONSTRUCTOR ***
    public OdeSolver(
        StatementExecutor statementExecutor, // Added executor parameter
        VariableStore variableStore,
        FunctionRegistry functionRegistry,
        string integrandVarName,     // e.g., "dydt"
        string dependentVarName,     // e.g., "y"
        string independentVarName,   // e.g., "t"
        double lowerLimit,
        double upperLimit,
        double stepSize = 0.0)       // stepSize <= 0 means adaptive
    {
        _statementExecutor = statementExecutor ?? throw new ArgumentNullException(nameof(statementExecutor));
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _integrandVarName = integrandVarName ?? throw new ArgumentNullException(nameof(integrandVarName));
        _dependentVarName = dependentVarName ?? throw new ArgumentNullException(nameof(dependentVarName));
        _independentVarName = independentVarName ?? throw new ArgumentNullException(nameof(independentVarName));
        _lowerLimit = lowerLimit;
        _upperLimit = upperLimit;
        _stepSize = stepSize;
        _useAdaptiveStepSize = stepSize <= 0.0;

        // Create evaluator for expressions - passes executor for context if needed
        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, _statementExecutor, false);
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
        Console.WriteLine($"ODE Solver Adaptive Config: Vary={_varyStepSize}, Min={_minSteps}, Max={_maxSteps}, Reduce={_reduceThreshold}, Increase={_increaseThreshold}");
    }

    /// <summary>
    /// Solve the ODE using numerical integration from lower to upper limit
    /// </summary>
    public double Solve()
    {
        // Get initial condition from the variable store at the lower limit
        double originalIndepVarValue = _variableStore.HasVariable(_independentVarName)
                                     ? _variableStore.GetVariable(_independentVarName)
                                     : double.NaN;
        _variableStore.SetVariable(_independentVarName, _lowerLimit);

        if (!_variableStore.HasVariable(_dependentVarName))
        {
            Console.WriteLine($"Warning: Initial condition for '{_dependentVarName}' at {_independentVarName}={_lowerLimit} not found. Assuming 0.");
            _variableStore.SetVariable(_dependentVarName, 0.0); // Assume 0 if not set
        }
        double initialValue = _variableStore.GetVariable(_dependentVarName);
        Console.WriteLine($"ODE Solver: Initial Condition: {_dependentVarName}={initialValue} at {_independentVarName}={_lowerLimit}");

        // Restore original independent variable value if it existed
        if (!double.IsNaN(originalIndepVarValue))
        {
            _variableStore.SetVariable(_independentVarName, originalIndepVarValue);
        }

        // *** UPDATED CALL: Record the initial state in the table ***
        _statementExecutor.RecordIntegralStep(_lowerLimit, initialValue, _independentVarName, _dependentVarName, forceRecord: true); // Force record initial

        double finalValue;
        // Use the appropriate method
        if (_useAdaptiveStepSize)
        {
            finalValue = SolveWithAdaptiveStep(initialValue);
        }
        else
        {
            finalValue = SolveWithFixedStep(initialValue);
        }

        // *** UPDATED CALL: Ensure the final state is recorded ***
        // Check if last recorded time is different from upperLimit before adding
        _statementExecutor.RecordIntegralStep(_upperLimit, finalValue, _independentVarName, _dependentVarName, forceRecord: true); // Force record final

        return finalValue;
    }


    /// <summary>
    /// Solve using a fixed step size (Heun's method/RK2)
    /// </summary>
    private double SolveWithFixedStep(double initialValue)
    {
        double t = _lowerLimit;
        double y = initialValue;
        int steps = CalculateSteps(); // Calculate number of steps based on _stepSize

        Console.WriteLine($"ODE Solver (Fixed Step): Steps={steps}, StepSize={_stepSize}");


        double stepSizeActual = (_upperLimit - _lowerLimit) / steps; // Use calculated step size

        // Perform integration using Heun's method (predictor-corrector)
        for (int i = 0; i < steps; i++)
        {
            // Predictor step (Euler)
            double dydt = GetSlope(t, y);
            double yPredictor = y + dydt * stepSizeActual;

            // Corrector step
            double tNext = t + stepSizeActual;
            // Important: Use the *predicted* y value at tNext to estimate the slope there
            double dydtNext = GetSlope(tNext, yPredictor);

            // Final value using average of slopes
            y = y + (dydt + dydtNext) * stepSizeActual / 2.0;
            t = tNext; // Update time *after* using old t for slope calculation

            // *** UPDATED CALL: Record step using executor ***
            _statementExecutor.RecordIntegralStep(t, y, _independentVarName, _dependentVarName);
        }

        return y; // Return final value
    }

    /// <summary>
    /// Solve using adaptive step size (RK4 based, needs improvement)
    /// </summary>
    private double SolveWithAdaptiveStep(double initialValue)
    {
        double t = _lowerLimit;
        double y = initialValue;

        Console.WriteLine("ODE Solver (Adaptive Step): Starting...");

        // Initial step size heuristic - can be improved
        double stepSize = (_upperLimit - _lowerLimit) / Math.Max(10, _minSteps);
        int stepsTaken = 0;
        const double safetyFactor = 0.9;
        // const double minStepFactor = 0.2; // DELETE
        // const double maxStepFactor = 5.0; // DELETE
        double minAllowedStep = (_upperLimit - _lowerLimit) / _maxSteps / 10; // Heuristic min
        double maxAllowedStep = (_upperLimit - _lowerLimit) / _minSteps * 10; // Heuristic max


        while (t < _upperLimit && stepsTaken < _maxSteps)
        {
            // Ensure we don't step beyond the upper limit
            stepSize = Math.Min(stepSize, _upperLimit - t);
            stepSize = Math.Max(stepSize, minAllowedStep); // Ensure minimum step

            // --- RKF45-like approach (simplified error estimation) ---
            // Calculate step using RK4
            double k1 = stepSize * GetSlope(t, y);
            double k2 = stepSize * GetSlope(t + 0.5 * stepSize, y + 0.5 * k1);
            double k3 = stepSize * GetSlope(t + 0.5 * stepSize, y + 0.5 * k2);
            double k4 = stepSize * GetSlope(t + stepSize, y + k3);

            double y_rk4 = y + (k1 + 2 * k2 + 2 * k3 + k4) / 6.0;

            // Estimate error (difference between RK4 and a lower order method, e.g., Heun using same slopes)
            double y_heun = y + (k1 + k4) / 2.0; // Simplified Heun using k1 and k4
            double errorEstimate = Math.Abs(y_rk4 - y_heun);
            double relativeError = (Math.Abs(y_rk4) > 1e-10) ? errorEstimate / Math.Abs(y_rk4) : errorEstimate;

            if (_varyStepSize)
            {
                // Adjust step size
                if (relativeError > _reduceThreshold && stepSize > minAllowedStep * 1.1) // Don't shrink below min
                {
                    stepSize = Math.Max(minAllowedStep, stepSize * Math.Pow(safetyFactor * _reduceThreshold / relativeError, 0.25)); // Reduce step
                                                                                                                                     //Console.WriteLine($"    Reducing step to {stepSize:G3} due to relError {relativeError:G3}");
                    continue; // Recalculate with smaller step
                }
                else if (relativeError < _increaseThreshold)
                {
                    stepSize = Math.Min(maxAllowedStep, stepSize * Math.Pow(safetyFactor * _increaseThreshold / relativeError, 0.20)); // Increase step
                                                                                                                                       //Console.WriteLine($"    Increasing step to {stepSize:G3} due to relError {relativeError:G3}");
                }
            }


            // Accept the RK4 step
            t += stepSize;
            y = y_rk4;
            stepsTaken++;

            // *** UPDATED CALL: Record step using executor ***
            _statementExecutor.RecordIntegralStep(t, y, _independentVarName, _dependentVarName);

            // Prepare for next potential step size increase (only if error was small)
            if (_varyStepSize && relativeError < _increaseThreshold)
            {
                stepSize = Math.Min(maxAllowedStep, stepSize * Math.Pow(safetyFactor * _increaseThreshold / relativeError, 0.20));
            }
            stepSize = Math.Min(stepSize, _upperLimit - t); // Don't overshoot on next loop check
            stepSize = Math.Max(stepSize, minAllowedStep); // Ensure minimum step for next try
        }

        if (stepsTaken >= _maxSteps)
        {
            Console.WriteLine($"Warning: ODE solver reached maximum iterations ({_maxSteps}).");
        }
        else
        {
            Console.WriteLine($"ODE Solver (Adaptive Step): Finished in {stepsTaken} steps.");
        }


        return y; // Return final value
    }


    /// <summary>
    /// Get the slope (derivative) at a specific point by evaluating the integrand variable
    /// after ensuring the system is solved for the current independent/dependent variables.
    /// </summary>
    private double GetSlope(double tValue, double yValue)
    {
        // 1. Set the current state variables
        _variableStore.SetVariable(_independentVarName, tValue);
        _variableStore.SetVariable(_dependentVarName, yValue);

        // 2. Force the StatementExecutor to resolve algebraic equations
        //    This assumes the StatementExecutor has a method to do this.
        //    If not, we might need to pass the list of algebraic equations here,
        //    or have a more complex interaction. For now, assume executor handles it.
        //    *** THIS IS A CRITICAL INTERACTION THAT NEEDS CAREFUL DESIGN ***
        //    Let's assume, for now, that evaluating the integrand variable implicitly
        //    triggers necessary calculations if the VariableStore/Evaluator are set up correctly.
        //    A better approach might be needed later.

        // 3. Evaluate the integrand variable (e.g., 'dydt')
        try
        {
            if (!_variableStore.HasVariable(_integrandVarName))
            {
                // This often happens on the first step before equations are solved.
                // The algebraic solver part of the main loop should calculate it.
                // Here, we might need to trigger a mini-solve or return 0 if it's expected
                // to be calculated later. For now, let's assume it gets calculated.
                // If the algebraic equations depend *only* on t and y, we could solve them here.
                // But if they depend on other integrated variables, it's complex.
                Console.WriteLine($"Warning: Integrand '{_integrandVarName}' not directly available. Attempting evaluation anyway.");
                // Force evaluation by trying to get it - the evaluator might trigger calculation.
            }
            // Retrieve the value - this should ideally reflect the solved state for tValue, yValue
            double slope = _variableStore.GetVariable(_integrandVarName);
            // Console.WriteLine($"      Slope({_independentVarName}={tValue:G4}, {_dependentVarName}={yValue:G4}) = {slope:G4}");
            return slope;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error evaluating slope '{_integrandVarName}' at {_independentVarName}={tValue}, {_dependentVarName}={yValue}: {ex.Message}");
            // Depending on strategy, might return 0, NaN, or throw. Throwing is safer for now.
            throw new InvalidOperationException($"Failed to evaluate the derivative '{_integrandVarName}'. Check the state equation.", ex);
        }
    }


    /// <summary>
    /// Calculate the number of steps for fixed step size integration
    /// </summary>
    private int CalculateSteps()
    {
        // Ensure step size is positive if specified
        if (!_useAdaptiveStepSize && _stepSize <= 0)
        {
            throw new ArgumentException("Fixed StepSize must be positive.", nameof(_stepSize));
        }

        double range = Math.Abs(_upperLimit - _lowerLimit);
        if (range == 0) return _minSteps; // Avoid division by zero if limits are same

        // Calculate steps based on fixed step size
        double stepsDouble = range / _stepSize;

        // Ensure minimum number of steps
        int steps = (int)Math.Ceiling(stepsDouble);
        steps = Math.Max(_minSteps, steps);
        // Ensure maximum number of steps is not exceeded (though usually adaptive handles this)
        steps = Math.Min(_maxSteps, steps);

        return steps;
    }

    // *** REMOVED GetResults() and internal lists (_timeValues, _resultValues) ***
}
