using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CircuitSimulator
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
    }

    public class RegexParser
    {
        internal static readonly Dictionary<string, Func<Gate>> GateFactory = new Dictionary<string, Func<Gate>>
        {
            ["AND"] = () => new AndGate(),
            ["OR"] = () => new OrGate(),
            ["NOT"] = () => new NotGate(),
            ["NAND"] = () => new NandGate(),
            ["NOR"] = () => new NorGate(),
            ["XOR"] = () => new XorGate(),
            ["XNOR"] = () => new XnorGate(),
            ["DFF"] = () => new DFlipFlop()
        };

        internal static readonly Dictionary<string, Dictionary<string, bool[]>> LookupTables = new Dictionary<string, Dictionary<string, bool[]>>();

        internal static Gate CreateLookupTableGate(string tableName)
        {
            if (!LookupTables.TryGetValue(tableName, out var table))
            {
                throw new DSLInvalidGateException(tableName, $"Lookup table '{tableName}' not found");
            }
            return new LookupTableGate(table);
        }

        internal static readonly Dictionary<Type, string> TypeToName = new Dictionary<Type, string>
        {
            [typeof(AndGate)] = "AND",
            [typeof(OrGate)] = "OR",
            [typeof(NotGate)] = "NOT",
            [typeof(NandGate)] = "NAND",
            [typeof(NorGate)] = "NOR",
            [typeof(XorGate)] = "XOR",
            [typeof(XnorGate)] = "XNOR",
            [typeof(DFlipFlop)] = "DFF"
        };

        public static Circuit Parse(string dslText, string basePath, string? circuitName = null, bool useNewParser = false)
        {
            if (useNewParser)
            {
                var lexer = new Lexer(dslText);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, basePath);
                var circuits = parser.ParseCircuits();

                // Return specific circuit if name provided, otherwise return the last one parsed
                if (circuitName != null && circuits.ContainsKey(circuitName))
                {
                    return circuits[circuitName];
                }
                return circuits.Values.LastOrDefault() ?? new Circuit();
            }
            else
            {
                var circuits = ParseCircuitsFromText(dslText, basePath);

                // Return specific circuit if name provided, otherwise return the last one parsed
                if (circuitName != null && circuits.ContainsKey(circuitName))
                {
                    return circuits[circuitName];
                }
                return circuits.Values.LastOrDefault() ?? new Circuit();
            }
        }

        private static Dictionary<string, Circuit> ParseCircuitsFromText(string dslText, string basePath)
        {
            var circuits = new Dictionary<string, Circuit>();
            var parseOrder = new List<string>(); // Track order of circuit definitions
            var lines = dslText.Split('\n')
                .Select(l => l.Split("//")[0].Trim()) // Remove comments
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();

            int i = 0;
            while (i < lines.Length)
            {
                if (lines[i].StartsWith("import "))
                {
                    // Parse import statement: import "filename.circuit"
                    var match = Regex.Match(lines[i], @"import ""([^""]+)""");
                    if (!match.Success)
                    {
                        throw new DSLInvalidSyntaxException(0, 0, "Invalid import statement. Expected 'import \"filename\"'");
                    }
                    var importFile = match.Groups[1].Value;
                    try
                    {
                        var importPath = Path.Combine(basePath, importFile);
                        var importedDsl = File.ReadAllText(importPath);
                        var importedCircuits = ParseCircuitsFromText(importedDsl, basePath);
                        foreach (var kvp in importedCircuits)
                        {
                            circuits[kvp.Key] = kvp.Value;
                            // Don't add imported circuits to parse order
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new DSLImportException(importFile, ex);
                    }
                    i++;
                }
                else if (lines[i].StartsWith("circuit "))
                {
                    // Parse circuit name
                    var match = Regex.Match(lines[i], @"circuit (\w+)");
                    if (!match.Success)
                    {
                        throw new DSLInvalidSyntaxException(0, 0, "Invalid circuit declaration. Expected 'circuit <name>'");
                    }
                    var currentCircuitName = match.Groups[1].Value;
                    var circuit = new Circuit();
                    circuits[currentCircuitName] = circuit;
                    parseOrder.Add(currentCircuitName); // Track local circuit definition order
                    i++; // skip {

                    // Parse blocks
                    while (i < lines.Length && !lines[i].StartsWith("}"))
                    {
                        if (lines[i].StartsWith("inputs {"))
                        {
                            var content = lines[i].Substring("inputs {".Length).TrimEnd('}').Trim();
                            var inputs = content.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                            foreach (var input in inputs)
                            {
                                if (input.Contains("["))
                                {
                                    var inputMatch = Regex.Match(input, @"(\w+)\[(\d+)\]");
                                    if (!inputMatch.Success)
                                    {
                                        throw new DSLInvalidSyntaxException(0, 0, "Invalid array input syntax. Expected 'name[size]'");
                                    }
                                    var name = inputMatch.Groups[1].Value;
                                    try
                                    {
                                        var size = int.Parse(inputMatch.Groups[2].Value);
                                        for (int j = 0; j < size; j++)
                                        {
                                            circuit.ExternalInputs[$"{name}[{j}]"] = false;
                                        }
                                    }
                                    catch (FormatException)
                                    {
                                        throw new DSLInvalidSyntaxException(0, 0, "Invalid size in array input. Expected integer");
                                    }
                                }
                                else
                                {
                                    circuit.ExternalInputs[input] = false;
                                }
                            }
                            i++;
                        }
                        else if (lines[i].StartsWith("outputs {"))
                        {
                            var content = lines[i].Substring("outputs {".Length).TrimEnd('}').Trim();
                            var outputs = content.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                            foreach (var output in outputs)
                            {
                                circuit.ExternalOutputs[output] = null;
                            }
                            i++;
                        }
                        else if (lines[i] == "gates {")
                        {
                            i = ParseGates(circuit, lines, i + 1, circuits, basePath);
                        }
                        else if (lines[i] == "lookup_tables {")
                        {
                            i = ParseLookupTables(lines, i + 1);
                        }
                        else if (lines[i] == "connections {")
                        {
                            i = ParseConnections(circuit, lines, i + 1);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    i++; // skip }
                }
                else
                {
                    // Handle unexpected lines
                    i++;
                }
            }

            return circuits;
        }

        private static int ParseInputs(Circuit circuit, string[] lines, int start)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var inputs = lines[i].Split(',').Select(s => s.Trim().TrimEnd('}')).Where(s => !string.IsNullOrEmpty(s));
                foreach (var input in inputs)
                {
                    // Handle arrays like a[4]
                    if (input.Contains("["))
                    {
                        var match = Regex.Match(input, @"(\w+)\[(\d+)\]");
                        var name = match.Groups[1].Value;
                        var size = int.Parse(match.Groups[2].Value);
                        for (int j = 0; j < size; j++)
                        {
                            circuit.ExternalInputs[$"{name}[{j}]"] = false;
                        }
                    }
                    else
                    {
                        circuit.ExternalInputs[input] = false;
                    }
                }
                i++;
            }
            return i + 1; // skip }
        }

        private static int ParseOutputs(Circuit circuit, string[] lines, int start)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var outputs = lines[i].Split(',').Select(s => s.Trim().TrimEnd('}')).Where(s => !string.IsNullOrEmpty(s));
                foreach (var output in outputs)
                {
                    // Outputs will be connected later
                    circuit.ExternalOutputs[output] = null; // placeholder
                }
                i++;
            }
            return i + 1;
        }

        private static int ParseGates(Circuit circuit, string[] lines, int start, Dictionary<string, Circuit> circuits, string basePath)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var match = Regex.Match(lines[i], @"(\w+)\s*=\s*(\w+)\(([^)]*)\)");
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var type = match.Groups[2].Value;
                    var args = match.Groups[3].Value.Trim();
                    if (type == "Circuit")
                    {
                        var circuitRef = args.Trim('"');
                        
                        if (!circuits.ContainsKey(circuitRef))
                        {
                            throw new DSLInvalidGateException(name, $"Circuit '{circuitRef}' not found in current file");
                        }
                        
                        var subCircuit = circuits[circuitRef];
                        var gate = new CircuitGate(subCircuit);
                        circuit.AddGate(name, gate);
                    }
                    else if (type == "LookupTable")
                    {
                        var tableName = args.Trim('"');
                        var gate = CreateLookupTableGate(tableName);
                        circuit.AddGate(name, gate);
                    }
                    else if (GateFactory.TryGetValue(type, out var factory))
                    {
                        var gate = factory();
                        circuit.AddGate(name, gate);
                    }
                    else
                    {
                        throw new DSLInvalidGateException(name, $"Unknown gate type '{type}'");
                    }
                }
                i++;
            }
            return i + 1;
        }

        private static int ParseConnections(Circuit circuit, string[] lines, int start)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var inputMatch = Regex.Match(lines[i], @"(.+?)\s*->\s*(.+?)\.in\[(\d+)\]");
                if (inputMatch.Success)
                {
                    var source = inputMatch.Groups[1].Value.Trim();
                    var target = inputMatch.Groups[2].Value.Trim();
                    int index;
                    try
                    {
                        index = int.Parse(inputMatch.Groups[3].Value);
                    }
                    catch (FormatException)
                    {
                        throw new DSLInvalidConnectionException(lines[i], "Invalid input index");
                    }

                    // Handle .out suffix for regular gates
                    if (source.EndsWith(".out"))
                    {
                        source = source.Substring(0, source.Length - 4);
                    }

                    object? sourceObj = null;
                    if (circuit.NamedGates.TryGetValue(source, out var sourceGate))
                    {
                        sourceObj = sourceGate;
                    }
                    else if (circuit.ExternalInputs.ContainsKey(source))
                    {
                        sourceObj = source;
                    }
                    // Handle subcircuit outputs: subcircuit.out[index]
                    else if (source.Contains(".out["))
                    {
                        var parts = source.Split(new[] { ".out[" }, StringSplitOptions.None);
                        if (parts.Length == 2 && parts[1].EndsWith("]"))
                        {
                            var gateName = parts[0];
                            int outputIndex;
                            try
                            {
                                outputIndex = int.Parse(parts[1].TrimEnd(']'));
                            }
                            catch (FormatException)
                            {
                                throw new DSLInvalidConnectionException(lines[i], "Invalid output index in source");
                            }
                            if (circuit.NamedGates.TryGetValue(gateName, out var gate) && (gate is CircuitGate || gate is LookupTableGate))
                            {
                                int maxOutputs = gate is CircuitGate circuitGate ? circuitGate.Outputs.Count : ((LookupTableGate)gate).Outputs.Count;
                                if (outputIndex >= maxOutputs)
                                {
                                    throw new DSLInvalidConnectionException(lines[i], $"Output index {outputIndex} out of range for gate '{gateName}'");
                                }
                                
                                if (gate is CircuitGate cg)
                                {
                                    // Create a SubcircuitOutputGate for this specific output
                                    var outputGate = new SubcircuitOutputGate(cg, outputIndex);
                                    var outputGateName = $"{gateName}_out_{outputIndex}";
                                    circuit.AddGate(outputGateName, outputGate);
                                    sourceObj = outputGate;
                                }
                                else if (gate is LookupTableGate ltg)
                                {
                                    // Create a LookupTableOutputGate for this specific output
                                    var outputGate = new LookupTableOutputGate(ltg, outputIndex);
                                    var outputGateName = $"{gateName}_out_{outputIndex}";
                                    circuit.AddGate(outputGateName, outputGate);
                                    sourceObj = outputGate;
                                }
                            }
                        }
                    }

                    if (sourceObj == null || !circuit.NamedGates.TryGetValue(target, out var targetGate))
                    {
                        throw new DSLInvalidConnectionException(lines[i], "Invalid source or target in connection");
                    }
                    circuit.Connect(sourceObj, targetGate, index);
                }
                else
                {
                    var outputMatch = Regex.Match(lines[i], @"(.+)\s*->\s*(.+)");
                    if (outputMatch.Success)
                    {
                        var source = outputMatch.Groups[1].Value.Trim();
                        var target = outputMatch.Groups[2].Value.Trim();

                        // Handle .out suffix for regular gates
                        if (source.EndsWith(".out"))
                        {
                            source = source.Substring(0, source.Length - 4);
                        }

                        // Handle subcircuit outputs: subcircuit.out[index]
                        int outputIndex = 0;
                        if (source.Contains(".out["))
                        {
                            var parts = source.Split(new[] { ".out[" }, StringSplitOptions.None);
                            if (parts.Length == 2 && parts[1].EndsWith("]"))
                            {
                                source = parts[0];
                                try
                                {
                                    outputIndex = int.Parse(parts[1].TrimEnd(']'));
                                }
                                catch (FormatException)
                                {
                                    throw new DSLInvalidConnectionException(lines[i], "Invalid output index in source");
                                }
                            }
                        }

                        if (!circuit.NamedGates.TryGetValue(source, out var sourceGate) || !circuit.ExternalOutputs.ContainsKey(target))
                        {
                            throw new DSLInvalidConnectionException(lines[i], "Invalid source or target in connection");
                        }

                        // For subcircuits and lookup tables, we need to create a wrapper that extracts the specific output
                        if ((sourceGate is CircuitGate circuitGate && outputIndex < circuitGate.Outputs.Count) ||
                            (sourceGate is LookupTableGate lookupTableGate && outputIndex < lookupTableGate.Outputs.Count))
                        {
                            if (sourceGate is CircuitGate cg)
                            {
                                // Create a SubcircuitOutputGate for this specific output
                                var outputGate = new SubcircuitOutputGate(cg, outputIndex);
                                circuit.AddGate($"{source}_out_{outputIndex}", outputGate);
                                circuit.ExternalOutputs[target] = outputGate;
                            }
                            else if (sourceGate is LookupTableGate ltg)
                            {
                                // Create a LookupTableOutputGate for this specific output
                                var outputGate = new LookupTableOutputGate(ltg, outputIndex);
                                circuit.AddGate($"{source}_out_{outputIndex}", outputGate);
                                circuit.ExternalOutputs[target] = outputGate;
                            }
                        }
                        else if (sourceGate is CircuitGate)
                        {
                            throw new DSLInvalidConnectionException(lines[i], $"Output index {outputIndex} out of range for subcircuit '{source}'");
                        }
                        else if (sourceGate is LookupTableGate)
                        {
                            throw new DSLInvalidConnectionException(lines[i], $"Output index {outputIndex} out of range for lookup table '{source}'");
                        }
                        else
                        {
                            circuit.ExternalOutputs[target] = sourceGate;
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException(lines[i], "Invalid connection syntax");
                    }
                }
                i++;
            }
            return i + 1;
        }

        private static int ParseLookupTables(string[] lines, int start)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var match = Regex.Match(lines[i], @"(\w+)\s*=\s*\{");
                if (match.Success)
                {
                    var tableName = match.Groups[1].Value;
                    var table = new Dictionary<string, bool[]>();
                    
                    i++; // skip the opening {
                    
                    // Parse table entries
                    while (i < lines.Length && lines[i] != "}")
                    {
                        var entryMatch = Regex.Match(lines[i], @"(\d+)\s*->\s*(\d+)");
                        if (entryMatch.Success)
                        {
                            var input = entryMatch.Groups[1].Value;
                            var outputStr = entryMatch.Groups[2].Value;
                            
                            // Convert output binary string to bool array
                            var output = new bool[outputStr.Length];
                            for (int j = 0; j < outputStr.Length; j++)
                            {
                                if (outputStr[j] == '1')
                                    output[j] = true;
                                else if (outputStr[j] == '0')
                                    output[j] = false;
                                else
                                    throw new DSLInvalidSyntaxException(0, 0, $"Invalid output bit '{outputStr[j]}'. Expected '0' or '1'");
                            }
                            
                            table[input] = output;
                        }
                        else if (!string.IsNullOrWhiteSpace(lines[i]) && lines[i] != "}")
                        {
                            throw new DSLInvalidSyntaxException(0, 0, "Invalid lookup table entry. Expected 'input -> output'");
                        }
                        i++;
                    }
                    
                    LookupTables[tableName] = table;
                }
                else if (!string.IsNullOrWhiteSpace(lines[i]) && lines[i] != "}")
                {
                    throw new DSLInvalidSyntaxException(0, 0, "Invalid lookup table definition. Expected 'name = {'");
                }
                i++;
            }
            return i + 1;
        }
    }
}