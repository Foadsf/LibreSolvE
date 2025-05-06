// LibreSolvE.Core/Evaluation/IntegrationTask.cs
using LibreSolvE.Core.Ast;

namespace LibreSolvE.Core.Evaluation;

internal class IntegrationTask
{
    public VariableNode TargetVariable { get; } // The variable receiving the final result (e.g., 'y')
    public FunctionCallNode IntegralCall { get; } // The INTEGRAL(...) node

    public IntegrationTask(VariableNode targetVariable, FunctionCallNode integralCall)
    {
        TargetVariable = targetVariable;
        IntegralCall = integralCall;
    }
}
