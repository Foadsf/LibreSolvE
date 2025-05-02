// LibreSolvE.Core/Evaluation/UnitParser.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection; // For robust quantity type check
using System.Text.RegularExpressions;
using UnitsNet;
using UnitsNet.Units;

namespace LibreSolvE.Core.Evaluation;

/// <summary>
/// Utility class to extract units from source text and parse unit strings.
/// </summary>
public static class UnitParser
{
    #region Static Data

    // Regex to find assignment start (ID followed by = or :=)
    private static readonly Regex AssignmentRegex =
        new Regex(@"^\s*(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*(:=|=)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to find units within square brackets (prioritized)
    private static readonly Regex UnitInBracketsRegex = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);
    // Regex to find units within standard EES comments brackets
    private static readonly Regex UnitInCommentRegex = new Regex(@"(?:\{|\""|//).*?\[([^\]]+)\]", RegexOptions.Compiled);


    // Dictionary mapping quantity Types TO THEIR UNIT ENUM TYPES
    // Essential for providing context to the parser. Add more as needed.
    private static readonly Dictionary<Type, Type> QuantityToUnitEnum = new Dictionary<Type, Type>
    {
        { typeof(Length), typeof(LengthUnit) },
        { typeof(Mass), typeof(MassUnit) },
        { typeof(Duration), typeof(DurationUnit) }, // Time
        { typeof(Temperature), typeof(TemperatureUnit) },
        { typeof(Pressure), typeof(PressureUnit) },
        { typeof(Energy), typeof(EnergyUnit) },
        { typeof(Power), typeof(PowerUnit) },
        { typeof(Volume), typeof(VolumeUnit) },
        { typeof(Area), typeof(AreaUnit) },
        { typeof(Speed), typeof(SpeedUnit) },
        { typeof(Acceleration), typeof(AccelerationUnit) },
        { typeof(Force), typeof(ForceUnit) },
        { typeof(Angle), typeof(AngleUnit) },
        { typeof(Frequency), typeof(FrequencyUnit) },
        { typeof(MassFlow), typeof(MassFlowUnit) },
        { typeof(VolumeFlow), typeof(VolumeFlowUnit) },
        { typeof(Density), typeof(DensityUnit) },
        { typeof(SpecificEnergy), typeof(SpecificEnergyUnit) },
        { typeof(SpecificEntropy), typeof(SpecificEntropyUnit) },
        { typeof(Ratio), typeof(RatioUnit) } // For dimensionless Eff
        // Add thermodynamic properties, viscosity, conductivity etc. as needed
    };

    // Manual mappings for complex units or problematic abbreviations.
    private static readonly Dictionary<string, string> UnitMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "kJ/kg-K", "kJ/kg·K" }, // Replace '-' with '·' for UnitsNet SpecificEntropy
        { "W/m-K", "W/m·K" },     // Replace '-' with '·' for UnitsNet ThermalConductivity
        { "m/s^2", "m/s²" },      // Use superscript for acceleration
        { "ft/s^2", "ft/s²" },
        { "m^2", "m²" },
        { "ft^2", "ft²" },
        { "m^3", "m³" },
        { "ft^3", "ft³" },
        { "kg*m/s^2", "N" },      // Map compound to standard abbreviation
        { "lbm", "lb" },          // Common alias for pound mass
        { "lb", "lbf" },          // If lb means lbf, map it explicitly
        // Map potentially ambiguous 'hp' to the desired default (e.g., Mechanical)
        // The parser might recognize 'hp(I)', 'hp(M)', 'hp(E)' etc. if needed
        { "hp", "hp(M)" }
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Extracts unit annotations from source code lines.
    /// Prioritizes units in brackets [] over units in comments like //[unit] or {"[unit]"}.
    /// </summary>
    public static Dictionary<string, string> ExtractUnitsFromSource(string sourceText)
    {
        var variableUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = sourceText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string? currentVarForUnit = null;

        foreach (var line in lines)
        {
            var assignmentMatch = AssignmentRegex.Match(line);
            string? unitFound = null;

            if (assignmentMatch.Success)
            {
                currentVarForUnit = assignmentMatch.Groups["var"].Value;
            }

            if (currentVarForUnit != null)
            {
                // Check for [unit] first
                var bracketMatch = UnitInBracketsRegex.Match(line);
                if (bracketMatch.Success)
                {
                    unitFound = bracketMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // If not found, check for //[unit], {"[unit]"}, or ""[unit]""
                    var commentMatch = UnitInCommentRegex.Match(line);
                    if (commentMatch.Success)
                    {
                        // Group 1 captures content inside brackets within a comment
                        unitFound = commentMatch.Groups[1].Value.Trim();
                    }
                }

                if (unitFound != null)
                {
                    Console.WriteLine($"Debug UnitParser: Found unit '[{unitFound}]' for variable '{currentVarForUnit}' on line: {line.Trim()}");
                    variableUnits[currentVarForUnit] = unitFound;
                    currentVarForUnit = null; // Reset after finding unit for this var
                }
            }

            // Reset currentVar if line is not empty/comment/whitespace and not an assignment start
            if (!string.IsNullOrWhiteSpace(line) && !assignmentMatch.Success &&
                !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("{") && !line.TrimStart().StartsWith("\""))
            {
                currentVarForUnit = null;
            }
        }
        return variableUnits;
    }

