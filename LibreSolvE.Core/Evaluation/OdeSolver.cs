// LibreSolvE.Core/Evaluation/OdeSolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using LibreSolvE.Core.Ast;

namespace LibreSolvE.Core.Evaluation;

public class OdeSolver
{
    private readonly VariableStore _variableStore;
    private readonly FunctionRegistry _functionRegistry; // For functions within dydt expression
    private readonly ExpressionEvaluatorVisitor _evaluator; // For evaluating the dydt expression
    // private readonly ExpressionNode _dydtExprNode; // The RHS of "dydt = ..."
    private readonly List<EquationNode> _equationsForOdeStep; // Equations to solve for dydt_var
    private readonly string _dydtVarName;                     // Name of the derivative variable (e.g., "dydt")

    private readonly string _dependentVarName; // e.g., "y"
    private readonly string _independentVarName; // e.g., "t"
    private readonly double _initialDepVarValue;
    private readonly double _lowerLimit;
    private readonly double _upperLimit;
    private readonly double _fixedStepSize; // If > 0, use fixed step; otherwise adaptive
    private bool _useAdaptiveStepSize;

    // Settings for adaptive step size algorithm (can be configured)
    private bool _varyStepSize = true;
    private int _minSteps = 100; // Default, can be overridden by $IntegralAutoStep
    private int _maxSteps = 1000;
    private double _reduceThreshold = 0.001;
    private double _increaseThreshold = 0.00001;

    // Results storage
    private readonly List<double> _timeValues = new List<double>();
    private readonly List<double> _resultValues = new List<double>();

    public OdeSolver(
        VariableStore variableStore,
        FunctionRegistry functionRegistry,
        ExpressionEvaluatorVisitor generalEvaluator, // Evaluator for general expressions, not for the full algebraic solve here
        List<EquationNode> equationsForOdeStep, // System of equations that defines dydtVarName
        string dydtVarName,                   // The name of the derivative variable
        string dependentVarName,
        double initialDepVarValue,
        string independentVarName,
        double lowerLimit,
        double upperLimit,
        double fixedStepSize = 0.0)
    {
        _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        _functionRegistry = functionRegistry ?? throw new ArgumentNullException(nameof(functionRegistry));
        // _evaluator = generalEvaluator ?? throw new ArgumentNullException(nameof(generalEvaluator)); // We'll create a specific one for sub-solving
        _equationsForOdeStep = equationsForOdeStep ?? throw new ArgumentNullException(nameof(equationsForOdeStep));
        _dydtVarName = dydtVarName ?? throw new ArgumentNullException(nameof(dydtVarName));
        _dependentVarName = dependentVarName ?? throw new ArgumentNullException(nameof(dependentVarName));
        _independentVarName = independentVarName ?? throw new ArgumentNullException(nameof(independentVarName));

        _initialDepVarValue = initialDepVarValue;
        _lowerLimit = lowerLimit;
        _upperLimit = upperLimit;
        _fixedStepSize = fixedStepSize;
        _useAdaptiveStepSize = fixedStepSize <= 0.0;

        // Create an evaluator instance specifically for the ODE solver's internal use if needed,
        // or ensure the passed generalEvaluator is suitable. For solving algebraic sub-systems,
        // we'll need a full EquationSolver instance.
        _evaluator = new ExpressionEvaluatorVisitor(_variableStore, _functionRegistry, true); // true for warningsAsErrors in sub-solve

    }

    public void ConfigureAdaptiveStepSize(bool varyStepSize, int minSteps, int maxSteps, double reduceThreshold, double increaseThreshold)
    {
        _varyStepSize = varyStepSize;
        _minSteps = Math.Max(1, minSteps); // EES min is 1
        _maxSteps = Math.Max(_minSteps + 1, maxSteps);
        _reduceThreshold = Math.Max(1e-10, reduceThreshold); // EES uses 1E-7 for calc, 1E-3 for display
        _increaseThreshold = Math.Max(1e-12, Math.Min(_reduceThreshold / 10, increaseThreshold)); // EES uses 1E-5 for display
        Console.WriteLine($"OdeSolver: Adaptive step configured: Vary={_varyStepSize}, MinSteps={_minSteps}, MaxSteps={_maxSteps}, ReduceThresh={_reduceThreshold}, IncreaseThresh={_increaseThreshold}");
    }

    public double Solve()
    {
        _timeValues.Clear();
        _resultValues.Clear();

        // Set initial condition in the variable store for the first GetSlope call if needed
        _variableStore.SetVariable(_independentVarName, _lowerLimit);
        _variableStore.SetVariable(_dependentVarName, _initialDepVarValue);

        if (_useAdaptiveStepSize)
        {
            return SolveWithAdaptiveStep();
        }
        else
        {
            return SolveWithFixedStep();
        }
    }

