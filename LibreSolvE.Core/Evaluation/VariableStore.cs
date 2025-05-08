// LibreSolvE.Core/Evaluation/VariableStore.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

public class VariableStore
{
    private readonly Dictionary<string, double> _variables =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitVariables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _guessValues =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    // *** NEW: Store units as strings ***
    private readonly Dictionary<string, string> _units =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _solvedVariables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void SetVariable(string name, double value)
    {
        _variables[name] = value;
        _explicitVariables.Add(name);
        _solvedVariables.Remove(name); // Explicit setting overrides solved status
    }

    // Method to be called by the solver
    public void SetSolvedVariable(string name, double value)
    {
        _variables[name] = value;
        _explicitVariables.Remove(name); // Ensure it's not marked as explicit
        _solvedVariables.Add(name);      // Mark as solved
    }

    public bool IsSolvedSet(string name) => _solvedVariables.Contains(name);

    public double GetVariable(string name)
    {
        if (_variables.TryGetValue(name, out double value))
        {
            return value;
        }
        if (_guessValues.TryGetValue(name, out double guessValue))
        {
            Console.WriteLine($"Warning: Variable '{name}' accessed before assignment. Using guess value {guessValue}.");
            _variables[name] = guessValue;
            return guessValue;
        }
        Console.WriteLine($"Warning: Variable '{name}' accessed before assignment. Using default 1.0.");
        _variables[name] = 1.0;
        return 1.0;
    }

    public bool IsExplicitlySet(string name) => _explicitVariables.Contains(name);
    public bool HasVariable(string name) => _variables.ContainsKey(name);
    public IEnumerable<string> GetAllVariableNames() => _variables.Keys;
    public IEnumerable<string> GetImplicitVariableNames() => _variables.Keys.Where(name => !_explicitVariables.Contains(name));

    // --- Guess Value Methods (Keep as before) ---
    public void SetGuessValue(string name, double value) => _guessValues[name] = value;
    public bool HasGuessValue(string name) => _guessValues.ContainsKey(name);
    public double GetGuessValue(string name, double defaultValue = 1.0) => _guessValues.TryGetValue(name, out double value) ? value : defaultValue;


    // *** NEW: Unit Methods ***
    public void SetUnit(string name, string? unit)
    {
        if (!string.IsNullOrWhiteSpace(unit))
        {
            Console.WriteLine($"Debug: Setting unit for '{name}' to '[{unit}]'");
            _units[name] = unit;
        }
    }

    public bool HasUnit(string name) => _units.ContainsKey(name);
    public string GetUnit(string name) => _units.TryGetValue(name, out string? unit) ? unit : string.Empty;
    // *** END NEW Unit Methods ***


    public void PrintVariables()
    {
        Console.WriteLine("--- Variable Store ---");
        if (_variables.Count == 0)
        {
            Console.WriteLine("(empty)");
            return;
        }
        var allVars = _variables.Keys.OrderBy(k => k);
        foreach (var varName in allVars)
        {
            double value = _variables[varName];
            string unitStr = GetUnit(varName);
            string unitDisplay = string.IsNullOrEmpty(unitStr) ? "" : $" [{unitStr}]";
            string source;

            if (IsExplicitlySet(varName)) source = "Explicit";
            else if (IsSolvedSet(varName)) source = "Solved"; // Check solved status
            else if (HasGuessValue(varName)) source = "Guess";
            else source = "Default";

            Console.WriteLine($"  {varName,-15} = {value,-20}{unitDisplay} ({source})");
        }
        Console.WriteLine("----------------------");
    }

    /// <summary>
    /// Forces re-evaluation of all equations with current variable values
    /// </summary>
    public void ResolveEquations()
    {
        // This implementation depends on how equations are stored and managed
        // A basic implementation might look like this:

        bool changed = true;
        int iterations = 0;
        int maxIterations = 100; // Prevent infinite loops

        // Keep resolving until no values change or we hit iteration limit
        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            // Store previous values for comparison
            var previousValues = new Dictionary<string, double>(_variables);

            // Re-evaluate all equations (if we had a list of equations)
            // This would typically be done by the StatementExecutor

            // For this implementation, we'll just check if values changed
            foreach (var varName in _variables.Keys.ToList())
            {
                if (_variables.TryGetValue(varName, out double currentValue) &&
                    previousValues.TryGetValue(varName, out double prevValue))
                {
                    if (Math.Abs(currentValue - prevValue) > 1e-10)
                    {
                        changed = true;
                        break;
                    }
                }
            }
        }

        if (iterations >= maxIterations)
        {
            Console.WriteLine("Warning: Maximum iterations reached in equation resolution");
        }
    }
}
