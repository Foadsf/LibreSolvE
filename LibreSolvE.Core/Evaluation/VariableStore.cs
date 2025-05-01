// LibreSolvE.Core/Evaluation/VariableStore.cs
using System.Collections.Generic;

namespace LibreSolvE.Core.Evaluation;

// Simple symbol table for now
public class VariableStore
{
    // Store current values. EES variables are case-insensitive.
    private readonly Dictionary<string, double> _variables =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    // TODO: Add storage for guess values, bounds, units later

    public void SetVariable(string name, double value)
    {
        _variables[name] = value;
        // Console.WriteLine($"Debug: Set {name} = {value}"); // Optional debug output
    }

    public double GetVariable(string name)
    {
        if (_variables.TryGetValue(name, out double value))
        {
            return value;
        }

        // EES behavior: Undefined variables often default to 0 or cause errors later.
        // For evaluation, let's throw for now if not found during expression calc.
        // TODO: Handle default/guess values later in the solving phase.
        Console.WriteLine($"Warning: Variable '{name}' accessed before assignment. Using default 0.");
        // Or: throw new KeyNotFoundException($"Variable '{name}' has not been assigned a value.");
        _variables[name] = 0.0; // Assign default if accessed
        return 0.0;

    }

    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name);
    }

    public void PrintVariables()
    {
        Console.WriteLine("--- Variable Store ---");
        if (_variables.Count == 0)
        {
            Console.WriteLine("(empty)");
            return;
        }
        foreach (var kvp in _variables.OrderBy(kv => kv.Key)) // Order alphabetically
        {
            Console.WriteLine($"{kvp.Key} = {kvp.Value}");
        }
        Console.WriteLine("----------------------");
    }
}
