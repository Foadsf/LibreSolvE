// LibreSolvE.Core/Parsing/ParsingException.cs
using System;

namespace LibreSolvE.Core.Parsing;

/// <summary>
/// Custom exception for parsing errors with enhanced information
/// </summary>
public class ParsingException : Exception
{
    public ParsingException(string message) : base(message) { }
    public ParsingException(string message, Exception innerException) : base(message, innerException) { }
}
