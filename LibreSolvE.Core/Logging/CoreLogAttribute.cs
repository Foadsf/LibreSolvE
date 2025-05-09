// LibreSolvE.Core/Logging/CoreLogAttribute.cs
using MethodBoundaryAspect.Fody.Attributes;
using Serilog; // Assuming Serilog is configured globally by the host app (CLI/GUI)
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LibreSolvE.Core.Logging // Or your AOP namespace
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CoreLogAttribute : OnMethodBoundaryAspect
    {
        public override void OnEntry(MethodExecutionArgs args)
        {
            Log.Debug("Core--> Enter: {Class}.{Method}({Parameters})",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                FormatParameters(args.Method, args.Arguments));
        }

        public override void OnExit(MethodExecutionArgs args)
        {
            // ... (similar exit logging as GUI's LogAttribute) ...
            string returnValue = (args.Method is MethodInfo mi && mi.ReturnType == typeof(void)) || args.ReturnValue == null
                                 ? "(void/null)"
                                 : args.ReturnValue.ToString() ?? "null";
            Log.Debug("Core<-- Exit:  {Class}.{Method} => {ReturnValue}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                returnValue);
        }

        public override void OnException(MethodExecutionArgs args)
        {
            Log.Error(args.Exception, "Core!!! Exception in {Class}.{Method}: {ExceptionType} - {ExceptionMessage}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                args.Exception.GetType().Name,
                args.Exception.Message);
        }

        private string FormatParameters(MethodBase method, object[] arguments)
        {
            // ... (same formatting logic as GUI's LogAttribute) ...
            var parameters = method.GetParameters();
            if (parameters.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"{parameters[i].Name ?? $"p{i}"}: {arguments[i]?.ToString() ?? "null"}");
            }
            return sb.ToString();
        }
    }
}
