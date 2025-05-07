// LibreSolvE.GUI/Logging/LoggingAspect.cs
using MethodBoundaryAspect.Fody.Attributes;
using Serilog;
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks; // Required for checking async methods

namespace LibreSolvE.GUI.AOP // Or your preferred namespace
{
    // Apply this attribute to methods you want to log
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class LogAttribute : OnMethodBoundaryAspect
    {
        // Flag to prevent logging parameters/return values for specific methods if needed
        public bool SkipParameterLogging { get; set; } = false;

        public override void OnEntry(MethodExecutionArgs args)
        {
            // Log method entry
            string parameters = SkipParameterLogging ? "[Skipped]" : FormatParameters(args.Method, args.Arguments);
            Log.Debug("--> Enter: {Class}.{Method}({Parameters})",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                parameters);
        }

        public override void OnExit(MethodExecutionArgs args)
        {
            // Log method exit
            string returnValue = "[Skipped]";
            if (!SkipParameterLogging)
            {
                // Check if the method is async Task<T>
                if (args.Method is MethodInfo methodInfo && typeof(Task).IsAssignableFrom(methodInfo.ReturnType) && methodInfo.ReturnType.IsGenericType)
                {
                    // For Task<T>, the result is in args.ReturnValue, but need to access it via dynamic or reflection carefully
                    // This might happen *before* the task is completed, so logging return value here might be premature for async.
                    // Consider logging task completion elsewhere if needed.
                    // For simplicity, we'll log the Task object itself or indicate it's a Task.
                    returnValue = $"(Task<{methodInfo.ReturnType.GetGenericArguments()[0].Name}>)";
                }
                // Check if the method is async Task (void)
                else if (args.Method is MethodInfo methodInfoVoid && typeof(Task).IsAssignableFrom(methodInfoVoid.ReturnType))
                {
                    returnValue = "(Task)";
                }
                // Regular return value
                else if (args.ReturnValue != null)
                {
                    returnValue = args.ReturnValue.ToString() ?? "null";
                    // Optional: Truncate long return values
                    // if (returnValue.Length > 100) returnValue = returnValue.Substring(0, 100) + "...";
                }
                else if ((args.Method as MethodInfo)?.ReturnType == typeof(void))
                {
                    returnValue = "(void)";
                }
                else
                {
                    returnValue = "null"; // Return value is null (e.g., reference type)
                }
            }

            Log.Debug("<-- Exit:  {Class}.{Method} => {ReturnValue}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                returnValue);
        }

        public override void OnException(MethodExecutionArgs args)
        {
            // Log exceptions
            Log.Error(args.Exception, "!!! Exception in {Class}.{Method}: {ExceptionType} - {ExceptionMessage}",
                args.Method.DeclaringType?.Name ?? "UnknownClass",
                args.Method.Name,
                args.Exception.GetType().Name,
                args.Exception.Message);
        }

        // Helper to format parameters
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
                // Optional: Truncate long parameter values
                // if (paramValue.Length > 50) paramValue = paramValue.Substring(0, 50) + "...";
                sb.Append($"{paramName}: {paramValue}");
            }
            return sb.ToString();
        }
    }
}