    /// <summary>
    /// Applies the extracted units to the VariableStore.
    /// </summary>
    public static void ApplyUnitsToVariableStore(VariableStore variableStore, Dictionary<string, string> units)
    {
        foreach (var kvp in units)
        {
            variableStore.SetUnit(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Parses a unit string, using mappings and iterating through known quantity types.
    /// </summary>
    /// <returns>A tuple containing the parsed unit Enum and its QuantityInfo.</returns>
    /// <exception cref="UnitsNet.UnitNotFoundException">Thrown if the unit string cannot be parsed.</exception>
    public static (Enum UnitEnum, QuantityInfo QuantityInfo) ParseUnitString(string unitStr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitStr);
        string originalUnitStr = unitStr;
        string unitToParse = unitStr;

        // Apply manual mapping first
        if (UnitMappings.TryGetValue(unitStr, out string? mappedUnit) && mappedUnit != null)
        {
            unitToParse = mappedUnit;
            Console.WriteLine($"Debug UnitParser: Mapped '{originalUnitStr}' to '{unitToParse}' for parsing.");
        }

        // Iterate through known Quantity Types and their corresponding Unit Enum Types
        foreach (var kvp in QuantityToUnitEnum)
        {
            // Type quantityType = kvp.Key; // Not directly needed for parsing
            Type unitEnumType = kvp.Value;

            // Attempt parsing using the specific Unit Enum Type for context
            if (UnitsNet.UnitParser.Default.TryParse(unitToParse, unitEnumType, CultureInfo.InvariantCulture, out Enum? unitEnum) && unitEnum != null)
            {
                // Parsed successfully to an Enum value. Now get QuantityInfo.
                try
                {
                    // Create a dummy quantity to get info
                    IQuantity quantity = Quantity.From(1.0, unitEnum);
                    return (unitEnum, quantity.QuantityInfo);
                }
                catch (Exception ex)
                {
                    // Should generally not happen if TryParse succeeded, but catch defensively
                    Console.WriteLine($"    Warning: Parsed '{unitToParse}' to {unitEnum} but failed QuantityInfo step: {ex.Message}");
                    // Continue loop in case another quantity type works (unlikely)
                }
            }
        }

        // If loop completes without returning, parsing failed.
        throw new UnitsNet.UnitNotFoundException($"Unit '{originalUnitStr}' (processed as '{unitToParse}') was not recognized by UnitsNet for any known quantity type.");
    }

    #endregion
}
