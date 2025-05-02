// LibreSolvE.Core/Evaluation/FunctionRegistry.cs
using System;
using System.Collections.Generic;
using System.Linq; // Required for Min/Max on arrays
using UnitsNet; // For unit conversion
using UnitsNet.Units; // For specific unit enums if needed

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Manages built-in and potentially user-defined functions available for evaluation.
/// </summary>
public class FunctionRegistry
{
    // Delegate type defining the signature for function implementations.
    // Takes an array of evaluated argument values, returns the function result.
    public delegate double FunctionDelegate(double[] args);

    // Dictionary storing registered functions. Uses case-insensitive keys for function names.
    private readonly Dictionary<string, FunctionInfo> _functions =
        new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the FunctionRegistry and registers built-in functions.
    /// </summary>
    public FunctionRegistry()
    {
        RegisterBuiltInFunctions();
    }

    /// <summary>
    /// Stores metadata about a registered function.
    /// </summary>
    public class FunctionInfo
    {
        public string Name { get; }
        public int MinArgs { get; }
        // MaxArgs = -1 indicates a variable number of arguments (like MIN, MAX).
        public int MaxArgs { get; }
        public FunctionDelegate Implementation { get; }

        public FunctionInfo(string name, int minArgs, int maxArgs, FunctionDelegate implementation)
        {
            Name = name;
            MinArgs = minArgs;
            MaxArgs = maxArgs;
            Implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
        }
    }

    /// <summary>
    /// Registers a function implementation with the registry.
    /// </summary>
    /// <param name="name">The case-insensitive name of the function (e.g., "SIN").</param>
    /// <param name="minArgs">The minimum number of arguments required.</param>
    /// <param name="maxArgs">The maximum number of arguments allowed (-1 for variable).</param>
    /// <param name="implementation">The delegate pointing to the function's C# implementation.</param>
    public void RegisterFunction(string name, int minArgs, int maxArgs, FunctionDelegate implementation)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Function name cannot be empty.", nameof(name));
        if (minArgs < 0) throw new ArgumentOutOfRangeException(nameof(minArgs), "Minimum arguments cannot be negative.");
        if (maxArgs != -1 && maxArgs < minArgs) throw new ArgumentOutOfRangeException(nameof(maxArgs), "Maximum arguments cannot be less than minimum arguments.");

