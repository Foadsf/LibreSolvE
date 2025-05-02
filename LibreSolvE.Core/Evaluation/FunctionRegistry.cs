// LibreSolvE.Core/Evaluation/FunctionRegistry.cs
using System;
using System.Collections.Generic;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Registry for built-in and user-defined functions
/// </summary>
public class FunctionRegistry
{
    // Delegate type for function implementations
    public delegate double FunctionDelegate(double[] args);

    // Dictionary of registered functions (case-insensitive)
    private readonly Dictionary<string, FunctionInfo> _functions =
        new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

    public FunctionRegistry()
    {
        RegisterBuiltInFunctions();
    }

    /// <summary>
    /// Information about a registered function
    /// </summary>
    public class FunctionInfo
    {
        public string Name { get; }
        public int MinArgs { get; }
        public int MaxArgs { get; } // -1 means variable number of arguments
        public FunctionDelegate Implementation { get; }

        public FunctionInfo(string name, int minArgs, int maxArgs, FunctionDelegate implementation)
        {
            Name = name;
            MinArgs = minArgs;
            MaxArgs = maxArgs;
            Implementation = implementation;
        }
    }

    /// <summary>
    /// Register a function with the registry
    /// </summary>
    public void RegisterFunction(string name, int minArgs, int maxArgs, FunctionDelegate implementation)
    {
        _functions[name] = new FunctionInfo(name, minArgs, maxArgs, implementation);
    }

    /// <summary>
    /// Check if a function is registered
    /// </summary>
    public bool HasFunction(string name)
    {
        return _functions.ContainsKey(name);
    }

    /// <summary>
    /// Get information about a registered function
    /// </summary>
    public FunctionInfo GetFunctionInfo(string name)
    {
        if (_functions.TryGetValue(name, out var info))
        {
            return info;
        }

        throw new KeyNotFoundException($"Function '{name}' is not registered");
    }

    /// <summary>
    /// Evaluate a function with the given arguments
    /// </summary>
    public double EvaluateFunction(string name, double[] args)
    {
        var info = GetFunctionInfo(name);

        // Validate argument count
        if (args.Length < info.MinArgs)
        {
            throw new ArgumentException($"Function '{name}' requires at least {info.MinArgs} arguments, but only {args.Length} were provided");
        }

        if (info.MaxArgs >= 0 && args.Length > info.MaxArgs)
        {
            throw new ArgumentException($"Function '{name}' accepts at most {info.MaxArgs} arguments, but {args.Length} were provided");
        }

        return info.Implementation(args);
    }

    /// <summary>
    /// Register all built-in mathematical functions
    /// </summary>
    private void RegisterBuiltInFunctions()
    {
        // Trigonometric functions
        RegisterFunction("SIN", 1, 1, args => Math.Sin(args[0]));
        RegisterFunction("COS", 1, 1, args => Math.Cos(args[0]));
        RegisterFunction("TAN", 1, 1, args => Math.Tan(args[0]));
        RegisterFunction("ASIN", 1, 1, args => Math.Asin(args[0]));
        RegisterFunction("ACOS", 1, 1, args => Math.Acos(args[0]));
        RegisterFunction("ATAN", 1, 1, args => Math.Atan(args[0]));
        RegisterFunction("ATAN2", 2, 2, args => Math.Atan2(args[0], args[1]));

        // Hyperbolic functions
        RegisterFunction("SINH", 1, 1, args => Math.Sinh(args[0]));
        RegisterFunction("COSH", 1, 1, args => Math.Cosh(args[0]));
        RegisterFunction("TANH", 1, 1, args => Math.Tanh(args[0]));

        // Exponential and logarithmic functions
        RegisterFunction("EXP", 1, 1, args => Math.Exp(args[0]));
        RegisterFunction("LOG", 1, 1, args => Math.Log(args[0]));      // Natural logarithm (base e)
        RegisterFunction("LOG10", 1, 1, args => Math.Log10(args[0]));  // Base 10 logarithm
        RegisterFunction("LN", 1, 1, args => Math.Log(args[0]));       // Alias for LOG

        // Power functions
        RegisterFunction("SQRT", 1, 1, args => Math.Sqrt(args[0]));
        RegisterFunction("POW", 2, 2, args => Math.Pow(args[0], args[1]));

        // Rounding functions
        RegisterFunction("ABS", 1, 1, args => Math.Abs(args[0]));
        RegisterFunction("CEIL", 1, 1, args => Math.Ceiling(args[0]));
        RegisterFunction("FLOOR", 1, 1, args => Math.Floor(args[0]));
        RegisterFunction("ROUND", 1, 2, args =>
            args.Length > 1
                ? Math.Round(args[0], (int)args[1])
                : Math.Round(args[0]));

        // Min/Max functions
        RegisterFunction("MIN", 2, -1, args => args.Min());
        RegisterFunction("MAX", 2, -1, args => args.Max());

        // Other functions
        RegisterFunction("IF", 3, 3, args => args[0] != 0 ? args[1] : args[2]);
    }
}
