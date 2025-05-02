// LibreSolvE.Core/Evaluation/VariableStore.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

// Extended variable store with guess values and units
public class VariableStore
{
    // Store current values. EES variables are case-insensitive.
    private readonly Dictionary<string, double> _variables =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    // Track which variables are explicitly set vs implicitly created
    private readonly HashSet<string> _explicitVariables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Store guess values for variables
    private readonly Dictionary<string, double> _guessValues =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    // Store units for variables (as strings for now)
    private readonly Dictionary<string, string> _units =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void SetVariable(string name, double value)
    {
        _variables[name] = value;
        _explicitVariables.Add(name);
        // Console.WriteLine($"Debug: Set {name} = {value}"); // Optional debug output
    }

    public double GetVariable(string name)
    {
        if (_variables.TryGetValue(name, out double value))
        {
            return value;
        }

        // For evaluation, check if there's a guess value
        if (_guessValues.TryGetValue(name, out double guessValue))
        {
            Console.WriteLine($"Warning: Variable '{name}' accessed before assignment. Using guess value {guessValue}.");
            _variables[name] = guessValue; // Use guess value
            return guessValue;
        }

        // Otherwise use non-zero default
        Console.WriteLine($"Warning: Variable '{name}' accessed before assignment. Using default 1.0.");
        _variables[name] = 1.0; // Assign non-zero default if accessed
        return 1.0;
    }

    public bool IsExplicitlySet(string name)
    {
        return _explicitVariables.Contains(name);
    }

    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name);
    }

    // --- Guess Value Methods ---

    public void SetGuessValue(string name, double value)
    {
        _guessValues[name] = value;
    }

    public bool HasGuessValue(string name)
    {
        return _guessValues.ContainsKey(name);
    }

    public double GetGuessValue(string name, double defaultValue = 1.0)
    {
        if (_guessValues.TryGetValue(name, out double value))
        {
            return value;
        }
        return defaultValue;
    }

    // --- Unit Methods ---

    public void SetUnit(string name, string? unit)
    {
        if (!string.IsNullOrEmpty(unit))
        {
            _units[name] = unit;
        }
    }

    public bool HasUnit(string name)
    {
        return _units.ContainsKey(name);
    }

    public string GetUnit(string name)
    {
        return _units.TryGetValue(name, out string? unit) ? unit : string.Empty;
    }

    public IEnumerable<string> GetAllVariableNames()
    {
        return _variables.Keys;
    }

    public IEnumerable<string> GetImplicitVariableNames()
    {
        return _variables.Keys.Where(name => !_explicitVariables.Contains(name));
    }

    public void PrintVariables()
    {
        Console.WriteLine("--- Variable Store ---");
        if (_variables.Count == 0)
        {
            Console.WriteLine("(empty)");
            return;
        }

        // First print explicitly set variables
        Console.WriteLine("Explicitly set variables:");
        bool hasExplicit = false;
        foreach (var kvp in _variables.Where(kv => _explicitVariables.Contains(kv.Key)).OrderBy(kv => kv.Key))
        {
            var unit = HasUnit(kvp.Key) ? $" [{GetUnit(kvp.Key)}]" : "";
            Console.WriteLine($"  {kvp.Key} = {kvp.Value}{unit}");
            hasExplicit = true;
        }
        if (!hasExplicit) Console.WriteLine("  (none)");

        // Then print implicitly created variables
        Console.WriteLine("Implicitly created variables:");
        bool hasImplicit = false;
        foreach (var kvp in _variables.Where(kv => !_explicitVariables.Contains(kv.Key)).OrderBy(kv => kv.Key))
        {
            var unit = HasUnit(kvp.Key) ? $" [{GetUnit(kvp.Key)}]" : "";
            var source = HasGuessValue(kvp.Key) ? "guess" : "default";
            Console.WriteLine($"  {kvp.Key} = {kvp.Value}{unit} ({source})");
            hasImplicit = true;
        }
        if (!hasImplicit) Console.WriteLine("  (none)");

        Console.WriteLine("----------------------");
    }
}
