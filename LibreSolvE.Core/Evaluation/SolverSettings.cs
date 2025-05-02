// LibreSolvE.Core/Evaluation/SolverSettings.cs
using System;
using System.Collections.Generic;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Enum representing the available numerical solvers
/// </summary>
public enum SolverType
{
    NelderMead,       // Derivative-free simplex method
    LevenbergMarquardt // Gradient-based, efficient for least-squares problems
}

/// <summary>
/// Settings and configuration for the equation solver
/// </summary>
public class SolverSettings
{
    // Solver algorithm selection
    public SolverType SolverType { get; set; } = SolverType.NelderMead;

    // Convergence criteria
    public double Tolerance { get; set; } = 1e-6;
    public int MaxIterations { get; set; } = 1000;

    // Variable bounds and initial guesses
    private Dictionary<string, VariableSettings> _variableSettings =
        new Dictionary<string, VariableSettings>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get settings for a specific variable
    /// </summary>
    public VariableSettings GetVariableSettings(string variableName)
    {
        if (!_variableSettings.TryGetValue(variableName, out var settings))
        {
            // Create default settings for this variable
            settings = new VariableSettings();
            _variableSettings[variableName] = settings;
        }

        return settings;
    }

    /// <summary>
    /// Set the initial guess value for a variable
    /// </summary>
    public void SetGuessValue(string variableName, double guessValue)
    {
        var settings = GetVariableSettings(variableName);
        settings.GuessValue = guessValue;
        settings.HasGuessValue = true;
    }

    /// <summary>
    /// Set bounds for a variable
    /// </summary>
    public void SetBounds(string variableName, double? lowerBound, double? upperBound)
    {
        var settings = GetVariableSettings(variableName);

        if (lowerBound.HasValue)
        {
            settings.LowerBound = lowerBound.Value;
            settings.HasLowerBound = true;
        }

        if (upperBound.HasValue)
        {
            settings.UpperBound = upperBound.Value;
            settings.HasUpperBound = true;
        }
    }

    /// <summary>
    /// Check if a variable has an initial guess value set
    /// </summary>
    public bool HasGuessValue(string variableName)
    {
        return _variableSettings.TryGetValue(variableName, out var settings) && settings.HasGuessValue;
    }

    /// <summary>
    /// Get the initial guess value for a variable
    /// </summary>
    public double GetGuessValue(string variableName, double defaultValue = 1.0)
    {
        if (_variableSettings.TryGetValue(variableName, out var settings) && settings.HasGuessValue)
        {
            return settings.GuessValue;
        }

        return defaultValue;
    }
}

/// <summary>
/// Settings for a specific variable in the solver
/// </summary>
public class VariableSettings
{
    // Initial guess value
    public double GuessValue { get; set; } = 1.0;
    public bool HasGuessValue { get; set; } = false;

    // Bounds
    public double LowerBound { get; set; } = double.MinValue;
    public bool HasLowerBound { get; set; } = false;

    public double UpperBound { get; set; } = double.MaxValue;
    public bool HasUpperBound { get; set; } = false;
}
