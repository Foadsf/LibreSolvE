// Update the existing LibreSolvE.GUI/Logging/VariableStoreLogger.cs file
using LibreSolvE.Core.Evaluation;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.GUI.Logging
{
    /// <summary>
    /// A wrapper around VariableStore to add enhanced logging
    /// </summary>
    public class VariableStoreLogger
    {
        /// <summary>
        /// This is a static helper method to log the contents of a VariableStore
        /// </summary>
        public static void LogVariableStoreContents(string context, VariableStore store)
        {
            if (store == null)
            {
                Log.Warning("{Context}: VariableStore is null", context);
                return;
            }

            var varNames = store.GetAllVariableNames().ToList();
            Log.Debug("{Context}: VariableStore contains {Count} variables", context, varNames.Count);

            foreach (var name in varNames.OrderBy(n => n))
            {
                try
                {
                    double value = store.GetVariable(name);
                    string unit = store.GetUnit(name);
                    bool isExplicit = store.IsExplicitlySet(name);

                    Log.Debug("{Context}: Variable {Name} = {Value} {Unit} (Explicit: {IsExplicit})",
                        context, name, value, unit, isExplicit);
                }
                catch (Exception ex)
                {
                    Log.Error("{Context}: Error accessing variable {Name}: {Error}",
                        context, name, ex.Message);
                }
            }
        }
    }
}
