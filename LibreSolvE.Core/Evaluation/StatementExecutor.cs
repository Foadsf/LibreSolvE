// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Plotting; // Make sure this using is present
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly SolverSettings _solverSettings;
    private readonly ExpressionEvaluatorVisitor _expressionEvaluator;
    public List<EquationNode> EquationsToSolve { get; } = new List<EquationNode>();

    // --- Integration Specific Fields ---
    private readonly List<IntegrationTask> _pendingIntegrations = new List<IntegrationTask>();
    private Dictionary<string, List<double>> _integralTable = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
    private string _integralTableIndependentVarName = string.Empty;
    private double _integralTableOutputStepSize = 0.0; // <= 0 means record adaptive/all steps
    private List<string> _integralTableColumns = new List<string>();
    private double _lastRecordedTime = double.NegativeInfinity; // Track last recorded time for fixed step output

    // --- ODE AutoStep Settings ---
    private bool _varyStepSize = true; // Default: use adaptive steps internally in ODE solver
    private int _minSteps = 5;
    private int _maxSteps = 2000;
    private double _reduceThreshold = 1e-1; // Default from EES appendix example
    private double _increaseThreshold = 1e-3;// Default from EES appendix example

    // --- Plotting Fields ---
    private readonly PlottingService _plottingService = new PlottingService();
    private readonly List<PlotCommandNode> _pendingPlotCommands = new List<PlotCommandNode>();
    private readonly List<PlotData> _generatedPlots = new List<PlotData>();
    private bool _hasGeneratedPlots = false;

    public bool HasGeneratedPlots() => _hasGeneratedPlots;
    public List<PlotData> GetGeneratedPlots() => _generatedPlots;

    // Event for GUI/TUI to subscribe to plot creation
    public event EventHandler<PlotData>? PlotCreated
    {
        add { _plottingService.PlotCreated += value; }
        remove { _plottingService.PlotCreated -= value; }
    }

    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry, SolverSettings solverSettings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _solverSettings = solverSettings ?? new SolverSettings();
        // Pass 'this' executor to the evaluator so it can access integral results etc. if needed in future
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, this, false);
    }

    private bool IsConstantValue(ExpressionNode node, out double value)
    {
        // This logic remains the same
        value = 0;
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            value = _expressionEvaluator.Evaluate(node);
            return _expressionEvaluator.GetUndefinedVariableCount() == 0;
        }
        catch { return false; }
    }

    public void Execute(EesFileNode fileNode)
    {
        Console.WriteLine("--- Pre-processing Statements ---");
        EquationsToSolve.Clear();
        _pendingIntegrations.Clear();
        _pendingPlotCommands.Clear();
        _generatedPlots.Clear(); // Clear plots from previous runs
        _hasGeneratedPlots = false;

        var potentialAssignments = new List<EquationNode>();
        var otherEquations = new List<EquationNode>();
        var directives = new List<DirectiveNode>();

        // Pass 1: Process Directives and collect Plot commands
        foreach (var statement in fileNode.Statements)
        {
            if (statement is DirectiveNode directive)
            {
                directives.Add(directive);
                ProcessDirective(directive); // Process directives immediately
            }
            else if (statement is PlotCommandNode plotCmd)
            {
                _pendingPlotCommands.Add(plotCmd); // Store plots for later
            }
        }

        // Pass 2: Process Assignments, classify Equations, identify Integrals
        Console.WriteLine("--- Classifying Statements ---");
        foreach (var statement in fileNode.Statements)
        {
            if (statement is AssignmentNode explicitAssign) // Handle :=
            {
                Console.WriteLine($"Executing explicit assignment: {explicitAssign.Variable.Name}");
                ExecuteExplicitAssignment(explicitAssign);
            }
            else if (statement is EquationNode eqNode) // Handle =
            {
                // Case 1: LHS is Var, RHS is INTEGRAL(...) -> Integration Task
                if (eqNode.LeftHandSide is VariableNode lhsVar &&
                    eqNode.RightHandSide is FunctionCallNode rhsFunc &&
                    string.Equals(rhsFunc.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Identified Integration Task for: {lhsVar.Name}");
                    _pendingIntegrations.Add(new IntegrationTask(lhsVar, rhsFunc));
                }
                // Case 2: LHS is Var, RHS is constant -> Potential Assignment
                else if (eqNode.LeftHandSide is VariableNode varNodeLhs && IsConstantValue(eqNode.RightHandSide, out _))
                {
                    Console.WriteLine($"Identified potential assignment equation: {eqNode}");
                    potentialAssignments.Add(eqNode);
                }
                // Case 3: LHS is constant, RHS is Var -> Potential Assignment (reversed)
                else if (eqNode.RightHandSide is VariableNode varNodeRhs && IsConstantValue(eqNode.LeftHandSide, out _))
                {
                    Console.WriteLine($"Identified potential assignment equation (reversed): {eqNode}");
                    potentialAssignments.Add(new EquationNode(varNodeRhs, eqNode.LeftHandSide)); // Store normalized form
                }
                // Case 4: Other equations -> Needs Solver
                else
                {
                    Console.WriteLine($"Identified equation to solve: {eqNode}");
                    otherEquations.Add(eqNode);
                }
            }
            // Ignore Directives and Plot Commands in this pass
        }

        // Execute potential assignments (Var = ConstExpr)
        Console.WriteLine("--- Processing Potential Assignments ---");
        foreach (var eqNode in potentialAssignments)
        {
            var variableNode = (VariableNode)eqNode.LeftHandSide;
            if (IsConstantValue(eqNode.RightHandSide, out double value)) // Re-evaluate
            {
                Console.WriteLine($"Assigning {variableNode.Name} = {value} (from equation)");
                _variableStore.SetVariable(variableNode.Name, value); // Explicitly set
            }
            else
            {
                Console.WriteLine($"Warning: Treating '{eqNode}' as equation as RHS is no longer constant.");
                otherEquations.Add(eqNode);
            }
        }
        Console.WriteLine("\n--- Variable Store State After Assignments ---");
        _variableStore.PrintVariables();

        // --- Integration Phase ---
        Console.WriteLine("\n--- Integration Phase ---");
        if (_pendingIntegrations.Count > 0)
        {
            // TODO: Handle dependencies *between* integrals if needed later
            foreach (var task in _pendingIntegrations)
            {
                Console.WriteLine($"Executing Integral for: {task.TargetVariable.Name}");
                ExecuteIntegralTask(task);
            }
            Console.WriteLine("--- Integration Phase Finished ---");

            // Print the integral table *after* integration is done
            PrintIntegralTable();
        }
        else
        {
            Console.WriteLine("No integral tasks found.");
        }


        // --- Algebraic Solve Phase ---
        Console.WriteLine("\n--- Algebraic Solving Phase ---");
        EquationsToSolve.AddRange(otherEquations);
        bool solveSuccess = SolveEquations(); // Solve remaining algebraic equations

        if (!solveSuccess && EquationsToSolve.Count > 0)
        {
            Console.Error.WriteLine("Solver failed for remaining algebraic equations.");
            // Optionally halt or continue based on a flag
        }

        // --- Plotting Phase ---
        Console.WriteLine("\n--- Plotting Phase ---");
        if (_pendingPlotCommands.Count > 0)
        {
            if (_integralTable.Count > 0) // Check if we have *any* table data
            {
                foreach (var plotCmd in _pendingPlotCommands)
                {
                    ProcessPlotCommand(plotCmd); // Process plots using populated integral table(s)
                }
            }
            else
            {
                Console.WriteLine("Skipping plots as no integral table data was generated.");
            }
        }
        else
        {
            Console.WriteLine("No plot commands found.");
        }


        Console.WriteLine("\n--- Statement Execution Finished ---");
    }

    // Executes explicit := assignments
    private void ExecuteExplicitAssignment(AssignmentNode assignNode)
    {
        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            double value = _expressionEvaluator.Evaluate(assignNode.RightHandSide);
            // Console.WriteLine($"Assigning {assignNode.Variable.Name} := {value} (explicit)");
            _variableStore.SetVariable(assignNode.Variable.Name, value);
        }
        catch (Exception ex)
        {
            // Wrap error with more context
            throw new InvalidOperationException($"Error evaluating explicit assignment for '{assignNode.Variable.Name}': {ex.Message}", ex);
        }
    }

    // Executes a specific INTEGRAL task
    private void ExecuteIntegralTask(IntegrationTask task)
    {
        var integralCall = task.IntegralCall;
        if (integralCall.Arguments.Count < 3 || integralCall.Arguments.Count > 5)
        {
            throw new ArgumentException($"Function '{integralCall.FunctionName}' requires 3-5 arguments (IntegrandVarName, IndependentVarName, LowerLimit, UpperLimit, [StepSize])");
        }

        // Extract argument nodes
        if (integralCall.Arguments[0] is not VariableNode integrandVarNode)
            throw new ArgumentException($"Argument 1 (Integrand) for '{integralCall.FunctionName}' must be a variable name representing the derivative.");
        if (integralCall.Arguments[1] is not VariableNode independentVarNode)
            throw new ArgumentException($"Argument 2 (Integration Variable) for '{integralCall.FunctionName}' must be a variable name.");

        string integrandName = integrandVarNode.Name;
        string independentName = independentVarNode.Name;
        string dependentName = task.TargetVariable.Name; // Variable receiving the result

        // Evaluate limits
        double lowerLimit = _expressionEvaluator.Evaluate(integralCall.Arguments[2]);
        double upperLimit = _expressionEvaluator.Evaluate(integralCall.Arguments[3]);

        // Evaluate optional step size (for fixed step integration)
        double stepSize = 0.0; // Default to adaptive
        if (integralCall.Arguments.Count >= 5)
        {
            stepSize = _expressionEvaluator.Evaluate(integralCall.Arguments[4]);
            if (stepSize <= 0)
            {
                Console.WriteLine($"Warning: Non-positive StepSize ({stepSize}) provided for INTEGRAL. Using adaptive step size.");
                stepSize = 0.0; // Revert to adaptive
            }
        }

        // Check if this integral populates the defined $IntegralTable
        if (!string.IsNullOrEmpty(_integralTableIndependentVarName) &&
            !string.Equals(_integralTableIndependentVarName, independentName, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Warning: $IntegralTable defined for '{_integralTableIndependentVarName}' but INTEGRAL uses '{independentName}'. Table will not be populated by this integral.");
            // Reset table tracking for this integral if it's not the target
            // This part needs better logic if multiple tables are allowed later
        }
        else if (!string.IsNullOrEmpty(_integralTableIndependentVarName))
        {
            // Reset state for populating the table
            _integralTable.Clear(); // Clear previous data
            _integralTable[_integralTableIndependentVarName] = new List<double>(); // Re-add independent var column
            foreach (var col in _integralTableColumns.Where(c => c != _integralTableIndependentVarName))
            {
                _integralTable[col] = new List<double>(); // Re-add other requested columns
            }
            _lastRecordedTime = double.NegativeInfinity;
            Console.WriteLine($"Integral for '{dependentName}' will populate Integral Table for '{independentName}'.");
        }


        // Create and configure ODE solver
        var odeSolver = new OdeSolver(
            this, // Pass executor reference
            _variableStore,
            _functionRegistry,
            integrandName,
            dependentName,
            independentName,
            lowerLimit,
            upperLimit,
            stepSize); // Pass 0 for adaptive

        // Configure adaptive settings from directives/defaults
        odeSolver.ConfigureAdaptiveStepSize(
            _varyStepSize, _minSteps, _maxSteps, _reduceThreshold, _increaseThreshold);

        // Solve the ODE
        double finalValue = odeSolver.Solve();

        // Store the final result in the target variable
        Console.WriteLine($"Integral result: {dependentName} = {finalValue}");
        _variableStore.SetVariable(dependentName, finalValue); // Set as explicit result
    }

    // *** NEW: Callback method for OdeSolver ***
    /// <summary>
    /// Records a step from the ODE solver into the Integral Table if one is defined
    /// and the step matches the output criteria.
    /// </summary>
    /// <param name="independentVarValue">Current value of the independent variable (e.g., time).</param>
    /// <param name="dependentValue">Current value of the dependent variable being integrated.</param>
    /// <param name="dependentVarName">The name of the dependent variable.</param>
    /// <param name="forceRecord">If true, record regardless of step size criteria (used for endpoint).</param>
    public void RecordIntegralStep(double independentVarValue, double dependentValue, string independentVarName, string dependentVarName, bool forceRecord = false)
    {
        // Check if an integral table is defined and if the independent variable matches
        if (string.IsNullOrEmpty(_integralTableIndependentVarName) ||
            !string.Equals(_integralTableIndependentVarName, independentVarName, StringComparison.OrdinalIgnoreCase)) // Compare with the parameter passed in
        {
            return; // No suitable table defined for this integration
        }

        bool shouldRecord = false;
        if (forceRecord)
        {
            shouldRecord = true;
        }
        else if (_integralTableOutputStepSize <= 0) // Record every step if step size is adaptive/zero
        {
            shouldRecord = true;
        }
        else // Check if step size interval has been met
        {
            // Handle floating point comparisons carefully
            if (independentVarValue >= _lastRecordedTime + _integralTableOutputStepSize - 1e-9) // Allow for small tolerance
            {
                shouldRecord = true;
                _lastRecordedTime += _integralTableOutputStepSize; // Prepare for next recording point
                                                                   // Adjust lastRecordedTime if needed to exactly match independentVarValue if very close? Might not be necessary.
                if (Math.Abs(_lastRecordedTime - independentVarValue) > _integralTableOutputStepSize * 0.5 && independentVarValue > _lastRecordedTime)
                {
                    // If we skipped multiple steps, maybe adjust? Or just record current. Let's just record current.
                    _lastRecordedTime = Math.Floor(independentVarValue / _integralTableOutputStepSize) * _integralTableOutputStepSize; // Try to align
                }
            }
        }
        // Special case: always record the very first point (t=lowerLimit)
        if (_integralTable[_integralTableIndependentVarName].Count == 0)
        {
            shouldRecord = true;
            _lastRecordedTime = independentVarValue; // Initialize recording time
        }


        if (shouldRecord)
        {
            // Update variable store temporarily to evaluate other variables
            double originalIndepValue = _variableStore.GetVariable(_integralTableIndependentVarName);
            double originalDepValue = _variableStore.HasVariable(dependentVarName) ? _variableStore.GetVariable(dependentVarName) : double.NaN;

            _variableStore.SetVariable(_integralTableIndependentVarName, independentVarValue);
            _variableStore.SetVariable(dependentVarName, dependentValue);

            // Evaluate and store all requested columns
            foreach (string columnName in _integralTableColumns)
            {
                try
                {
                    // Get the current value (it's already set if it's the indep/dep var)
                    double value = _variableStore.GetVariable(columnName);
                    // Add to the list for this column
                    _integralTable[columnName].Add(value);
                }
                catch (Exception ex)
                {
                    // Variable might not be calculable at this intermediate step
                    Console.WriteLine($"Warning: Could not evaluate '{columnName}' for Integral Table at {_integralTableIndependentVarName}={independentVarValue}. Storing NaN. Error: {ex.Message}");
                    _integralTable[columnName].Add(double.NaN); // Store NaN if evaluation fails
                }
            }

            // Restore original values (might not be strictly necessary depending on solver usage)
            _variableStore.SetVariable(_integralTableIndependentVarName, originalIndepValue);
            if (!double.IsNaN(originalDepValue)) _variableStore.SetVariable(dependentVarName, originalDepValue);

        }
    }


    // --- Remaining methods (ProcessDirective, SolveEquations, PrintIntegralTable, etc.) ---

    private void ProcessDirective(DirectiveNode directive)
    {
        string text = directive.DirectiveText.Trim();
        string directiveName;
        string directiveArgs;

        // Find the first space to separate directive name from args
        int firstSpace = text.IndexOfAny(new[] { ' ', '\t' });
        if (firstSpace > 0)
        {
            directiveName = text.Substring(0, firstSpace);
            directiveArgs = text.Substring(firstSpace + 1).Trim();
        }
        else
        {
            directiveName = text;
            directiveArgs = string.Empty;
        }


        if (directiveName.Equals("$IntegralTable", StringComparison.OrdinalIgnoreCase))
        {
            ProcessIntegralTableDirectiveArgs(directiveArgs);
        }
        else if (directiveName.Equals("$IntegralAutoStep", StringComparison.OrdinalIgnoreCase))
        {
            ProcessIntegralAutoStepDirectiveArgs(directiveArgs);
        }
        // Add other directives like $Warnings, $Complex, etc. here
        else
        {
            Console.WriteLine($"Warning: Unknown directive encountered: {directiveName}");
        }
    }

    private void ProcessIntegralTableDirectiveArgs(string args)
    {
        _integralTable.Clear();
        _integralTableColumns.Clear();
        _integralTableIndependentVarName = string.Empty;
        _integralTableOutputStepSize = 0.0; // Default: record adaptive steps

        // Format: VarName[:Step], col1, col2, ...
        string[] parts = args.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine("Warning: $IntegralTable directive has no arguments.");
            return;
        }

        // First part is IndependentVar[:Step]
        string firstPart = parts[0];
        string stepStr = "";
        int colonIndex = firstPart.IndexOf(':');
        if (colonIndex != -1)
        {
            _integralTableIndependentVarName = firstPart.Substring(0, colonIndex).Trim();
            stepStr = firstPart.Substring(colonIndex + 1).Trim();
        }
        else
        {
            _integralTableIndependentVarName = firstPart.Trim();
        }

        if (string.IsNullOrEmpty(_integralTableIndependentVarName))
        {
            Console.WriteLine("Warning: Invalid $IntegralTable format - missing independent variable name.");
            return;
        }

        // Parse step size
        if (!string.IsNullOrEmpty(stepStr))
        {
            if (double.TryParse(stepStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double stepVal) && stepVal > 0)
            {
                _integralTableOutputStepSize = stepVal;
                Console.WriteLine($"Integral Table output step size set to: {_integralTableOutputStepSize}");
            }
            else
            {
                Console.WriteLine($"Warning: Invalid step size '{stepStr}' in $IntegralTable. Will record adaptive steps.");
                _integralTableOutputStepSize = 0.0;
            }
        }

        // Add columns
        _integralTableColumns.Add(_integralTableIndependentVarName);
        _integralTable[_integralTableIndependentVarName] = new List<double>();

        for (int i = 1; i < parts.Length; i++)
        {
            string colName = parts[i].Trim();
            if (!_integralTable.ContainsKey(colName))
            {
                _integralTableColumns.Add(colName);
                _integralTable[colName] = new List<double>();
            }
        }
        Console.WriteLine($"Integral Table Setup: Var='{_integralTableIndependentVarName}', Step={(_integralTableOutputStepSize > 0 ? _integralTableOutputStepSize.ToString() : "Adaptive")}, Columns='{string.Join(", ", _integralTableColumns)}'");
    }

    private void ProcessIntegralAutoStepDirectiveArgs(string args)
    {
        // Format: Vary=1 Min=5 Max=2000 Reduce=1e-1 Increase=1e-3
        string[] parts = args.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string[] kv = part.Split('=');
            if (kv.Length == 2)
            {
                string key = kv[0].Trim();
                string valueStr = kv[1].Trim();

                try
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "vary": _varyStepSize = (int.Parse(valueStr) != 0); break;
                        case "min": _minSteps = int.Parse(valueStr); break;
                        case "max": _maxSteps = int.Parse(valueStr); break;
                        case "reduce": _reduceThreshold = double.Parse(valueStr, CultureInfo.InvariantCulture); break;
                        case "increase": _increaseThreshold = double.Parse(valueStr, CultureInfo.InvariantCulture); break;
                        default: Console.WriteLine($"Warning: Unknown $IntegralAutoStep parameter: {key}"); break;
                    }
                }
                catch (FormatException)
                {
                    Console.WriteLine($"Warning: Could not parse value '{valueStr}' for $IntegralAutoStep parameter '{key}'.");
                }
            }
        }
        Console.WriteLine($"Integral AutoStep Settings: Vary={_varyStepSize}, Min={_minSteps}, Max={_maxSteps}, Reduce={_reduceThreshold}, Increase={_increaseThreshold}");
    }

    // This method remains largely the same
    public bool SolveEquations()
    {
        if (EquationsToSolve.Count == 0)
        {
            Console.WriteLine("No algebraic equations identified for solver.");
            return true; // No equations is considered success
        }

        // Pass registry and settings to the solver
        var solver = new EquationSolver(_variableStore, _functionRegistry, EquationsToSolve, _solverSettings);
        bool success = solver.Solve();

        return success;
    }

    public void PrintIntegralTable()
    {
        // This logic remains the same
        if (_integralTable.Count == 0 || _integralTableColumns.Count == 0 || !_integralTable.ContainsKey(_integralTableIndependentVarName) || _integralTable[_integralTableIndependentVarName].Count == 0)
        {
            Console.WriteLine("No Integral Table data available to print.");
            return;
        }

        Console.WriteLine("\n--- Integral Table ---");
        Console.WriteLine(string.Join("\t", _integralTableColumns.Select(c => c.PadRight(12)))); // Add padding

        int rowCount = _integralTable[_integralTableIndependentVarName].Count;
        for (int i = 0; i < rowCount; i++)
        {
            List<string> rowValues = new List<string>();
            foreach (string columnName in _integralTableColumns)
            {
                // Ensure the list for the column exists and has the required index
                if (_integralTable.TryGetValue(columnName, out var columnList) && i < columnList.Count)
                {
                    rowValues.Add(columnList[i].ToString("G6").PadRight(12)); // Add padding
                }
                else
                {
                    rowValues.Add("NaN".PadRight(12)); // Or some placeholder if data is missing
                }
            }
            Console.WriteLine(string.Join("\t", rowValues));
        }
        Console.WriteLine("----------------------");
    }

    private void ProcessPlotCommand(PlotCommandNode plotCmd)
    {
        // This logic remains largely the same
        try
        {
            // Use _integralTable as the data source
            if (_integralTable == null || _integralTable.Count == 0 || _integralTableColumns.Count == 0 || !_integralTable.ContainsKey(_integralTableIndependentVarName) || _integralTable[_integralTableIndependentVarName].Count == 0)
            {
                Console.WriteLine($"Warning: Skipping plot command '{plotCmd.CommandText}' as no Integral Table data is available.");
                return;
            }

            var plotData = _plottingService.CreatePlot(plotCmd.CommandText, _integralTable);

            if (plotData.Series.Count == 0 || plotData.Series.All(s => s.XValues.Count == 0))
            {
                Console.WriteLine($"Warning: No data points available for plot '{plotData.Settings.Title}'. Check variables in PLOT command and Integral Table.");
                return;
            }

            string fileName = $"plot_{plotData.Settings.Title.Replace(" ", "_").Replace(":", "_").Replace("/", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.svg";
            // Sanitize filename further if necessary
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            _plottingService.SaveToSvg(plotData, fileName);
            plotData.FilePath = fileName;
            _generatedPlots.Add(plotData);
            _hasGeneratedPlots = true;

            Console.WriteLine($"Plot '{plotData.Settings.Title}' saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing plot command '{plotCmd.CommandText}': {ex.Message}");
        }
    }

    public Dictionary<string, List<double>> GetIntegralTable()
    {
        return _integralTable;
    }

}
