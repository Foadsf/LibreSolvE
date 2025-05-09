// LibreSolvE.CLI/Logging/LoggingAspect.cs
using MethodBoundaryAspect.Fody.Attributes;
using Serilog;
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LibreSolvE.CLI.Logging
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class LogAttribute : OnMethodBoundaryAspect
    {
        public bool SkipParameterLogging { get; set; } = false;

        public override void OnEntry(MethodExecutionArgs args)
        {
            string parameters = SkipParameterLogging ? "[Skipped]" : FormatParameters(args.Method, args.Arguments);
            Log.Debug("CLI--> Enter: {Class}.{Method}({Parameters})",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                parameters);
        }

        public override void OnExit(MethodExecutionArgs args)
        {
            string returnValue = "[Skipped]";
            if (!SkipParameterLogging)
            {
                if (args.Method is MethodInfo methodInfo && typeof(Task).IsAssignableFrom(methodInfo.ReturnType) && methodInfo.ReturnType.IsGenericType)
                {
                    returnValue = $"(Task<{methodInfo.ReturnType.GetGenericArguments()[0].Name}>)";
                }
                else if (args.Method is MethodInfo methodInfoVoid && typeof(Task).IsAssignableFrom(methodInfoVoid.ReturnType))
                {
                    returnValue = "(Task)";
                }
                else if (args.ReturnValue != null)
                {
                    returnValue = args.ReturnValue.ToString() ?? "null";
                }
                else if ((args.Method as MethodInfo)?.ReturnType == typeof(void))
                {
                    returnValue = "(void)";
                }
                else
                {
                    returnValue = "null";
                }
            }

            Log.Debug("CLI<-- Exit:  {Class}.{Method} => {ReturnValue}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                returnValue);
        }

        public override void OnException(MethodExecutionArgs args)
        {
            Log.Error(args.Exception, "CLI!!! Exception in {Class}.{Method}: {ExceptionType} - {ExceptionMessage}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                args.Exception.GetType().Name,
                args.Exception.Message);
        }

        private string FormatParameters(MethodBase method, object[] arguments)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                string paramName = parameters[i].Name ?? $"param{i}";
                string paramValue = arguments[i]?.ToString() ?? "null";
                sb.Append($"{paramName}: {paramValue}");
            }
            return sb.ToString();
        }
    }
}
