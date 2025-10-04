using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CircuitSimulator.Core
{
    // Custom exception classes for DSL parsing errors
    public class DSLParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public DSLParseException(string message) : base(message) { }
        public DSLParseException(string message, Exception innerException) : base(message, innerException) { }
        public DSLParseException(int line, int column, string message) : base(message)
        {
            Line = line;
            Column = column;
        }
        public DSLParseException(int line, int column, string message, Exception innerException) : base(message, innerException)
        {
            Line = line;
            Column = column;
        }
    }

    public class DSLImportException : DSLParseException
    {
        public DSLImportException(string fileName, Exception innerException)
            : base($"Failed to import file '{fileName}': {innerException.Message}", innerException) { }
    }

    public class DSLInvalidSyntaxException : DSLParseException
    {
        public DSLInvalidSyntaxException(int line, int column, string reason)
            : base(line, column, $"Invalid syntax at line {line}, column {column}: {reason}")
        {
        }
    }

    public class DSLInvalidGateException : DSLParseException
    {
        public DSLInvalidGateException(string gateName, string reason)
            : base($"Invalid gate definition for '{gateName}': {reason}") { }
        public DSLInvalidGateException(string gateName, string reason, Exception innerException)
            : base($"Invalid gate definition for '{gateName}': {reason}", innerException) { }
    }

    public class DSLInvalidConnectionException : DSLParseException
    {
        public DSLInvalidConnectionException(string connection, string reason)
            : base($"Invalid connection '{connection}': {reason}") { }

        public DSLInvalidConnectionException(int line, int column, string connection, string reason)
            : base(line, column, $"Invalid connection '{connection}': {reason}") { }
    }
}