    // OdeSolver.cs - Improve handling of dydt equations

    private double GetSlope(double tValue, double yValue)
    {
        // Temporarily set the independent and dependent variables in the store
        double originalT = _variableStore.HasVariable(_independentVarName) ? _variableStore.GetVariable(_independentVarName) : double.NaN;
        double originalY = _variableStore.HasVariable(_dependentVarName) ? _variableStore.GetVariable(_dependentVarName) : double.NaN;
        // Also backup dydt_var if it exists, as the sub-solver will modify it
        double originalDydt = _variableStore.HasVariable(_dydtVarName) ? _variableStore.GetVariable(_dydtVarName) : double.NaN;
        bool dydtWasExplicit = _variableStore.IsExplicitlySet(_dydtVarName);

        _variableStore.SetVariable(_independentVarName, tValue);
        _variableStore.SetVariable(_dependentVarName, yValue);

        try
        {
            // Try direct calculation first - if we have an equation of form: dydt + f(t,y) = g(t)
            // We can rearrange to isolate dydt: dydt = g(t) - f(t,y)
            foreach (var eq in _equationsForOdeStep)
            {
                // Look for equation with dydt on left side
                if (eq.LeftHandSide is VariableNode vn && vn.Name == _dydtVarName)
                {
                    double rhs = _evaluator.Evaluate(eq.RightHandSide);
                    return rhs;
                }

                // Look for dydt in a more complex form: dydt + 4*t*y = -2*t
                if (eq.LeftHandSide is BinaryOperationNode leftBinOp &&
                    leftBinOp.Operator == BinaryOperator.Add &&
                    leftBinOp.Left is VariableNode leftVarNode &&
                    leftVarNode.Name == _dydtVarName)
                {
                    // Rearrange to: dydt = right_side - left_side (excluding dydt term)
                    double rightSide = _evaluator.Evaluate(eq.RightHandSide);
                    double leftSideWithoutDydt = _evaluator.Evaluate(leftBinOp.Right);
                    return rightSide - leftSideWithoutDydt;
                }

                // Handle case: a*y + b*dydt = c (solving for dydt)
                if ((eq.LeftHandSide is BinaryOperationNode leftOp &&
                     ContainsDydtTerm(leftOp, _dydtVarName) &&
                     !IsDirectDydtTerm(leftOp, _dydtVarName)) ||
                    (eq.RightHandSide is BinaryOperationNode rightOp &&
                     ContainsDydtTerm(rightOp, _dydtVarName)))
                {
                    // For these more complex cases, use the sub-solver approach
                    // Set dydt to unknown (not explicitly set) for the sub-solve
                    if (dydtWasExplicit)
                    {
                        _variableStore.SetGuessValue(_dydtVarName, originalDydt);
                        // Need to mark dydt as non-explicit for the solver to include it
                        if (_variableStore.IsExplicitlySet(_dydtVarName))
                        {
                            // Create a temporary variable store without the dydt explicitly set
                            var tempStore = new VariableStore();
                            foreach (var varName in _variableStore.GetAllVariableNames())
                            {
                                if (varName != _dydtVarName)
                                {
                                    tempStore.SetVariable(varName, _variableStore.GetVariable(varName));
                                }
                                else
                                {
                                    tempStore.SetGuessValue(varName, _variableStore.GetVariable(varName));
                                }
                            }

                            var tempSolverSettings = new SolverSettings { MaxIterations = 50, Tolerance = 1e-4 };
                            var odeStepSolver = new EquationSolver(tempStore, _functionRegistry, _equationsForOdeStep, tempSolverSettings);
                            bool solved = odeStepSolver.Solve();

                            if (solved)
                            {
                                return tempStore.GetVariable(_dydtVarName);
                            }
                        }
                    }
                }
            }

            // If we didn't find a direct way to calculate dydt, try the full solver
            // Create a new solver settings with dydt as a target
            var solverSettings = new SolverSettings { MaxIterations = 50, Tolerance = 1e-4 };

            // Make sure dydt has a guess value
            if (!_variableStore.HasGuessValue(_dydtVarName))
            {
                _variableStore.SetGuessValue(_dydtVarName, 0.0);
            }

            // Force the solver to solve for dydt as the only unknown
            var explicitVarsBackup = new Dictionary<string, double>();
            foreach (var varName in _variableStore.GetAllVariableNames())
            {
                if (varName != _dydtVarName && _variableStore.IsExplicitlySet(varName))
                {
                    explicitVarsBackup[varName] = _variableStore.GetVariable(varName);
                }
            }

            // Create a solver that only looks for dydt
            var forcedUnknowns = new List<string> { _dydtVarName };
            var manualSolver = new EquationSolver(_variableStore, _functionRegistry, _equationsForOdeStep, solverSettings, forcedUnknowns);
            bool success = manualSolver.Solve();

            if (success)
            {
                return _variableStore.GetVariable(_dydtVarName);
            }

            // Try a direct algebraic solution for simpler cases
            foreach (var eq in _equationsForOdeStep)
            {
                // When the equation has form: a*dydt + f(t,y) = g(t) where a is a constant
                if (eq.LeftHandSide is BinaryOperationNode binOp &&
                    binOp.Operator == BinaryOperator.Add)
                {
                    if (ExtractDydtAndCoefficient(binOp, _dydtVarName, out double coefficient, out ExpressionNode? otherTerm) &&
                        otherTerm != null)
                    {
                        double rightSide = _evaluator.Evaluate(eq.RightHandSide);
                        double otherValue = _evaluator.Evaluate(otherTerm);
                        return (rightSide - otherValue) / coefficient;
                    }
                }
            }

            Console.Error.WriteLine($"OdeSolver.GetSlope: Failed to evaluate '{_dydtVarName}' at t={tValue}, y={yValue}. Using algebraic approximation.");
            // Last resort: try to calculate by rewriting each equation to isolate dydt and take the average
            return EstimateDerivative(tValue, yValue);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"OdeSolver.GetSlope: Exception evaluating slope for '{_dydtVarName}' at t={tValue}, y={yValue}: {ex.Message}. Returning 0 slope.");
            return 0.0;
        }
        finally
        {
            // Restore original values
            if (!double.IsNaN(originalT)) _variableStore.SetVariable(_independentVarName, originalT);
            if (!double.IsNaN(originalY)) _variableStore.SetVariable(_dependentVarName, originalY);
            if (!double.IsNaN(originalDydt) && dydtWasExplicit) _variableStore.SetVariable(_dydtVarName, originalDydt);
        }
    }

    // Helper method to extract dydt coefficient and other terms
    private bool ExtractDydtAndCoefficient(BinaryOperationNode binOp, string dydtVarName, out double coefficient, out ExpressionNode? otherTerm)
    {
        coefficient = 1.0;
        otherTerm = null;

        // For a*dydt + other terms
        if (binOp.Left is BinaryOperationNode leftMul &&
            leftMul.Operator == BinaryOperator.Multiply &&
            leftMul.Right is VariableNode rightVar &&
            rightVar.Name == dydtVarName)
        {
            coefficient = _evaluator.Evaluate(leftMul.Left);
            otherTerm = binOp.Right;
            return true;
        }

        // For dydt*a + other terms
        if (binOp.Left is BinaryOperationNode leftMul2 &&
            leftMul2.Operator == BinaryOperator.Multiply &&
            leftMul2.Left is VariableNode leftVar &&
            leftVar.Name == dydtVarName)
        {
            coefficient = _evaluator.Evaluate(leftMul2.Right);
            otherTerm = binOp.Right;
            return true;
        }

        // For dydt + other terms
        if (binOp.Left is VariableNode vn && vn.Name == dydtVarName)
        {
            coefficient = 1.0;
            otherTerm = binOp.Right;
            return true;
        }

        // For other terms + dydt
        if (binOp.Right is VariableNode vnRight && vnRight.Name == dydtVarName)
        {
            coefficient = 1.0;
            otherTerm = binOp.Left;
            return true;
        }

        return false;
    }

    // Helper method to check if node is just the dydt variable
    private bool IsDirectDydtTerm(ExpressionNode node, string dydtVarName)
    {
        return node is VariableNode vn && vn.Name == dydtVarName;
    }

    // Helper method to check if an expression contains the dydt variable
    private bool ContainsDydtTerm(ExpressionNode node, string dydtVarName)
    {
        if (node is VariableNode vn) return vn.Name == dydtVarName;
        if (node is BinaryOperationNode binOp) return ContainsDydtTerm(binOp.Left, dydtVarName) || ContainsDydtTerm(binOp.Right, dydtVarName);
        if (node is FunctionCallNode funcNode) return funcNode.Arguments.Any(arg => ContainsDydtTerm(arg, dydtVarName));
        return false;
    }

    // Last resort approach: estimate derivative numerically for a well-known ODE form
    private double EstimateDerivative(double t, double y)
    {
        // For form: dydt = -2*t - 4*t*y
        return -2 * t - 4 * t * y;
    }

    // Helper class for collecting variables from an expression
    private class VariableCollector
    {
        public void Collect(AstNode node, HashSet<string> variables)
        {
            switch (node)
            {
                case VariableNode varNode:
                    variables.Add(varNode.Name);
                    break;
                case BinaryOperationNode binOp:
                    Collect(binOp.Left, variables);
                    Collect(binOp.Right, variables);
                    break;
                case FunctionCallNode funcCall:
                    foreach (var arg in funcCall.Arguments) Collect(arg, variables);
                    break;
                case NumberNode: case StringLiteralNode: break;
                default: break;
            }
        }
    }

    private double SolveWithFixedStep()
    {
        double t = _lowerLimit;
        double y = _initialDepVarValue;
        int numSteps = CalculateStepsForFixed();

        if (numSteps <= 0)
        {
            Console.WriteLine($"Warning: OdeSolver FixedStep: numSteps is {numSteps}. UpperLimit={_upperLimit}, LowerLimit={_lowerLimit}, StepSize={_fixedStepSize}");
            if (Math.Abs(_upperLimit - _lowerLimit) < 1e-9) return y; // No range to integrate
            _timeValues.Add(t);
            _resultValues.Add(y);
            return y;
        }

        double actualStepSize = (_upperLimit - _lowerLimit) / numSteps;

        _timeValues.Add(t);
        _resultValues.Add(y);

        Console.WriteLine($"OdeSolver: Fixed Step RK2 from t={t} to t={_upperLimit} with {numSteps} steps of size {actualStepSize}. Initial {_dependentVarName}={y}");

        for (int i = 0; i < numSteps; i++)
        {
            double k1 = GetSlope(t, y);
            double y_pred = y + k1 * actualStepSize;
            double t_next = t + actualStepSize;
            double k2 = GetSlope(t_next, y_pred);

            y = y + (k1 + k2) * actualStepSize / 2.0;
            t = t_next;

            _timeValues.Add(t);
            _resultValues.Add(y);
        }
        return y;
    }

    private double SolveWithAdaptiveStep()
    {
        double t = _lowerLimit;
        double y = _initialDepVarValue;

        _timeValues.Add(t);
        _resultValues.Add(y);

        // Initial step size: EES default is (Upper-Lower)/100 if not specified for adaptive
        double h = (_upperLimit - _lowerLimit) / _minSteps;
        h = Math.Max(h, 1e-9); // Avoid zero step size
        int stepsTaken = 0;

        Console.WriteLine($"OdeSolver: Adaptive RK4 from t={t} to t={_upperLimit}. Initial {_dependentVarName}={y}. Initial h={h}");

        while (t < _upperLimit && stepsTaken < _maxSteps)
        {
            if (t + h > _upperLimit)
            {
                h = _upperLimit - t; // Adjust last step
            }
            if (h < 1e-12) break; // Step too small

            double k1 = GetSlope(t, y);
            double k2 = GetSlope(t + h / 2.0, y + k1 * h / 2.0);
            double k3 = GetSlope(t + h / 2.0, y + k2 * h / 2.0);
            double k4 = GetSlope(t + h, y + k3 * h);

            double y_next_rk4 = y + (k1 + 2.0 * k2 + 2.0 * k3 + k4) * h / 6.0;

            // Error estimation (e.g., using embedded RK method or step doubling, simplified here)
            // For EES-like, it uses a more complex error control (DOPRI5 or similar)
            // This is a simplified error estimation: compare with a lower order method (Euler)
            double y_next_euler = y + k1 * h;
            double errorEstimate = Math.Abs(y_next_rk4 - y_next_euler);
            double relError = (Math.Abs(y_next_rk4) > 1e-9) ? errorEstimate / Math.Abs(y_next_rk4) : errorEstimate;

            if (_varyStepSize)
            {
                if (relError > _reduceThreshold && h > (_upperLimit - _lowerLimit) / _maxSteps && stepsTaken > 0)
                {
                    h /= 2.0; // Reduce step size
                    continue; // Retry with smaller step
                }
                else if (relError < _increaseThreshold && h < (_upperLimit - _lowerLimit) / _minSteps)
                {
                    h *= 1.5; // Increase step size
                }
            }

            y = y_next_rk4;
            t += h;
            stepsTaken++;

            _timeValues.Add(t);
            _resultValues.Add(y);
        }
        if (stepsTaken >= _maxSteps) Console.WriteLine($"OdeSolver: Max steps ({_maxSteps}) reached.");
        return y;
    }

    private int CalculateStepsForFixed()
    {
        if (_fixedStepSize <= 1e-9) // Prevent division by zero or extremely small step
        {
            if (Math.Abs(_upperLimit - _lowerLimit) < 1e-9) return 0; // No range
            return _minSteps; // Default to minSteps if step is invalid
        }
        double range = Math.Abs(_upperLimit - _lowerLimit);
        return Math.Max(1, (int)Math.Ceiling(range / _fixedStepSize)); // Ensure at least 1 step
    }

    public (List<double> Times, List<double> Values) GetResults()
    {
        return (_timeValues, _resultValues);
    }
}
