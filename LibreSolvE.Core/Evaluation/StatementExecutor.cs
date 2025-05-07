// LibreSolvE.Core/Evaluation/StatementExecutor.cs
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Plotting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibreSolvE.Core.Evaluation;

public class StatementExecutor
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry;
    private readonly SolverSettings _solverSettings;
    private ExpressionEvaluatorVisitor _expressionEvaluator; // Will be initialized after _statementExecutor is set

    // Categorized statements
    private readonly List<AssignmentNode> _explicitAssignments = new();
    private readonly List<EquationNode> _potentialAssignments = new();
    private readonly List<EquationNode> _algebraicEquations = new();
    private readonly List<EquationNode> _odeStateEquations = new(); // Equations like "dydt = ..."
    private readonly List<AssignmentNode> _integralDefinitions = new(); // Equations like "y = INTEGRAL(...)"
    private readonly List<DirectiveNode> _directives = new();
    private readonly List<PlotCommandNode> _plotCommands = new();


    // fields to hold ODE solver settings
    private bool _varyStepSize = true; // Default EES behavior for adaptive
    private int _minSteps = 100;      // EES default Min Steps for adaptive if not specified
    private int _maxSteps = 1000;     // EES default Max Steps
    private double _reduceThreshold = 0.001; // EES default Reduce Error
    private double _increaseThreshold = 0.00001; // EES default Increase Error


    // integral table data structure
    private Dictionary<string, List<double>> _integralTable = new Dictionary<string, List<double>>();
    private string _integralTableIndVarName = string.Empty; // Independent variable for the table (e.g., 't')
    private double _integralTableOutputStepSize = 0.0; // If 0, output at every solver step
    private List<string> _integralTableColumns = new List<string>();


    private readonly PlottingService _plottingService = new PlottingService();

    public event EventHandler<PlotData> PlotCreated
    {
        add { _plottingService.PlotCreated += value; }
        remove { _plottingService.PlotCreated -= value; }
    }

    public StatementExecutor(VariableStore variableStore, FunctionRegistry functionRegistry, SolverSettings solverSettings)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        _solverSettings = solverSettings ?? new SolverSettings();
        // Initialize evaluator here. Pass 'this' to allow ExpressionEvaluator to call back if needed (e.g. for INTEGRAL context).
        _expressionEvaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, this, false);
    }

    // Method to access the independent variable name for the integral table
    public string GetIntegralTableIndVarName()
    {
        return _integralTableIndVarName;
    }

    // Modify IsConstantValue to have an overload or an out parameter indicating if it depended on non-explicit variables
    private bool IsConstantValue(ExpressionNode node, out double value, out bool dependsOnNonExplicitUnknowns)
    {
        value = 0;
        dependsOnNonExplicitUnknowns = false;
        if (node is FunctionCallNode funcCallNode &&
            string.Equals(funcCallNode.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
        {
            return false; // INTEGRAL is never a simple constant assignment
        }

        // Temporarily set warningsAsErrors to true for this check to catch dependencies on defaults/guesses
        // that are not yet explicitly set as part of the problem's knowns.
        bool originalWarningsAsErrors = _expressionEvaluator.GetWarningsAsErrors();
        _expressionEvaluator.SetWarningsAsErrors(true); // Treat undefined (not explicitly set) as errors for this check

        try
        {
            _expressionEvaluator.ResetUndefinedVariableCount();
            value = _expressionEvaluator.Evaluate(node); // This will now throw if it hits a var not explicitly set
            // If it didn't throw, it means all variables in 'node' were either literals or explicitly set.
            dependsOnNonExplicitUnknowns = false; // No, because it would have thrown.
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("accessed before assignment during solve step"))
        {
            // This means the expression depended on a variable that wasn't explicitly set
            dependsOnNonExplicitUnknowns = true;
            return false;
        }
        catch
        {
            // Other evaluation errors
            dependsOnNonExplicitUnknowns = true; // Assume dependency if other errors occur
            return false;
        }
        finally
        {
            _expressionEvaluator.SetWarningsAsErrors(originalWarningsAsErrors); // Restore original setting
        }
    }

    public void Execute(EesFileNode fileNode)
    {
        CategorizeStatements(fileNode);

        ProcessDirectives();
        ExecuteExplicitAssignments();
        ExecutePotentialAssignments(); // This will filter out Integral definitions

        // Now, _integralDefinitions contains y = INTEGRAL(...)
        // And _algebraicEquations contains other equations, including dydt = ...
        ExecuteIntegralDefinitions(); // This will solve ODEs and populate _integralTable

        PrintIntegralTable(); // Print table if populated

        SolveRemainingAlgebraicEquations(); // Solves what's left in _algebraicEquations

        ProcessPlotCommands(); // Uses the populated _integralTable

        Console.WriteLine("--- Statement Processing Finished ---");
    }

    // StatementExecutor.cs - CategorizeStatements method modification

    private void CategorizeStatements(EesFileNode fileNode)
    {
        Console.WriteLine("--- Pre-processing Statements: Categorizing ---");
        foreach (var statement in fileNode.Statements)
        {
            switch (statement)
            {
                case DirectiveNode directive:
                    _directives.Add(directive);
                    Console.WriteLine($"Debug: Categorized Directive: {directive.DirectiveText}");
                    break;
                case PlotCommandNode plotCmd:
                    _plotCommands.Add(plotCmd);
                    Console.WriteLine($"Debug: Categorized Plot Command: {plotCmd.CommandText}");
                    break;
                case AssignmentNode assignNode:
                    // Check if RHS is an INTEGRAL call
                    if (assignNode.RightHandSide is FunctionCallNode funcCall &&
                        string.Equals(funcCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
                    {
                        _integralDefinitions.Add(assignNode);
                        Console.WriteLine($"Debug: Categorized Integral Definition: {assignNode}");
                    }
                    else
                    {
                        _explicitAssignments.Add(assignNode);
                        Console.WriteLine($"Debug: Categorized Explicit Assignment: {assignNode}");
                    }
                    break;
                case EquationNode eqNode:
                    // First, check if it's an Integral Definition like "y = INTEGRAL(...)"
                    if (eqNode.LeftHandSide is VariableNode depVarNode &&
                        eqNode.RightHandSide is FunctionCallNode rhsFuncCall &&
                        string.Equals(rhsFuncCall.FunctionName, "INTEGRAL", StringComparison.OrdinalIgnoreCase))
                    {
                        // Create a pseudo-AssignmentNode for consistent handling.
                        var integralAssignNode = new AssignmentNode(depVarNode, rhsFuncCall);
                        _integralDefinitions.Add(integralAssignNode);
                        Console.WriteLine($"Debug: Categorized Integral Definition (from EquationNode): {eqNode}");
                    }
                    // NEW: Check if it's a state equation with dydt
                    else if ((eqNode.LeftHandSide is VariableNode lhsVarNode &&
                              lhsVarNode.Name.ToLowerInvariant().Contains("dydt")) ||
                            (IsDydtEquation(eqNode)))
                    {
                        _odeStateEquations.Add(eqNode);
                        Console.WriteLine($"Debug: Categorized ODE State Equation: {eqNode}");
                    }
                    // Else, proceed with existing logic for potential assignments / algebraic equations
                    else
                    {
                        bool isPotentiallyAssignable = false;
                        ExpressionNode? assignableLhs = null;
                        ExpressionNode? assignableRhs = null;

                        if (eqNode.LeftHandSide is VariableNode varNodeLhsInner)
                        {
                            // Try Var = ConstExpr
                            if (IsConstantValue(eqNode.RightHandSide, out _, out bool rhsDependsOnUnknowns) && !rhsDependsOnUnknowns)
                            {
                                isPotentiallyAssignable = true;
                                assignableLhs = varNodeLhsInner;
                                assignableRhs = eqNode.RightHandSide;
                                Console.WriteLine($"Debug: Categorized Potential Assignment: {eqNode}");
                            }
                        }

                        if (!isPotentiallyAssignable && eqNode.RightHandSide is VariableNode varNodeRhs)
                        {
                            // Try ConstExpr = Var (normalized to Var = ConstExpr)
                            if (IsConstantValue(eqNode.LeftHandSide, out _, out bool lhsDependsOnUnknowns) && !lhsDependsOnUnknowns)
                            {
                                isPotentiallyAssignable = true;
                                assignableLhs = varNodeRhs;
                                assignableRhs = eqNode.LeftHandSide;
                                Console.WriteLine($"Debug: Categorized Potential Assignment (Reversed): {eqNode}");
                            }
                        }

                        if (isPotentiallyAssignable)
                        {
                            if (assignableLhs != null && assignableRhs != null)
                            {
                                _potentialAssignments.Add(new EquationNode(assignableLhs, assignableRhs));
                            }
                            else
                            {
                                Console.WriteLine($"Warning: Potential assignment categorization issue with {eqNode}. Treating as algebraic.");
                                _algebraicEquations.Add(eqNode);
                            }
                        }
                        else
                        {
                            _algebraicEquations.Add(eqNode);
                            Console.WriteLine($"Debug: Categorized Algebraic Equation: {eqNode}");
                        }
                    }
                    break;
            }
        }
    }

    // Helper method to detect if an equation is related to dydt
    private bool IsDydtEquation(EquationNode eqNode)
    {
        // Check if the equation involves a dydt variable anywhere
        return ContainsDerivativeTerms(eqNode.LeftHandSide) || ContainsDerivativeTerms(eqNode.RightHandSide);
    }

    // Helper method to check if an expression contains derivative terms
    private bool ContainsDerivativeTerms(ExpressionNode node)
    {
        switch (node)
        {
            case VariableNode varNode:
                return varNode.Name.ToLowerInvariant().Contains("dydt") ||
                      varNode.Name.ToLowerInvariant().Contains("d") && varNode.Name.ToLowerInvariant().Contains("dt");
            case BinaryOperationNode binOpNode:
                return ContainsDerivativeTerms(binOpNode.Left) || ContainsDerivativeTerms(binOpNode.Right);
            case FunctionCallNode funcNode:
                return funcNode.Arguments.Any(arg => ContainsDerivativeTerms(arg));
            default:
                return false;
        }
    }

    private void ProcessDirectives()
    {
        Console.WriteLine("--- Processing Directives ---");
        foreach (var directive in _directives)
        {
            string text = directive.DirectiveText.Trim();
            Console.WriteLine($"Processing Directive: {text}");
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
    }

    private void ExecuteExplicitAssignments()
    {
        Console.WriteLine("--- Executing Explicit Assignments (:=) ---");
        foreach (var assignNode in _explicitAssignments)
        {
            ExecuteSingleExplicitAssignment(assignNode);
        }
    }

    private void ExecuteSingleExplicitAssignment(AssignmentNode assignNode)
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
        }
    }


    // StatementExecutor.cs - ExecutePotentialAssignments method modification

    private void ExecutePotentialAssignments()
    {
        Console.WriteLine("--- Processing Potential Assignments (Var = ConstExpr) ---");

        // This list will hold equations that were initially thought to be potential assignments
        // but, upon re-evaluation, are found to depend on unknowns.
        List<EquationNode> toReCategorizeAsAlgebraic = new List<EquationNode>();
        List<EquationNode> toReCategorizeAsOdeState = new List<EquationNode>();
        List<EquationNode> successfullyAssigned = new List<EquationNode>();

        foreach (var eqNode in _potentialAssignments)
        {
            // Check if this is actually a state equation with dydt that was miscategorized
            if (eqNode.LeftHandSide is VariableNode lhsVarNode &&
                lhsVarNode.Name.ToLowerInvariant().Contains("dydt"))
            {
                toReCategorizeAsOdeState.Add(eqNode);
                Console.WriteLine($"Debug: Recategorizing potential assignment '{eqNode}' to ODE state equation.");
                continue;
            }

            var variableNode = (VariableNode)eqNode.LeftHandSide; // This cast is safe due to categorization logic

            // Use the strict IsConstantValue to ensure RHS is truly assignable now
            if (IsConstantValue(eqNode.RightHandSide, out double val, out bool rhsDependsOnUnknowns) && !rhsDependsOnUnknowns)
            {
                Console.WriteLine($"Assigning {variableNode.Name} = {val} (from equation)");
                _variableStore.SetVariable(variableNode.Name, val);
                successfullyAssigned.Add(eqNode);
            }
            else
            {
                // If it involves dydt, recategorize as ODE state
                if (ContainsDerivativeTerms(eqNode.LeftHandSide) || ContainsDerivativeTerms(eqNode.RightHandSide))
                {
                    toReCategorizeAsOdeState.Add(eqNode);
                    Console.WriteLine($"Debug: Recategorizing potential assignment '{eqNode}' to ODE state equation due to derivative terms.");
                }
                // If it's not a true constant assignment now (e.g., depended on a variable
                // that is still an unknown for the algebraic solver), it should be an algebraic equation.
                else
                {
                    Console.WriteLine($"Debug: RHS for potential assignment '{eqNode}' still depends on unknowns or was not constant. Re-categorizing to algebraic.");
                    toReCategorizeAsAlgebraic.Add(eqNode);
                }
            }
        }

        // Update the _potentialAssignments list to remove those that were re-categorized or assigned
        // And add the re-categorized ones to _algebraicEquations or _odeStateEquations
        foreach (var eq in successfullyAssigned)
        {
            _potentialAssignments.Remove(eq);
        }
        foreach (var eq in toReCategorizeAsAlgebraic)
        {
            _potentialAssignments.Remove(eq);
            if (!_algebraicEquations.Contains(eq)) // Avoid duplicates if somehow already there
            {
                _algebraicEquations.Add(eq);
            }
        }
        foreach (var eq in toReCategorizeAsOdeState)
        {
            _potentialAssignments.Remove(eq);
            if (!_odeStateEquations.Contains(eq)) // Avoid duplicates if somehow already there
            {
                _odeStateEquations.Add(eq);
            }
        }
    }

    // In LibreSolvE.Core/Evaluation/StatementExecutor.cs

    private void ExecuteIntegralDefinitions()
    {
        Console.WriteLine($"--- Executing Integral Definitions ({_integralDefinitions.Count} found) ---");
        if (_integralDefinitions.Count == 0 && _algebraicEquations.Any(eq => ContainsDerivativeTerm(eq)))
        {
            Console.WriteLine("Warning: Potential ODE state equations found, but no y = INTEGRAL(...) definition to drive the solver.");
        }

        // equationsForOdeStep should contain all currently known algebraic equations
        // that might be needed to determine 'dydt_var' at each time step.
        // The Integral definition itself (y = Integral(...)) is NOT part of this set for the sub-solver.
        List<EquationNode> equationsForOdeSubSolve = new List<EquationNode>(_algebraicEquations);
        // Also include any equations that were categorized as ODE state equations previously if any.
        // (Though, with current logic, state equations for dydt would be in _algebraicEquations initially)
        equationsForOdeSubSolve.AddRange(_odeStateEquations.Where(ose => !equationsForOdeSubSolve.Contains(ose)));


        // This loop was problematic and likely unnecessary with correct categorization:
        // foreach(var integralDef in _integralDefinitions) {
        //     if (integralDef is EquationNode eqn) equationsForOdeStep.Remove(eqn); // This line caused CS8121
        // }
        // The _integralDefinitions are AssignmentNodes, _algebraicEquations are EquationNodes.
        // The categorization step should prevent y = Integral(...) from being in _algebraicEquations.

        List<EquationNode> consumedByOde = new List<EquationNode>();

        foreach (var integralAssignNode in _integralDefinitions)
        {
            var dependentVarNode = integralAssignNode.Variable;
            var integralCallNode = (FunctionCallNode)integralAssignNode.RightHandSide;

            // ... (argument extraction for INTEGRAL as before) ...
            if (integralCallNode.Arguments.Count < 4 || integralCallNode.Arguments.Count > 5)
            {
                Console.Error.WriteLine($"Error: INTEGRAL function for '{dependentVarNode.Name}' has an incorrect number of arguments.");
                continue;
            }
            // (Safe casts after check)
            string dydtVarName = ((VariableNode)integralCallNode.Arguments[0]).Name;
            string indepVarName = ((VariableNode)integralCallNode.Arguments[1]).Name;
            double lowerLimit = _expressionEvaluator.Evaluate(integralCallNode.Arguments[2]);
            double upperLimit = _expressionEvaluator.Evaluate(integralCallNode.Arguments[3]);
            double fixedStepSize = (integralCallNode.Arguments.Count == 5) ? _expressionEvaluator.Evaluate(integralCallNode.Arguments[4]) : 0.0;


            // Find all equations that involve dydtVarName. These are needed by the OdeSolver's internal sub-solve.
            List<EquationNode> relevantEquationsForDydt = equationsForOdeSubSolve.Where(eq =>
                ExpressionContainsVariable(eq.LeftHandSide, dydtVarName) ||
                ExpressionContainsVariable(eq.RightHandSide, dydtVarName)).ToList();

            if (!relevantEquationsForDydt.Any())
            {
                Console.Error.WriteLine($"Error: Derivative variable '{dydtVarName}' used in INTEGRAL for '{dependentVarNode.Name}' does not appear to be defined by any available algebraic equations.");
                continue;
            }
            // Add these to a list of equations consumed by ODE steps to potentially remove from main algebraic solve later
            consumedByOde.AddRange(relevantEquationsForDydt.Where(req => !consumedByOde.Contains(req)));


            double initialValueY;
            // ... (initial value logic for y - remains the same)
            if (_variableStore.IsExplicitlySet(dependentVarNode.Name))
            {
                initialValueY = _variableStore.GetVariable(dependentVarNode.Name);
                Console.WriteLine($"Debug: Using explicitly set initial value for '{dependentVarNode.Name}': {initialValueY} at t={lowerLimit}");
            }
            else
            {
                initialValueY = _variableStore.GetVariable(dependentVarNode.Name);
                if (lowerLimit == 0.0 && Math.Abs(initialValueY - 1.0) < 1e-9 && !_variableStore.HasGuessValue(dependentVarNode.Name))
                {
                    initialValueY = 0.0;
                    _variableStore.SetVariable(dependentVarNode.Name, 0.0);
                    Console.WriteLine($"Debug: Assuming implicit y(0)=0 for '{dependentVarNode.Name}'. Set to 0.");
                }
                Console.WriteLine($"Debug: Using initial value for '{dependentVarNode.Name}': {initialValueY} at t={lowerLimit} (from store/default)");
            }

            Console.WriteLine($"--- Solving ODE for '{dependentVarNode.Name}' from t={lowerLimit} to t={upperLimit} ---");
            Console.WriteLine($"    Dependent Var: {dependentVarNode.Name}, Initial Value: {initialValueY}");
            Console.WriteLine($"    Independent Var: {indepVarName}");
            Console.WriteLine($"    Derivative Var: {dydtVarName}, determined by system: [{string.Join(" ; ", relevantEquationsForDydt)}]");


            var odeSolver = new OdeSolver(
                _variableStore,
                _functionRegistry,
                _expressionEvaluator,
                relevantEquationsForDydt,  // Pass only the equations relevant to finding dydt
                dydtVarName,
                dependentVarNode.Name,
                initialValueY,
                indepVarName,
                lowerLimit,
                upperLimit,
                fixedStepSize
            );

            ConfigureOdeSolver(odeSolver);

            double finalY = odeSolver.Solve();
            _variableStore.SetVariable(dependentVarNode.Name, finalY);
            Console.WriteLine($"ODE for '{dependentVarNode.Name}' solved. Final value: {finalY} at t={upperLimit}");

            if (_integralTableColumns.Count > 0 && !string.IsNullOrEmpty(_integralTableIndVarName) &&
                string.Equals(_integralTableIndVarName, indepVarName, StringComparison.OrdinalIgnoreCase))
            {
                var (times, values) = odeSolver.GetResults();
                UpdateIntegralTable(indepVarName, dependentVarNode.Name, times, values);
            }
        }

        // Remove equations that were solely for defining derivatives from the main algebraic set
        foreach (var eq in consumedByOde)
        {
            _algebraicEquations.Remove(eq);
        }
        // _odeStateEquations list might be redundant now, or used if we want to explicitly track them
        // For now, let's clear it as its contents were copied or are part of the consumed set.
        _odeStateEquations.Clear();
    }

    // Helper to check if an AST expression contains a specific variable name
    private bool ExpressionContainsVariable(ExpressionNode node, string varName)
    {
        if (node is VariableNode vn && string.Equals(vn.Name, varName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (node is BinaryOperationNode bon)
            return ExpressionContainsVariable(bon.Left, varName) || ExpressionContainsVariable(bon.Right, varName);
        if (node is FunctionCallNode fcn)
            return fcn.Arguments.Any(arg => ExpressionContainsVariable(arg, varName));
        return false;
    }

    // Helper to check if an equation node (LHS or RHS) contains a derivative term (heuristic)
    private bool ContainsDerivativeTerm(EquationNode eq)
    {
        // A simple heuristic: does it contain "dydt" or similar?
        // This could be improved by checking if any variable in the equation
        // is used as the first argument to an INTEGRAL function.
        Func<AstNode, bool>? hasDydt = null;
        hasDydt = (node) =>
        {
            if (node == null) return false;
            if (node is VariableNode vn && vn.Name.ToLowerInvariant().Contains("dydt")) return true;
            if (node is BinaryOperationNode bon)
                return (bon.Left != null && hasDydt(bon.Left)) || (bon.Right != null && hasDydt(bon.Right));
            if (node is FunctionCallNode fcn)
                return fcn.Arguments != null && fcn.Arguments.Any(a => a != null && hasDydt(a));
            return false;
        };
        return hasDydt(eq.LeftHandSide) || hasDydt(eq.RightHandSide);
    }


    public bool SolveRemainingAlgebraicEquations()
    {
        Console.WriteLine("\n--- Equation Solving Phase (Algebraic) ---");
        if (_algebraicEquations.Count == 0)
        {
            Console.WriteLine("No algebraic equations identified for solver.");
            return true; // No equations to solve is considered a success in this context.
        }

        var solver = new EquationSolver(_variableStore, _functionRegistry, _algebraicEquations, _solverSettings);
        bool success = solver.Solve();

        if (success)
        {
            Console.WriteLine("\n--- Algebraic Solver Phase Completed Successfully ---");
            _algebraicEquations.Clear(); // Clear equations once successfully solved
        }
        else
        {
            Console.Error.WriteLine("\n--- Algebraic Solver FAILED ---");
            // Do not clear if failed, might be useful for debugging or retry
        }
        return success; // Return the actual success status from the solver.
    }


    public void ProcessIntegralTableDirective(string directiveText)
    {
        _integralTable.Clear();
        _integralTableColumns.Clear();
        _integralTableIndVarName = string.Empty;
        _integralTableOutputStepSize = 0.0; // Default: output at every internal solver step

        string specPart = directiveText.Substring("$IntegralTable".Length).Trim();

        // Regex to capture: VarName[:StepSize], Col1, Col2, ... // Comment
        // Allows VarName, VarName:Step, VarName, Col1, VarName:Step, Col1
        var match = Regex.Match(specPart, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*(?::\s*([0-9\.]+))?\s*(?:,(.*))?$");

        if (!match.Success)
        {
            // Try just VarName
            match = Regex.Match(specPart, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)(\s*//.*|\s*\{.*\}|\s*""."")?$");
            if (!match.Success)
            {
                Console.Error.WriteLine($"Error: Could not parse $IntegralTable directive: {directiveText}");
                return;
            }
        }

        _integralTableIndVarName = match.Groups[1].Value.Trim();
        _integralTableColumns.Add(_integralTableIndVarName); // Independent var is always the first column

        if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            if (double.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double step))
            {
                _integralTableOutputStepSize = step;
            }
            else
            {
                Console.Error.WriteLine($"Warning: Could not parse step size '{match.Groups[2].Value}' in $IntegralTable. Using solver steps.");
            }
        }

        string? columnsString = string.Empty;
        if (match.Groups[3].Success)
        { // If there was a comma and potentially more columns
            columnsString = match.Groups[3].Value;
        }
        else if (!match.Groups[2].Success && specPart.Contains(","))
        { // VarName, Col1,... (no step)
            int firstComma = specPart.IndexOf(',');
            if (firstComma != -1)
            {
                columnsString = specPart.Substring(firstComma + 1);
            }
        }


        if (!string.IsNullOrWhiteSpace(columnsString))
        {
            // Remove any trailing comments from the columns string
            var commentMatch = Regex.Match(columnsString, @"(\s*//.*|\s*\{.*\}|\s*"".*"")\s*$");
            if (commentMatch.Success)
            {
                columnsString = columnsString.Remove(commentMatch.Index).Trim();
            }

            var cols = columnsString.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(c => c.Trim())
                                    .Where(c => !string.IsNullOrEmpty(c));
            foreach (var col in cols)
            {
                if (!_integralTableColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                {
                    _integralTableColumns.Add(col);
                }
            }
        }

        // Initialize lists for all columns
        foreach (var colName in _integralTableColumns)
        {
            _integralTable[colName] = new List<double>();
        }

        Console.WriteLine($"Created Integral Table. Independent Variable: '{_integralTableIndVarName}', Output Step: {_integralTableOutputStepSize}. Columns: {string.Join(", ", _integralTableColumns)}");
    }

    public void UpdateIntegralTable(string indepVarName, string depVarName, List<double> indepValues, List<double> depValues)
    {
        if (_integralTableColumns.Count == 0 || !string.Equals(_integralTableIndVarName, indepVarName, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Warning: $IntegralTable not defined or independent variable mismatch. Cannot update table.");
            return;
        }

        Console.WriteLine($"Updating Integral Table with {indepValues.Count} points for {indepVarName} and {depVarName}.");

        // Clear existing data for these primary variables
        _integralTable[_integralTableIndVarName].Clear();
        if (_integralTable.ContainsKey(depVarName)) _integralTable[depVarName].Clear();

        foreach (var colName in _integralTableColumns)
        {
            if (!string.Equals(colName, indepVarName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(colName, depVarName, StringComparison.OrdinalIgnoreCase) &&
                _integralTable.ContainsKey(colName)) // Ensure column exists before clearing
            {
                _integralTable[colName].Clear();
            }
        }

        // Determine output points based on _integralTableOutputStepSize
        List<double> outputTimes = new List<double>(); // DECLARED HERE
        List<double> outputDepValues = new List<double>();
        // List<List<double>> outputOtherValues = new List<List<double>>(); // Not used currently
        // for(int k=0; k < _integralTableColumns.Count - 2; ++k) outputOtherValues.Add(new List<double>()); // Not used currently


        if (_integralTableOutputStepSize > 0 && indepValues.Count > 1)
        {
            double startTime = indepValues.First();
            double endTime = indepValues.Last();
            double currentTime = startTime;
            int k_idx = 0; // Index for original data

            while (currentTime <= endTime + 1e-9 * Math.Abs(endTime - startTime)) // Add relative epsilon
            {
                while (k_idx + 1 < indepValues.Count && indepValues[k_idx + 1] < currentTime - 1e-9) // Search with tolerance
                {
                    k_idx++;
                }

                double t0 = indepValues[k_idx];
                double y0 = depValues[k_idx];

                outputTimes.Add(currentTime);

                if (k_idx + 1 >= indepValues.Count || Math.Abs(currentTime - t0) < 1e-9)
                {
                    outputDepValues.Add(y0);
                }
                else
                {
                    double t1 = indepValues[k_idx + 1];
                    double y1 = depValues[k_idx + 1];
                    if (Math.Abs(t1 - t0) < 1e-9)
                    {
                        outputDepValues.Add(y0);
                    }
                    else
                    {
                        double fraction = (currentTime - t0) / (t1 - t0);
                        outputDepValues.Add(y0 + fraction * (y1 - y0));
                    }
                }

                if (Math.Abs(currentTime - endTime) < 1e-9) break; // Reached end

                currentTime += _integralTableOutputStepSize;
                if (currentTime > endTime && Math.Abs(outputTimes.Last() - endTime) > 1e-9)
                { // If overshot, add endTime as last point
                    outputTimes.Add(endTime);
                    // Interpolate for depValues at endTime
                    // Re-find k_idx for endTime
                    int k_end_idx = 0;
                    while (k_end_idx + 1 < indepValues.Count && indepValues[k_end_idx + 1] < endTime - 1e-9) k_end_idx++;

                    double t0_end = indepValues[k_end_idx]; double y0_end = depValues[k_end_idx];
                    if (k_end_idx + 1 >= indepValues.Count || Math.Abs(endTime - t0_end) < 1e-9) outputDepValues.Add(y0_end);
                    else
                    {
                        double t1_end = indepValues[k_end_idx + 1]; double y1_end = depValues[k_end_idx + 1];
                        if (Math.Abs(t1_end - t0_end) < 1e-9) outputDepValues.Add(y0_end);
                        else outputDepValues.Add(y0_end + (endTime - t0_end) / (t1_end - t0_end) * (y1_end - y0_end));
                    }
                    break;
                }
                if (outputTimes.Count > (_maxSteps > 0 ? _maxSteps * 5 : 10000))
                { // Safety break, ensure _maxSteps is positive
                    Console.WriteLine("Warning: Exceeded maximum output points for integral table. Truncating.");
                    break;
                }
            }
        }
        else
        {
            outputTimes.AddRange(indepValues);
            outputDepValues.AddRange(depValues);
        }

        if (!outputTimes.Any())
        { // If somehow outputTimes is empty, add initial point at least
            if (indepValues.Any())
            {
                outputTimes.Add(indepValues.First());
                outputDepValues.Add(depValues.First());
            }
            else
            {
                Console.WriteLine("Warning: No data points to update integral table.");
                return;
            }
        }

        _integralTable[_integralTableIndVarName].AddRange(outputTimes);
        if (_integralTable.ContainsKey(depVarName))
        {
            _integralTable[depVarName].AddRange(outputDepValues);
        }

        for (int i = 0; i < outputTimes.Count; i++) // 'i' IS DECLARED HERE
        {
            _variableStore.SetVariable(indepVarName, outputTimes[i]); // outputTimes[i] IS VALID
            _variableStore.SetVariable(depVarName, outputDepValues[i]);

            foreach (string colName in _integralTableColumns)
            {
                if (string.Equals(colName, indepVarName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(colName, depVarName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!_integralTable.ContainsKey(colName)) _integralTable[colName] = new List<double>();

                EquationNode? definingEqForCol = _algebraicEquations
                    .FirstOrDefault(eq => eq.LeftHandSide is VariableNode lhsVar && string.Equals(lhsVar.Name, colName, StringComparison.OrdinalIgnoreCase));

                if (definingEqForCol == null)
                {
                    definingEqForCol = _odeStateEquations
                        .FirstOrDefault(eq => eq.LeftHandSide is VariableNode lhsVar && string.Equals(lhsVar.Name, colName, StringComparison.OrdinalIgnoreCase));
                }

                if (definingEqForCol != null) // Explicit check
                {
                    try
                    {
                        // Now definingEqForCol is known to be not null in this block
                        double val = _expressionEvaluator.Evaluate(definingEqForCol.RightHandSide);
                        _integralTable[colName].Add(val);
                    }
                    catch (Exception ex)
                    {
                        double currentTimeForLog = outputTimes[i];
                        Console.WriteLine($"Warning: Could not evaluate '{colName}' for integral table at t={currentTimeForLog}: {ex.Message}");
                        _integralTable[colName].Add(double.NaN);
                    }
                }
                else
                {
                    if (_variableStore.HasVariable(colName))
                    {
                        _integralTable[colName].Add(_variableStore.GetVariable(colName));
                    }
                    else
                    {
                        double currentTimeForLog = outputTimes[i];
                        Console.WriteLine($"Warning: Variable '{colName}' for integral table not defined by an equation or in store at t={currentTimeForLog}.");
                        _integralTable[colName].Add(double.NaN);
                    }
                }
            }
        }
    }


    public void PrintIntegralTable()
    {
        if (_integralTable.Count == 0 || _integralTableColumns.Count == 0 || !_integralTable.First().Value.Any())
        {
            // Console.WriteLine("No Integral Table data available to print or table is empty."); // Suppress if not explicitly requested
            return;
        }

        Console.WriteLine("\n--- Integral Table ---");
        Console.WriteLine(string.Join("\t", _integralTableColumns.Select(c => c.PadRight(12)))); // Header

        int rowCount = _integralTable[_integralTableColumns[0]].Count;
        for (int i = 0; i < rowCount; i++)
        {
            string[] rowValues = new string[_integralTableColumns.Count];
            for (int j = 0; j < _integralTableColumns.Count; j++)
            {
                string columnName = _integralTableColumns[j];
                if (i < _integralTable[columnName].Count)
                {
                    rowValues[j] = _integralTable[columnName][i].ToString("F6", CultureInfo.InvariantCulture).PadRight(12);
                }
                else
                {
                    rowValues[j] = "N/A".PadRight(12);
                }
            }
            Console.WriteLine(string.Join("\t", rowValues));
        }
        Console.WriteLine("----------------------");
    }

    public Dictionary<string, List<double>> GetIntegralTable()
    {
        return _integralTable;
    }

    public void ProcessIntegralAutoStepDirective(string directiveText)
    {
        string settingsPart = directiveText.Substring("$IntegralAutoStep".Length).Trim();
        var settings = settingsPart.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var setting in settings)
        {
            var parts = setting.Split('=');
            if (parts.Length != 2) continue;

            string key = parts[0].Trim().ToLowerInvariant();
            string valueStr = parts[1].Trim();

            if (!double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double numValue))
            {
                Console.WriteLine($"Warning: Could not parse value '{valueStr}' for $IntegralAutoStep key '{key}'.");
                continue;
            }

            switch (key)
            {
                case "vary": _varyStepSize = (int)numValue == 1; break;
                case "min": _minSteps = (int)numValue; break;
                case "max": _maxSteps = (int)numValue; break;
                case "reduce": _reduceThreshold = numValue; break;
                case "increase": _increaseThreshold = numValue; break;
                default: Console.WriteLine($"Warning: Unknown $IntegralAutoStep key '{key}'."); break;
            }
        }
        Console.WriteLine($"IntegralAutoStep configured: Vary={_varyStepSize}, Min={_minSteps}, Max={_maxSteps}, Reduce={_reduceThreshold}, Increase={_increaseThreshold}");
    }

    public void ConfigureOdeSolver(OdeSolver solver)
    {
        solver.ConfigureAdaptiveStepSize(
            _varyStepSize,
            _minSteps,
            _maxSteps,
            _reduceThreshold,
            _increaseThreshold);
    }

    private void ProcessPlotCommands()
    {
        Console.WriteLine("\n--- Processing Plot Commands ---");
        foreach (var plotCmd in _plotCommands)
        {
            try
            {
                if (_integralTable.Count == 0 || !_integralTable.First().Value.Any())
                {
                    Console.WriteLine($"Warning: No integral table data available for plot command: {plotCmd.CommandText}. Plot will not be generated.");
                    continue;
                }
                Console.WriteLine($"Processing Plot Command: {plotCmd.CommandText}");
                var plotData = _plottingService.CreatePlot(plotCmd.CommandText, _integralTable);

                string fileName = $"plot_{DateTime.Now:yyyyMMdd_HHmmssfff}.svg";
                _plottingService.SaveToSvg(plotData, fileName);
                Console.WriteLine($"Plot saved to {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing plot command '{plotCmd.CommandText}': {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            }
        }
    }
}
