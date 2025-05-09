// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Plotting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

// No local using aliases here to avoid confusion

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    #region Fields
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly SolverSettings _solverSettings;
    private readonly ExpressionEvaluatorVisitor _expressionEvaluator;

    private readonly List<AssignmentNode> _explicitAssignments = new();
    private List<EquationNode> _potentialAssignments = new();
    private readonly List<EquationNode> _algebraicEquations = new();
    private readonly List<EquationNode> _odeStateEquations = new();
    private readonly List<AssignmentNode> _integralDefinitions = new();

    private readonly List<DirectiveNode> _directives = new();
    private readonly List<PlotCommandNode> _plotCommands = new();

    private bool _varyStepSize = true;
    private int _minSteps = 100;
    private int _maxSteps = 1000;
    private double _reduceThreshold = 0.001;
    private double _increaseThreshold = 0.00001;

    private readonly Dictionary<string, List<double>> _integralTable = new(StringComparer.OrdinalIgnoreCase);
    private string _integralTableIndVarName = string.Empty;
    private double _integralTableOutputStepSize = 0.0;
    private readonly List<string> _integralTableColumns = new();

    private PlottingService _plottingService = new();
    #endregion Fields

    #region Events
    public event EventHandler<PlotData>? PlotCreated
    {
        add { _plottingService.PlotCreated += value; }
        remove { _plottingService.PlotCreated -= value; }
    }
    #endregion Events

    #region Constructor
    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry, SolverSettings solverSettings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _solverSettings = solverSettings ?? new SolverSettings();
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, this, false);
    }
    #endregion Constructor

    #region Public Methods
    public void Execute(EesFileNode fileNode)
    {
        CategorizeStatements(fileNode);
        ProcessDirectives();
        ExecuteExplicitAssignments();
        ExecutePotentialAssignments();
        ExecuteIntegralDefinitions();
        PrintIntegralTable();
        ProcessPlotCommands();
        Console.WriteLine("--- Statement Processing Finished ---");
    }

    public bool SolveRemainingAlgebraicEquations()
    {
        Console.WriteLine("\n--- Attempting to Solve Remaining Algebraic Equations ---");
        if (_algebraicEquations.Count == 0)
        {
            Console.WriteLine("No algebraic equations identified for solver.");
            return true;
        }

        var algebraicVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eq in _algebraicEquations)
        {
            CollectVariablesFromExpression(eq.LeftHandSide, algebraicVars);
            CollectVariablesFromExpression(eq.RightHandSide, algebraicVars);
        }
        var unknowns = algebraicVars.Where(v => !_variableStore.IsExplicitlySet(v) && !_variableStore.IsSolvedSet(v)).ToList();

        Console.WriteLine($"Found {_algebraicEquations.Count} algebraic equations and {unknowns.Count} unknown variables for this block: {string.Join(", ", unknowns)}");

        // CHANGED LOGIC HERE: Handle case with no unknowns
        if (unknowns.Count == 0)
        {
            Console.WriteLine("All variables in equations are already known. Verifying consistency...");
            // Verify that the equations are consistent with current values
            bool allConsistent = true;
            foreach (var eq in _algebraicEquations)
            {
                double lhs = _expressionEvaluator.Evaluate(eq.LeftHandSide);
                double rhs = _expressionEvaluator.Evaluate(eq.RightHandSide);
                double diff = Math.Abs(lhs - rhs);
                if (diff > 1e-6)
                {
                    Console.WriteLine($"Inconsistency in equation: {eq}, LHS={lhs}, RHS={rhs}, diff={diff}");
                    allConsistent = false;
                }
            }
            return allConsistent;
        }

        if (_algebraicEquations.Count < unknowns.Count)
        {
            string errorMessage = $"There are {_algebraicEquations.Count} equations and {unknowns.Count} variables. The problem is underspecified and cannot be solved with the current algebraic set.";
            Console.Error.WriteLine($"Error: {errorMessage}");
            throw new InvalidOperationException(errorMessage);
        }

        var solver = new EquationSolver(_variableStore, _functionRegistry, _algebraicEquations, _solverSettings, unknowns);
        bool success = solver.Solve();

        if (success) Console.WriteLine("\n--- Algebraic Solver Phase Completed Successfully ---");
        else Console.Error.WriteLine("\n--- Algebraic Solver FAILED ---");
        return success;
    }
    #endregion Public Methods

    #region Statement Processing Logic
    private void CategorizeStatements(EesFileNode fileNode)
    {
        Console.WriteLine("--- Pre-processing Statements: Categorizing ---");
        _explicitAssignments.Clear(); _potentialAssignments.Clear(); _algebraicEquations.Clear();
        _odeStateEquations.Clear(); _integralDefinitions.Clear(); _directives.Clear(); _plotCommands.Clear();

        // First pass: collect all explicit directives, plots, integrals and direct assignments
        foreach (var statement in fileNode.Statements)
        {
            switch (statement)
            {
                case DirectiveNode directive:
                    _directives.Add(directive);
                    break;
                case PlotCommandNode plotCmd:
                    _plotCommands.Add(plotCmd);
                    break;
                case AssignmentNode assignNode:
                    _explicitAssignments.Add(assignNode);
                    break;
                case EquationNode eqNode:
                    if (eqNode.LeftHandSide is VariableNode dvn && eqNode.RightHandSide is FunctionCallNode rfcn &&
                        string.Equals(rfcn.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
                    {
                        _integralDefinitions.Add(new AssignmentNode(dvn, rfcn));
                    }
                    else if (IsDydtEquation(eqNode))
                    {
                        _odeStateEquations.Add(eqNode);
                    }
                    else if (eqNode.LeftHandSide is VariableNode lhsVar)
                    {
                        // By default, treat any equation with a variable on the left as a potential assignment
                        // We'll analyze dependencies later
                        _potentialAssignments.Add(eqNode);
                    }
                    else
                    {
                        // Non-assignment equations (complex LHS)
                        _algebraicEquations.Add(eqNode);
                    }
                    break;
            }
        }

        // Second pass: analyze dependencies and reorder potential assignments
        // This ensures assignments are processed in the correct order
        ReorderPotentialAssignments();

        Console.WriteLine($"Categorized: {_explicitAssignments.Count} explicit assignments, " +
                         $"{_potentialAssignments.Count} potential assignments, " +
                         $"{_algebraicEquations.Count} algebraic equations, " +
                         $"{_odeStateEquations.Count} ODE equations");
    }

    // Add a new method to analyze and reorder assignments based on dependencies

    private void ReorderPotentialAssignments()
    {
        // 1. Identify variables and their dependencies
        Dictionary<string, HashSet<string>> dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, EquationNode> variableDefinitions = new Dictionary<string, EquationNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var eqNode in _potentialAssignments)
        {
            if (eqNode.LeftHandSide is VariableNode lhsVar)
            {
                string varName = lhsVar.Name;
                variableDefinitions[varName] = eqNode;

                // Identify dependencies in the RHS
                if (!dependencies.ContainsKey(varName))
                {
                    dependencies[varName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                // Collect all variables from the RHS
                CollectVariablesFromExpression(eqNode.RightHandSide, dependencies[varName]);
            }
        }

        // 2. Find cycles and move cyclic dependencies to algebraic equations
        List<string> processedVars = new List<string>();
        HashSet<string> varsInCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var varName in variableDefinitions.Keys.ToList())
        {
            if (!processedVars.Contains(varName))
            {
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (IsCyclicUtil(varName, visited, recursionStack, dependencies))
                {
                    // This variable is part of a cycle
                    foreach (var cycleVar in recursionStack)
                    {
                        varsInCycles.Add(cycleVar);
                    }
                }

                processedVars.AddRange(visited);
            }
        }

        // 3. Sort the remaining variables by dependency and reassign assignment lists
        List<EquationNode> newPotentialAssignments = new List<EquationNode>();
        List<EquationNode> cyclicDependencyEquations = new List<EquationNode>();

        // Find all root variables (ones with no dependencies or only dependencies on explicit variables)
        HashSet<string> explicitVars = new HashSet<string>(_variableStore.GetAllVariableNames().Where(
            name => _variableStore.IsExplicitlySet(name)), StringComparer.OrdinalIgnoreCase);

        // Keep processing until all variables are handled
        while (variableDefinitions.Count > 0)
        {
            bool foundAssignment = false;

            // Find variables that only depend on already known variables
            foreach (var entry in variableDefinitions.ToList())
            {
                string varName = entry.Key;
                EquationNode eqNode = entry.Value;

                // Skip variables that are part of cycles
                if (varsInCycles.Contains(varName))
                {
                    cyclicDependencyEquations.Add(eqNode);
                    variableDefinitions.Remove(varName);
                    foundAssignment = true;
                    continue;
                }

                // Check if all dependencies are satisfied
                bool allDependenciesSatisfied = true;
                if (dependencies.TryGetValue(varName, out var varDeps))
                {
                    foreach (var dep in varDeps)
                    {
                        if (!explicitVars.Contains(dep) && !_variableStore.IsExplicitlySet(dep))
                        {
                            allDependenciesSatisfied = false;
                            break;
                        }
                    }
                }

                if (allDependenciesSatisfied)
                {
                    newPotentialAssignments.Add(eqNode);
                    explicitVars.Add(varName); // Now this variable is "known"
                    variableDefinitions.Remove(varName);
                    foundAssignment = true;
                }
            }

            // If we didn't find any assignments that can be processed, we have a deadlock
            if (!foundAssignment && variableDefinitions.Count > 0)
            {
                // Move all remaining assignments to algebraic equations
                foreach (var entry in variableDefinitions)
                {
                    cyclicDependencyEquations.Add(entry.Value);
                }
                break;
            }
        }

        // Update the assignment and equation lists
        _potentialAssignments = newPotentialAssignments;
        _algebraicEquations.AddRange(cyclicDependencyEquations);
    }

    // Helper method to detect cycles in the dependency graph
    private bool IsCyclicUtil(string varName, HashSet<string> visited, HashSet<string> recursionStack,
                             Dictionary<string, HashSet<string>> dependencies)
    {
        visited.Add(varName);
        recursionStack.Add(varName);

        if (dependencies.TryGetValue(varName, out var deps))
        {
            foreach (var dep in deps)
            {
                // Skip explicit variables - they don't cause cycles
                if (_variableStore.IsExplicitlySet(dep))
                    continue;

                // If dep is already visited and in recursion stack, we have a cycle
                if (recursionStack.Contains(dep))
                    return true;

                // If dep is not visited, explore it
                if (!visited.Contains(dep) && dependencies.ContainsKey(dep))
                {
                    if (IsCyclicUtil(dep, visited, recursionStack, dependencies))
                        return true;
                }
            }
        }

        // Remove from recursion stack
        recursionStack.Remove(varName);
        return false;
    }


    // Helper method to check for cyclic dependencies
    private bool HasCyclicDependency(string varName, Dictionary<string, HashSet<string>> dependencies,
                                    HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // If we've already visited this variable, we have a cycle
        if (visited.Contains(varName))
            return true;

        visited.Add(varName);

        // Check dependencies
        if (dependencies.TryGetValue(varName, out var deps))
        {
            foreach (var dep in deps)
            {
                // Skip self-dependency
                if (string.Equals(dep, varName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // If dependent variable has dependencies, check them recursively
                if (dependencies.ContainsKey(dep) && HasCyclicDependency(dep, dependencies, visited))
                    return true;
            }
        }

        visited.Remove(varName);
        return false;
    }

    // Helper method to check if an expression references a variable directly or indirectly
    private bool HasCircularReference(string varName, ExpressionNode node)
    {
        if (node is VariableNode vn && string.Equals(vn.Name, varName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (node is BinaryOperationNode binOp)
            return HasCircularReference(varName, binOp.Left) || HasCircularReference(varName, binOp.Right);

        if (node is FunctionCallNode funcNode)
            return funcNode.Arguments.Any(arg => HasCircularReference(varName, arg));

        return false;
    }

    // helper method to determine if an equation should be treated as a simple assignment
    private bool IsSimpleAssignmentForm(EquationNode eqNode)
    {
        // If the left side is a variable and right side doesn't reference it, it's a simple assignment form
        if (eqNode.LeftHandSide is VariableNode lvn)
        {
            var rhsVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectVariablesFromExpression(eqNode.RightHandSide, rhsVars);
            return !rhsVars.Contains(lvn.Name);
        }
        return false;
    }

    private void ProcessDirectives()
    {
        Console.WriteLine("--- Processing Directives ---");
        foreach (var directive in _directives)
        {
            string text = directive.DirectiveText.Trim();
            if (text.StartsWith("$IntegralTable", StringComparison.OrdinalIgnoreCase)) ProcessIntegralTableDirective(text);
            else if (text.StartsWith("$IntegralAutoStep", StringComparison.OrdinalIgnoreCase)) ProcessIntegralAutoStepDirective(text);
            else Console.WriteLine($"Warning: Unknown directive {text}");
        }
    }

    private void ExecuteExplicitAssignments()
    {
        Console.WriteLine("--- Executing Explicit Assignments (:=) ---");
        foreach (var assignNode in _explicitAssignments)
        {
            try
            {
                double value = _expressionEvaluator.Evaluate(assignNode.RightHandSide);
                _variableStore.SetVariable(assignNode.Variable.Name, value);
            }
            catch (Exception ex) { Console.WriteLine($"Error evaluating explicit assignment for '{assignNode.Variable.Name}': {ex.Message}"); }
        }
    }


    private void ExecutePotentialAssignments()
    {
        Console.WriteLine("--- Executing Potential Assignments (Var = ConstExpr) ---");

        foreach (var eqNode in _potentialAssignments)
        {
            if (eqNode.LeftHandSide is VariableNode lhsVar)
            {
                string varName = lhsVar.Name;
                double value;

                try
                {
                    // Since we've already sorted assignments by dependency,
                    // we can now evaluate them directly
                    value = _expressionEvaluator.Evaluate(eqNode.RightHandSide);

                    // Check if redefining a variable
                    if (_variableStore.IsExplicitlySet(varName))
                    {
                        double currentVal = _variableStore.GetVariable(varName);
                        double relDiff = Math.Abs(value - currentVal) / Math.Max(Math.Abs(currentVal), 1e-9);

                        if (relDiff > 1e-6)
                        {
                            // Only throw if this isn't a check variable like "var_check"
                            if (!varName.EndsWith("_check", StringComparison.OrdinalIgnoreCase) &&
                                !varName.EndsWith("_error", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException(
                                    $"Equation '{eqNode}' attempts to redefine '{varName}'. " +
                                    $"Current: {currentVal}, New: {value}");
                            }
                        }
                    }

                    // Set the variable
                    _variableStore.SetVariable(varName, value);
                    Console.WriteLine($"  Assigned {varName} = {value}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error evaluating assignment for '{varName}': {ex.Message}");
                    // Don't abort - continue processing other assignments
                }
            }
        }
    }
    #endregion Statement Processing Logic

    #region ODE and Integral Table Logic
    private void ExecuteIntegralDefinitions()
    {
        if (_integralDefinitions.Count == 0 && _odeStateEquations.Any()) Console.WriteLine("Warning: ODE state equations found, but no y = INTEGRAL(...) definition.");
        List<EquationNode> equationsForOdeSubSolve = new List<EquationNode>(_algebraicEquations);
        equationsForOdeSubSolve.AddRange(_odeStateEquations.Where(ose => !equationsForOdeSubSolve.Contains(ose)));
        List<EquationNode> consumedByOde = new List<EquationNode>();

        foreach (var integralAssignNode in _integralDefinitions)
        {
            var dependentVarNode = integralAssignNode.Variable;
            var integralCallNode = (FunctionCallNode)integralAssignNode.RightHandSide;
            if (integralCallNode.Arguments.Count < 4 || integralCallNode.Arguments.Count > 5) { Console.Error.WriteLine($"Error: INTEGRAL for '{dependentVarNode.Name}' bad arg count."); continue; }
            string dydtVarName = ((VariableNode)integralCallNode.Arguments[0]).Name;
            string indepVarName = ((VariableNode)integralCallNode.Arguments[1]).Name;
            double lowerLimit = _expressionEvaluator.Evaluate(integralCallNode.Arguments[2]);
            double upperLimit = _expressionEvaluator.Evaluate(integralCallNode.Arguments[3]);
            double fixedStepSize = (integralCallNode.Arguments.Count == 5) ? _expressionEvaluator.Evaluate(integralCallNode.Arguments[4]) : 0.0;
            List<EquationNode> relevantEquationsForDydt = equationsForOdeSubSolve.Where(eq => ExpressionContainsVariable(eq.LeftHandSide, dydtVarName) || ExpressionContainsVariable(eq.RightHandSide, dydtVarName)).ToList();
            if (!relevantEquationsForDydt.Any()) { Console.Error.WriteLine($"Error: Derivative '{dydtVarName}' for INTEGRAL '{dependentVarNode.Name}' not defined."); continue; }
            consumedByOde.AddRange(relevantEquationsForDydt.Where(req => !consumedByOde.Contains(req)));
            double initialValueY = _variableStore.IsExplicitlySet(dependentVarNode.Name) ? _variableStore.GetVariable(dependentVarNode.Name) : _variableStore.GetVariable(dependentVarNode.Name);
            if (lowerLimit == 0.0 && Math.Abs(initialValueY - _variableStore.GetGuessValue(dependentVarNode.Name, 1.0)) < 1e-9 && !_variableStore.HasGuessValue(dependentVarNode.Name)) { initialValueY = 0.0; _variableStore.SetVariable(dependentVarNode.Name, 0.0); }
            var odeSolver = new OdeSolver(_variableStore, _functionRegistry, _expressionEvaluator, relevantEquationsForDydt, dydtVarName, dependentVarNode.Name, initialValueY, indepVarName, lowerLimit, upperLimit, fixedStepSize);
            ConfigureOdeSolver(odeSolver); double finalY = odeSolver.Solve();
            string? originalUnit = _variableStore.HasUnit(dependentVarNode.Name) ? _variableStore.GetUnit(dependentVarNode.Name) : null;
            _variableStore.SetSolvedVariable(dependentVarNode.Name, finalY);
            if (originalUnit != null) _variableStore.SetUnit(dependentVarNode.Name, originalUnit);
            if (_integralTableColumns.Count > 0 && !string.IsNullOrEmpty(_integralTableIndVarName) && string.Equals(_integralTableIndVarName, indepVarName, StringComparison.OrdinalIgnoreCase))
            {
                var (times, values) = odeSolver.GetResults(); UpdateIntegralTable(indepVarName, dependentVarNode.Name, times, values);
            }
        }
        foreach (var eq in consumedByOde) _algebraicEquations.Remove(eq);
        _odeStateEquations.Clear();
    }

    private void ProcessIntegralTableDirective(string directiveText)
    {
        _integralTable.Clear(); _integralTableColumns.Clear(); _integralTableIndVarName = string.Empty; _integralTableOutputStepSize = 0.0;
        string specPart = directiveText.Substring("$IntegralTable".Length).Trim();
        var match = Regex.Match(specPart, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*(?::\s*([a-zA-Z0-9_\.]+))?\s*(?:,(.*))?$");
        if (!match.Success) { match = Regex.Match(specPart, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)(\s*//.*|\s*\{.*\}|\s*"".*"")?$"); if (!match.Success) { Console.Error.WriteLine($"Error: Could not parse $IntegralTable directive: {directiveText}"); return; } }
        _integralTableIndVarName = match.Groups[1].Value.Trim(); _integralTableColumns.Add(_integralTableIndVarName);
        if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value)) { string stepValue = match.Groups[2].Value; if (double.TryParse(stepValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double step)) { _integralTableOutputStepSize = step; } else if (_variableStore.HasVariable(stepValue)) { _integralTableOutputStepSize = _variableStore.GetVariable(stepValue); } else { Console.WriteLine($"Warning: Could not interpret step size '{stepValue}' in $IntegralTable. Using solver steps."); } }
        string? columnsString = string.Empty; if (match.Groups[3].Success) { columnsString = match.Groups[3].Value; } else if (!match.Groups[2].Success && specPart.Contains(",")) { int firstComma = specPart.IndexOf(','); if (firstComma != -1) { columnsString = specPart.Substring(firstComma + 1); } }
        if (!string.IsNullOrWhiteSpace(columnsString))
        {
            var commentMatch = Regex.Match(columnsString, @"(\s*//.*|\s*\{.*\}|\s*"".*"")\s*$"); if (commentMatch.Success) { columnsString = columnsString.Remove(commentMatch.Index).Trim(); }
            var cols = columnsString.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c));
            foreach (var col in cols)
            {
                // Corrected Contains for List<string> with comparer
                if (!_integralTableColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                {
                    _integralTableColumns.Add(col);
                }
            }
        }
        foreach (var colName_1 in _integralTableColumns) { _integralTable[colName_1] = new List<double>(); }
    }

    private void UpdateIntegralTable(string indepVarName, string depVarName, List<double> indepValues, List<double> depValues)
    {
        if (_integralTableColumns.Count == 0 || !string.Equals(_integralTableIndVarName, indepVarName, StringComparison.OrdinalIgnoreCase)) return;
        _integralTable[_integralTableIndVarName].Clear(); if (_integralTable.ContainsKey(depVarName)) _integralTable[depVarName].Clear();
        foreach (var colName in _integralTableColumns) { if (!string.Equals(colName, indepVarName, StringComparison.OrdinalIgnoreCase) && !string.Equals(colName, depVarName, StringComparison.OrdinalIgnoreCase) && _integralTable.ContainsKey(colName)) { _integralTable[colName].Clear(); } }
        List<double> outputTimes = new List<double>(); List<double> outputDepValues = new List<double>();
        if (_integralTableOutputStepSize > 0 && indepValues.Count > 1) { double startTime = indepValues.First(), endTime = indepValues.Last(), currentTime = startTime; int k_idx = 0; while (currentTime <= endTime + 1e-9 * Math.Abs(endTime - startTime)) { while (k_idx + 1 < indepValues.Count && indepValues[k_idx + 1] < currentTime - 1e-9) k_idx++; double t0 = indepValues[k_idx], y0 = depValues[k_idx]; outputTimes.Add(currentTime); if (k_idx + 1 >= indepValues.Count || Math.Abs(currentTime - t0) < 1e-9) outputDepValues.Add(y0); else { double t1 = indepValues[k_idx + 1], y1 = depValues[k_idx + 1]; if (Math.Abs(t1 - t0) < 1e-9) outputDepValues.Add(y0); else outputDepValues.Add(y0 + (currentTime - t0) / (t1 - t0) * (y1 - y0)); } if (Math.Abs(currentTime - endTime) < 1e-9) break; currentTime += _integralTableOutputStepSize; if (currentTime > endTime && Math.Abs(outputTimes.Last() - endTime) > 1e-9) { outputTimes.Add(endTime); int k_end_idx = 0; while (k_end_idx + 1 < indepValues.Count && indepValues[k_end_idx + 1] < endTime - 1e-9) k_end_idx++; double t0_end = indepValues[k_end_idx], y0_end = depValues[k_end_idx]; if (k_end_idx + 1 >= indepValues.Count || Math.Abs(endTime - t0_end) < 1e-9) outputDepValues.Add(y0_end); else { double t1_end = indepValues[k_end_idx + 1], y1_end = depValues[k_end_idx + 1]; if (Math.Abs(t1_end - t0_end) < 1e-9) outputDepValues.Add(y0_end); else outputDepValues.Add(y0_end + (endTime - t0_end) / (t1_end - t0_end) * (y1_end - y0_end)); } break; } if (outputTimes.Count > (_maxSteps > 0 ? _maxSteps * 5 : 10000)) { Console.WriteLine("Warning: Exceeded max output points for integral table."); break; } } } else { outputTimes.AddRange(indepValues); outputDepValues.AddRange(depValues); }
        if (!outputTimes.Any()) { if (indepValues.Any()) { outputTimes.Add(indepValues.First()); outputDepValues.Add(depValues.First()); } else { return; } }
        _integralTable[_integralTableIndVarName].AddRange(outputTimes); if (_integralTable.ContainsKey(depVarName)) { _integralTable[depVarName].AddRange(outputDepValues); }
        for (int timeIdx = 0; timeIdx < outputTimes.Count; timeIdx++)
        {
            double tValue = outputTimes[timeIdx], yValue = outputDepValues[timeIdx]; _variableStore.SetVariable(indepVarName, tValue); _variableStore.SetVariable(depVarName, yValue);
            foreach (string columnName in _integralTableColumns)
            {
                if (string.Equals(columnName, indepVarName, StringComparison.OrdinalIgnoreCase) || string.Equals(columnName, depVarName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_integralTable.ContainsKey(columnName)) _integralTable[columnName] = new List<double>();
                try
                {
                    EquationNode? defEq = _algebraicEquations.FirstOrDefault(eq => eq.LeftHandSide is Ast.VariableNode lvn && string.Equals(lvn.Name, columnName, StringComparison.OrdinalIgnoreCase)) ??
                                        _odeStateEquations.FirstOrDefault(eq => eq.LeftHandSide is Ast.VariableNode lvn && string.Equals(lvn.Name, columnName, StringComparison.OrdinalIgnoreCase));
                    double val;
                    if (defEq != null) { val = _expressionEvaluator.Evaluate(defEq.RightHandSide); _variableStore.SetVariable(columnName, val); }
                    else if (_variableStore.HasVariable(columnName)) { val = _variableStore.GetVariable(columnName); }
                    else { val = double.NaN; }
                    _integralTable[columnName].Add(val);
                }
                catch (Exception ex) { _integralTable[columnName].Add(double.NaN); Console.WriteLine($"Warn: Eval '{columnName}' for table at t={tValue}: {ex.Message}"); }
            }
        }
    }

    private void PrintIntegralTable()
    {
        if (_integralTable.Count == 0 || _integralTableColumns.Count == 0 || !_integralTable.First().Value.Any()) return;
        Console.WriteLine("\n--- Integral Table ---"); Console.WriteLine(string.Join("\t", _integralTableColumns.Select(c => c.PadRight(12))));
        int rowCount = _integralTable[_integralTableColumns[0]].Count;
        for (int i = 0; i < rowCount; i++) { string[] rowValues = new string[_integralTableColumns.Count]; for (int j = 0; j < _integralTableColumns.Count; j++) { string colName = _integralTableColumns[j]; if (i < _integralTable[colName].Count) rowValues[j] = _integralTable[colName][i].ToString("F6", CultureInfo.InvariantCulture).PadRight(12); else rowValues[j] = "N/A".PadRight(12); } Console.WriteLine(string.Join("\t", rowValues)); }
        Console.WriteLine("----------------------");
    }

    private void ProcessIntegralAutoStepDirective(string directiveText)
    {
        string settingsPart = directiveText.Substring("$IntegralAutoStep".Length).Trim(); var settings = settingsPart.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var setting in settings) { var parts = setting.Split('='); if (parts.Length != 2) continue; string key = parts[0].Trim().ToLowerInvariant(), valStr = parts[1].Trim(); if (!double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double numVal)) { Console.WriteLine($"Warn: Parse val '{valStr}' for $IAS key '{key}'."); continue; } switch (key) { case "vary": _varyStepSize = (int)numVal == 1; break; case "min": _minSteps = (int)numVal; break; case "max": _maxSteps = (int)numVal; break; case "reduce": _reduceThreshold = numVal; break; case "increase": _increaseThreshold = numVal; break; default: Console.WriteLine($"Warn: Unknown $IAS key '{key}'."); break; } }
    }

    private void ConfigureOdeSolver(OdeSolver solver)
    {
        solver.ConfigureAdaptiveStepSize(_varyStepSize, _minSteps, _maxSteps, _reduceThreshold, _increaseThreshold);
    }

    private void ProcessPlotCommands()
    {
        Console.WriteLine("\n--- Processing Plot Commands ---");
        foreach (var plotCmd in _plotCommands) { try { if (_integralTable.Count == 0 || !_integralTable.First().Value.Any()) { Console.WriteLine($"Warn: No integral table data for plot: {plotCmd.CommandText}."); continue; } var plotData = _plottingService.CreatePlot(plotCmd.CommandText, _integralTable); if (plotData == null) { Console.WriteLine($"Warn: PlotData not generated for: {plotCmd.CommandText}."); continue; } } catch (Exception ex) { Console.WriteLine($"Error plot cmd '{plotCmd.CommandText}': {ex.Message}"); } }
    }
    #endregion ODE and Integral Table Logic

    #region Helpers
    // In LibreSolvE.Core/Evaluation/StatementExecutor.cs
    // Update the IsConstantValue method with debug output

    private bool IsConstantValue(ExpressionNode node, out double value)
    {
        value = 0;

        _expressionEvaluator.ResetEvaluationDependencyFlag(); // Reset flag before this specific evaluation

        try
        {
            // Evaluate the expression. The evaluator will set its internal flag
            // if any variable accessed was not explicitly set or solved.
            value = _expressionEvaluator.Evaluate(node);

            // After evaluation, check the flag.
            if (_expressionEvaluator.DidEvaluationUseNonExplicitVariable())
            {
                // Add debug output
                Console.WriteLine($"Debug: Expression '{node}' is not constant (used non-explicit variables)");
                return false; // Depended on a guess/default, so not "constant" for immediate assignment.
            }

            return true; // Successfully evaluated AND did not rely on non-explicit variables.
        }
        catch (Exception ex) // Any evaluation error (DivideByZero, function error, etc.)
        {
            // Add debug output
            Console.WriteLine($"Debug: Expression '{node}' evaluation failed: {ex.Message}");
            // Errors during evaluation also mean it's not a simple constant value right now.
            return false;
        }
    }

    private bool IsDydtEquation(EquationNode eqNode)
    {
        if (eqNode == null || (eqNode.LeftHandSide == null && eqNode.RightHandSide == null))
            return false;

        // Specifically look for state variables that contain 'd' followed by other letters, then 'dt'
        // This is more precise than the previous broad check
        if (eqNode.LeftHandSide is VariableNode vn)
        {
            string name = vn.Name.ToLowerInvariant();
            // Look for standalone 'd<var>/dt' or 'd<var>dt' patterns, not just any 'd' and 'dt' in the name
            if (name.StartsWith("d") && name.EndsWith("dt") && name.Length > 3)
                return true;

            // Classic 'dydt' pattern
            if (name == "dydt" || name == "dxdt")
                return true;
        }

        // Look for equations explicitly formed like: d<var>/dt = ... or d<var>dt = ...
        return false;
    }

    private bool ContainsDerivativeTerms(ExpressionNode node)
    {
        switch (node) { case VariableNode vn: return vn.Name.ToLowerInvariant().Contains("dydt") || (vn.Name.ToLowerInvariant().Contains("d") && vn.Name.ToLowerInvariant().Contains("dt")); case BinaryOperationNode bon: return ContainsDerivativeTerms(bon.Left) || ContainsDerivativeTerms(bon.Right); case FunctionCallNode fcn: return fcn.Arguments.Any(arg => ContainsDerivativeTerms(arg)); default: return false; }
    }

    private void CollectVariablesFromExpression(ExpressionNode? node, HashSet<string> variables)
    {
        if (node == null) return; switch (node) { case VariableNode vn: variables.Add(vn.Name); break; case BinaryOperationNode bon: CollectVariablesFromExpression(bon.Left, variables); CollectVariablesFromExpression(bon.Right, variables); break; case FunctionCallNode fcn: foreach (var arg in fcn.Arguments) CollectVariablesFromExpression(arg, variables); break; }
    }

    public Dictionary<string, List<double>> GetIntegralTableData() => new Dictionary<string, List<double>>(_integralTable);
    public List<string> GetIntegralTableColumnOrder() => new List<string>(_integralTableColumns);
    public string GetIntegralTableIndependentVariable() => _integralTableIndVarName;

    private bool ExpressionContainsVariable(ExpressionNode? node, string varName)
    {
        if (node == null) return false;
        if (node is VariableNode vn && string.Equals(vn.Name, varName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (node is BinaryOperationNode bon)
            return ExpressionContainsVariable(bon.Left, varName) || ExpressionContainsVariable(bon.Right, varName);
        if (node is FunctionCallNode fcn)
            return fcn.Arguments.Any(arg => ExpressionContainsVariable(arg, varName));
        return false;
    }
    #endregion Helpers
}
