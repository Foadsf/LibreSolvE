// LibreSolvE.Core/Evaluation/VariableStore.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

// Simple symbol table for now
public class VariableStore
{
    // Store current values. EES variables are case-insensitive.
    private readonly Dictionary<string, double> _variables =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    // Track which variables are explicitly set vs implicitly created
    private readonly HashSet<string> _explicitVariables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // TODO: Add storage for guess values, bounds, units later

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

        // For evaluation, let's create implicit variables with non-zero defaults to avoid div/0
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
            Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
            hasExplicit = true;
        }
        if (!hasExplicit) Console.WriteLine("  (none)");

        // Then print implicitly created variables
        Console.WriteLine("Implicitly created variables:");
        bool hasImplicit = false;
        foreach (var kvp in _variables.Where(kv => !_explicitVariables.Contains(kv.Key)).OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  {kvp.Key} = {kvp.Value} (default)");
            hasImplicit = true;
        }
        if (!hasImplicit) Console.WriteLine("  (none)");

        Console.WriteLine("----------------------");
    }
}