        _functions[name] = new FunctionInfo(name, minArgs, maxArgs, implementation);
        Console.WriteLine($"Debug: Registered function '{name}'");
    }

    /// <summary>
    /// Checks if a function with the given name is registered.
    /// </summary>
    /// <param name="name">The case-insensitive function name.</param>
    /// <returns>True if the function exists, false otherwise.</returns>
    public bool HasFunction(string name)
    {
        return _functions.ContainsKey(name);
    }

    /// <summary>
    /// Retrieves the metadata for a registered function.
    /// </summary>
    /// <param name="name">The case-insensitive function name.</param>
    /// <returns>The FunctionInfo object.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the function is not registered.</exception>
    public FunctionInfo GetFunctionInfo(string name)
    {
        if (_functions.TryGetValue(name, out var info))
        {
            return info;
        }
        throw new KeyNotFoundException($"Function '{name}' is not registered.");
    }

    /// <summary>
    /// Evaluates a registered function with the provided arguments.
    /// Includes argument count validation and exception wrapping.
    /// </summary>
    /// <param name="name">The case-insensitive function name.</param>
    /// <param name="args">An array of evaluated argument values.</param>
    /// <returns>The result of the function execution.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the function is not registered.</exception>
    /// <exception cref="ArgumentException">Thrown if the argument count is invalid.</exception>
    /// <exception cref="InvalidOperationException">Wraps errors occurring during function execution.</exception>
    public double EvaluateFunction(string name, double[] args)
    {
        var info = GetFunctionInfo(name); // Throws KeyNotFoundException if not found

        // Validate argument count
        int argCount = args?.Length ?? 0;
        if (argCount < info.MinArgs)
        {
            throw new ArgumentException($"Function '{name}' requires at least {info.MinArgs} arguments, but only {argCount} were provided.");
        }
        if (info.MaxArgs >= 0 && argCount > info.MaxArgs)
        {
            throw new ArgumentException($"Function '{name}' accepts at most {info.MaxArgs} arguments, but {argCount} were provided.");
        }

        // Execute the implementation
        try
        {
            return info.Implementation(args ?? Array.Empty<double>());
        }
        catch (UnitsNet.UnitNotFoundException unitEx) // Catch specific UnitsNet errors from CONVERT
        {
            // Re-throw as ArgumentException for better context for the user
            throw new ArgumentException($"Unit error evaluating function '{name}': {unitEx.Message}", unitEx);
        }
        catch (Exception ex)
        {
            // Wrap other exceptions for context
            throw new InvalidOperationException($"Error evaluating function '{name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Registers the standard built-in mathematical functions.
    /// </summary>
    private void RegisterBuiltInFunctions()
    {
        Console.WriteLine("Debug: Registering built-in functions...");

        // Constants (handled as variables usually, but could be functions)
        // RegisterFunction("PI", 0, 0, args => Math.PI);

        // Trigonometric (assuming radians for now, unit handling needed later)
        RegisterFunction("SIN", 1, 1, args => Math.Sin(args[0]));
        RegisterFunction("COS", 1, 1, args => Math.Cos(args[0]));
        RegisterFunction("TAN", 1, 1, args => Math.Tan(args[0]));
        RegisterFunction("ASIN", 1, 1, args => Math.Asin(args[0]));
        RegisterFunction("ACOS", 1, 1, args => Math.Acos(args[0]));
        RegisterFunction("ATAN", 1, 1, args => Math.Atan(args[0]));
        RegisterFunction("ATAN2", 2, 2, args => Math.Atan2(args[0], args[1])); // Note argument order: y, x

        // Hyperbolic
        RegisterFunction("SINH", 1, 1, args => Math.Sinh(args[0]));
        RegisterFunction("COSH", 1, 1, args => Math.Cosh(args[0]));
        RegisterFunction("TANH", 1, 1, args => Math.Tanh(args[0]));

        // Exponential and Logarithmic
        RegisterFunction("EXP", 1, 1, args => Math.Exp(args[0]));
        RegisterFunction("LOG", 1, 1, args => Math.Log(args[0]));      // Natural log (base e)
        RegisterFunction("LOG10", 1, 1, args => Math.Log10(args[0]));  // Base 10 log
        RegisterFunction("LN", 1, 1, args => Math.Log(args[0]));       // Alias for LOG

        // Power and Root
        RegisterFunction("SQRT", 1, 1, args =>
        {
            if (args[0] < 0) throw new ArgumentOutOfRangeException(nameof(args), "SQRT argument cannot be negative.");
            return Math.Sqrt(args[0]);
        });
        RegisterFunction("POW", 2, 2, args => Math.Pow(args[0], args[1])); // Base, Exponent

        // Rounding and Absolute Value
        RegisterFunction("ABS", 1, 1, args => Math.Abs(args[0]));
        RegisterFunction("CEIL", 1, 1, args => Math.Ceiling(args[0])); // Smallest integer >= arg
        RegisterFunction("FLOOR", 1, 1, args => Math.Floor(args[0]));   // Largest integer <= arg
        RegisterFunction("ROUND", 1, 2, args => // ROUND(value, [digits])
            args.Length > 1
                ? Math.Round(args[0], (int)args[1], MidpointRounding.AwayFromZero) // Specify rounding mode
                : Math.Round(args[0], MidpointRounding.AwayFromZero));
        RegisterFunction("TRUNC", 1, 1, args => Math.Truncate(args[0])); // Integer part towards zero

        // Min/Max (Variable arguments)
        RegisterFunction("MIN", 2, -1, args => args.Min()); // Use LINQ Min
        RegisterFunction("MAX", 2, -1, args => args.Max()); // Use LINQ Max

        // Conditional (Basic version)
        RegisterFunction("IF", 3, 3, args => args[0] != 0 ? args[1] : args[2]); // IF(cond, true_val, false_val)

        // Unit Conversion Placeholder (Requires string handling in evaluator)
        RegisterFunction("CONVERT", 2, 2, args =>
        {
            // This is conceptually what needs to happen, but requires the evaluator
            // to pass string arguments correctly. The current EvaluateFunction only takes doubles.
            throw new NotImplementedException("CONVERT function requires string arguments - evaluator needs modification.");
            // Example: return UnitsNet.UnitConverter.ConvertByName(1.0, stringArg1, stringArg2);
        });

        // Temperature Conversion Placeholder
        RegisterFunction("CONVERTTEMP", 3, 3, args =>
        {
            // Requires evaluator to pass string arguments for the first two parameters.
            throw new NotImplementedException("CONVERTTEMP function requires string arguments - evaluator needs modification.");
            // Example: return UnitsNet.Temperature.From(args[2], tempUnitEnum1).ToUnit(tempUnitEnum2).Value;
        });

        Console.WriteLine($"Debug: Finished registering {_functions.Count} built-in functions.");
    }
}
