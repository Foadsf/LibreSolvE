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

    private double GetSlope(double tValue, double yValue)
    {
        // Temporarily set the independent and dependent variables in the store
        double originalT = _variableStore.HasVariable(_independentVarName) ? _variableStore.GetVariable(_independentVarName) : double.NaN;
        double originalY = _variableStore.HasVariable(_dependentVarName) ? _variableStore.GetVariable(_dependentVarName) : double.NaN;
        // Also backup dydt_var if it exists, as the sub-solver will modify it
        double originalDydt = _variableStore.HasVariable(_dydtVarName) ? _variableStore.GetVariable(_dydtVarName) : double.NaN;


        _variableStore.SetVariable(_independentVarName, tValue);
        _variableStore.SetVariable(_dependentVarName, yValue);
        // Ensure dydtVarName has a guess value if it's not explicitly set for the sub-solver
        if (!_variableStore.IsExplicitlySet(_dydtVarName) && !_variableStore.HasGuessValue(_dydtVarName))
        {
            _variableStore.SetGuessValue(_dydtVarName, 0.0); // Default guess for derivative
        }


        try
        {
            // Solve the provided algebraic system (_equationsForOdeStep) for _dydtVarName
            // This is a sub-solve step. We need a temporary, limited solver.
            // For simplicity here, we assume the system is small or that dydt_var can be isolated.
            // A full robust solution would involve invoking EquationSolver on _equationsForOdeStep
            // to find the value of _dydtVarName.

            // Create a temporary EquationSolver for the subset of equations
            // Make sure the equations passed are only those needed to define dydt and its dependencies
            // For the example dydt + 4*t*y = -2*t, this is just one equation.
            var tempSolverSettings = new SolverSettings { MaxIterations = 50, Tolerance = 1e-4 }; // Quick solve

            // The variables to solve for in this sub-problem are _dydtVarName and any other
            // variables that are coupled with it in _equationsForOdeStep and are not t or y.
            // For simplicity, we'll assume for now that the _equationsForOdeStep can be directly solved for _dydtVarName
            // if t and y are known. More complex systems would need a full solve.

            var subProblemEquations = new List<EquationNode>(_equationsForOdeStep);

            // We need to find which variables in subProblemEquations are unknown *besides* t and y (which are fixed for this slope calc)
            var unknownVarsForSlope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var collector = new VariableCollector();
            foreach (var eq in subProblemEquations)
            {
                collector.Collect(eq.LeftHandSide, unknownVarsForSlope);
                collector.Collect(eq.RightHandSide, unknownVarsForSlope);
            }
            unknownVarsForSlope.Remove(_independentVarName); // t is known
            unknownVarsForSlope.Remove(_dependentVarName);   // y is known

            // If _dydtVarName is the only unknown, or if the system is simple enough
            if (unknownVarsForSlope.Count <= 1 && unknownVarsForSlope.Contains(_dydtVarName) || unknownVarsForSlope.Count == 0)
            {
                // This is a simplification. If other variables are coupled with dydt,
                // a full solve of the sub-system is needed.
                // For now, let's assume EquationSolver can handle it if _dydtVarName is the primary target.
            }


            var odeStepSolver = new EquationSolver(_variableStore, _functionRegistry, subProblemEquations, tempSolverSettings);

            bool solved = odeStepSolver.Solve();

            if (!solved)
            {
                Console.Error.WriteLine($"OdeSolver.GetSlope: Sub-solver failed to find value for '{_dydtVarName}' at t={tValue}, y={yValue}. Returning 0 slope.");
                // Attempt to evaluate directly if it's in an explicit form in case the solver fails on simple cases
                var explicitEq = subProblemEquations.FirstOrDefault(e => e.LeftHandSide is VariableNode vn && vn.Name == _dydtVarName);
                if (explicitEq != null)
                {
                    try { return _evaluator.Evaluate(explicitEq.RightHandSide); } catch { return 0.0; }
                }
                return 0.0; // Or throw
            }
            return _variableStore.GetVariable(_dydtVarName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"OdeSolver.GetSlope: Exception evaluating slope for '{_dydtVarName}' at t={tValue}, y={yValue}: {ex.Message}. Returning 0 slope.");
            return 0.0; // Or rethrow
        }
        finally
        {
            if (!double.IsNaN(originalT)) _variableStore.SetVariable(_independentVarName, originalT);
            if (!double.IsNaN(originalY)) _variableStore.SetVariable(_dependentVarName, originalY);
            if (!double.IsNaN(originalDydt)) _variableStore.SetVariable(_dydtVarName, originalDydt);
            else if (_variableStore.HasVariable(_dydtVarName) && !_variableStore.IsExplicitlySet(_dydtVarName))
            {
                // If it was created by the sub-solver and wasn't there before,
                // it might be cleaner to remove it, but this could be complex.
                // For now, leave it; its value will be overwritten by the main solver if it's an unknown there.
            }
        }
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
