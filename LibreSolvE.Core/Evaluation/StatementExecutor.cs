// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly SolverSettings _solverSettings;
    private readonly ExpressionEvaluatorVisitor _expressionEvaluator;
    public List<EquationNode> EquationsToSolve { get; } = new List<EquationNode>();

    // fields to hold ODE solver settings
    private bool _varyStepSize = true;
    private int _minSteps = 5;
    private int _maxSteps = 2000;
    private double _reduceThreshold = 1e-1;
    private double _increaseThreshold = 1e-3;

    // integral table data structure
    private Dictionary<string, List<double>> _integralTable = new Dictionary<string, List<double>>();
    private string _integralTableVarName = string.Empty;
    private double _integralTableStepSize = 0.0;
    private List<string> _integralTableColumns = new List<string>();


    // Updated constructor
    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry, SolverSettings solverSettings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _solverSettings = solverSettings ?? new SolverSettings();
        // WarningsAsErrors = false: Allow undefined vars during initial scan/assignment execution
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, false);
    }

    // Helper to check if an expression can be evaluated to a constant *now*
    private bool IsConstantValue(ExpressionNode node, out double value)
    {
        value = 0;
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            value = _expressionEvaluator.Evaluate(node);
            // It's constant if evaluation succeeded AND no undefined variables were hit
            return _expressionEvaluator.GetUndefinedVariableCount() == 0;
        }
        catch
        {
            return false; // Evaluation failed (e.g., div by zero) or feature not implemented
        }
    }

    public void Execute(EesFileNode fileNode)
    {
        Console.WriteLine("--- Pre-processing Statements ---");
        EquationsToSolve.Clear();
        var potentialAssignments = new List<EquationNode>();
        var otherEquations = new List<EquationNode>();
        var directives = new List<DirectiveNode>();

        // First pass: collect directives
        foreach (var statement in fileNode.Statements)
        {
            if (statement is DirectiveNode directive)
            {
                directives.Add(directive);
                ProcessDirective(directive);
            }
        }

        // Second pass: process assignments and equations
        foreach (var statement in fileNode.Statements)
        {
            if (statement is AssignmentNode explicitAssign) // Handle := assignments directly
            {
                Console.WriteLine($"Debug: Found explicit assignment: {explicitAssign.Variable.Name}");
                ExecuteExplicitAssignment(explicitAssign); // Execute immediately
            }
            else if (statement is EquationNode eqNode)
            {
                // Check if it's of the form Var = ConstantValue
                if (eqNode.LeftHandSide is VariableNode varNode && IsConstantValue(eqNode.RightHandSide, out _))
                {
                    Console.WriteLine($"Debug: Found potential assignment equation: {eqNode}");
                    potentialAssignments.Add(eqNode);
                }
                // Check if it's ConstantValue = Var (less common but possible)
                else if (eqNode.RightHandSide is VariableNode varNodeRhs && IsConstantValue(eqNode.LeftHandSide, out _))
                {
                    Console.WriteLine($"Debug: Found potential assignment equation (reversed): {eqNode}");
                    // Treat as assignment Var = Const for processing
                    potentialAssignments.Add(new EquationNode(varNodeRhs, eqNode.LeftHandSide));
                }
                else
                {
                    Console.WriteLine($"Debug: Found potential equation to solve: {eqNode}");
                    otherEquations.Add(eqNode); // Likely needs solving
                }
            }
            // Skip DirectiveNodes here as they've already been processed
        }

        // Execute potential assignments (Var = ConstExpr)
        Console.WriteLine("--- Processing Potential Assignments ---");
        foreach (var eqNode in potentialAssignments)
        {
            // We already know RHS is constant, LHS is VariableNode from checks above
            var variableNode = (VariableNode)eqNode.LeftHandSide;
            if (IsConstantValue(eqNode.RightHandSide, out double value)) // Re-evaluate to get value
            {
                Console.WriteLine($"Assigning {variableNode.Name} = {value} (from equation)");
                _variableStore.SetVariable(variableNode.Name, value);
            }
            else
            {
                // Should not happen based on initial check, but handle defensively
                Console.WriteLine($"Warning: Could not evaluate RHS for potential assignment: {eqNode}. Treating as equation.");
                otherEquations.Add(eqNode);
            }
        }

        // Remaining equations are those that need the solver
        EquationsToSolve.AddRange(otherEquations);

        Console.WriteLine("--- Statement Processing Finished ---");
        Console.WriteLine($"--- Collected {EquationsToSolve.Count} equations for solver ---");
    }

    /// <summary>
    /// Process a directive node
    /// </summary>
    private void ProcessDirective(DirectiveNode directive)
    {
        string text = directive.DirectiveText;

        if (text.StartsWith("$IntegralTable", StringComparison.OrdinalIgnoreCase))
        {
            ProcessIntegralTableDirective(text);
        }
        else if (text.StartsWith("$IntegralAutoStep", StringComparison.OrdinalIgnoreCase))
        {
            ProcessIntegralAutoStepDirective(text);
        }
        else
        {
            Console.WriteLine($"Warning: Unknown directive {text}");
        }
    }

    // Executes explicit := assignments
    private void ExecuteExplicitAssignment(AssignmentNode assignNode)
    {
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            double value = _expressionEvaluator.Evaluate(assignNode.RightHandSide);
            Console.WriteLine($"Assigning {assignNode.Variable.Name} := {value} (explicit)");
            _variableStore.SetVariable(assignNode.Variable.Name, value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error evaluating explicit assignment for '{assignNode.Variable.Name}': {ex.Message}");
            // Decide whether to halt
        }
    }

    public bool SolveEquations()
    {
        if (EquationsToSolve.Count == 0)
        {
            Console.WriteLine("No equations identified for solver.");
            return true;
        }

        // Pass registry and settings to the solver
        var solver = new EquationSolver(_variableStore, _functionRegistry, EquationsToSolve, _solverSettings);
        bool success = solver.Solve();

        return success;
    }

    /// <summary>
    /// Handle $IntegralTable directive
    /// </summary>
    public void ProcessIntegralTableDirective(string directive)
    {
        // Clear any existing integral table
        _integralTable.Clear();
        _integralTableColumns.Clear();

        // Parse the directive
        // Format: $IntegralTable VarName[:Step], var1, var2, ...
        string tableSpec = directive.Substring("$IntegralTable".Length).Trim();

        // Extract variable name and step size
        int colonIndex = tableSpec.IndexOf(':');
        string varName;
        string stepSizeStr = null;

        if (colonIndex > 0)
        {
            varName = tableSpec.Substring(0, colonIndex).Trim();

            // Extract possible step size and columns
            int commaIndex = tableSpec.IndexOf(',', colonIndex);
            if (commaIndex > 0)
            {
                stepSizeStr = tableSpec.Substring(colonIndex + 1, commaIndex - colonIndex - 1).Trim();
                tableSpec = tableSpec.Substring(commaIndex + 1);
            }
            else
            {
                stepSizeStr = tableSpec.Substring(colonIndex + 1).Trim();
                tableSpec = string.Empty;
            }
        }
        else
        {
            int commaIndex = tableSpec.IndexOf(',');
            if (commaIndex > 0)
            {
                varName = tableSpec.Substring(0, commaIndex).Trim();
                tableSpec = tableSpec.Substring(commaIndex + 1);
            }
            else
            {
                varName = tableSpec.Trim();
                tableSpec = string.Empty;
            }
        }

        // Store the integration variable name
        _integralTableVarName = varName;

        // Process step size if provided
        if (!string.IsNullOrEmpty(stepSizeStr))
        {
            // Try to parse as a number or look up as a variable
            if (double.TryParse(stepSizeStr, out double step))
            {
                _integralTableStepSize = step;
            }
            else if (_variableStore.HasVariable(stepSizeStr))
            {
                _integralTableStepSize = _variableStore.GetVariable(stepSizeStr);
            }
            else
            {
                Console.WriteLine($"Warning: Cannot interpret '{stepSizeStr}' as step size in $IntegralTable directive");
                _integralTableStepSize = 0.0;
            }
        }

        // Process columns
        if (!string.IsNullOrEmpty(tableSpec))
        {
            string[] columns = tableSpec.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string column in columns)
            {
                _integralTableColumns.Add(column.Trim());
                _integralTable[column.Trim()] = new List<double>();
            }
        }

        // Always include the independent variable column
        if (!_integralTable.ContainsKey(_integralTableVarName))
        {
            _integralTable[_integralTableVarName] = new List<double>();
            _integralTableColumns.Insert(0, _integralTableVarName);
        }

        Console.WriteLine($"Created Integral Table with variable {_integralTableVarName} and columns: {string.Join(", ", _integralTableColumns)}");
    }

    /// <summary>
    /// Add a row to the integral table
    /// </summary>
    public void AddIntegralTableRow(double independentVarValue)
    {
        if (_integralTable.Count == 0 || _integralTableColumns.Count == 0)
        {
            return; // No integral table defined
        }

        // Add independent variable value
        _integralTable[_integralTableVarName].Add(independentVarValue);

        // Add other column values
        foreach (string column in _integralTableColumns)
        {
            if (column != _integralTableVarName)
            {
                if (_variableStore.HasVariable(column))
                {
                    _integralTable[column].Add(_variableStore.GetVariable(column));
                }
                else
                {
                    _integralTable[column].Add(0.0); // Default value if variable not found
                }
            }
        }
    }

    /// <summary>
    /// Print the integral table
    /// </summary>
    public void PrintIntegralTable()
    {
        if (_integralTable.Count == 0 || _integralTableColumns.Count == 0)
        {
            Console.WriteLine("No Integral Table available.");
            return;
        }

        // Print header
        Console.WriteLine("--- Integral Table ---");
        Console.WriteLine(string.Join("\t", _integralTableColumns));

        // Print rows
        int rowCount = _integralTable[_integralTableColumns[0]].Count;
        for (int i = 0; i < rowCount; i++)
        {
            string[] rowValues = new string[_integralTableColumns.Count];
            for (int j = 0; j < _integralTableColumns.Count; j++)
            {
                string columnName = _integralTableColumns[j];
                rowValues[j] = _integralTable[columnName][i].ToString("G6");
            }
            Console.WriteLine(string.Join("\t", rowValues));
        }

        Console.WriteLine("----------------------");
    }

    /// <summary>
    /// Get the integral table data
    /// </summary>
    public Dictionary<string, List<double>> GetIntegralTable()
    {
        return _integralTable;
    }

    /// <summary>
    /// Updates the integral table with data from an ODE solution
    /// </summary>
    public void UpdateIntegralTable(string independentVarName, string dependentVarName, List<double> times, List<double> values)
    {
        if (_integralTable.Count == 0 || !_integralTable.ContainsKey(independentVarName))
        {
            // No integral table defined or wrong independent variable
            return;
        }

        // Update the independent variable column
        _integralTable[independentVarName] = new List<double>(times);

        // Update the dependent variable column if it's in the table
        if (_integralTable.ContainsKey(dependentVarName))
        {
            _integralTable[dependentVarName] = new List<double>(values);
        }

        // Calculate other columns based on these values
        for (int i = 0; i < times.Count; i++)
        {
            // Set time and value for this step
            _variableStore.SetVariable(independentVarName, times[i]);
            _variableStore.SetVariable(dependentVarName, values[i]);

            // Update other columns in the integral table
            foreach (string column in _integralTableColumns)
            {
                if (column != independentVarName && column != dependentVarName &&
                    _variableStore.HasVariable(column))
                {
                    // Make sure the list has enough space
                    while (_integralTable[column].Count <= i)
                    {
                        _integralTable[column].Add(0.0);
                    }

                    // Update the value
                    _integralTable[column][i] = _variableStore.GetVariable(column);
                }
            }
        }
    }



    /// <summary>
    /// Handle $IntegralAutoStep directive
    /// </summary>
    public void ProcessIntegralAutoStepDirective(string directive)
    {
        // Method implementation here...
    }

    /// <summary>
    /// Configure an ODE solver with current auto-step settings
    /// </summary>
    public void ConfigureOdeSolver(OdeSolver solver)
    {
        // Method implementation here...
    }


}
