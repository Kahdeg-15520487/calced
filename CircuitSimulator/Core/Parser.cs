using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CircuitSimulator.LSP;

namespace CircuitSimulator.Core
{
    public class Parser
    {
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
            return new LookupTableGate(table, tableName);
        }
        private readonly List<Token> _tokens;
        private int _current;
        private readonly string _basePath;
        private readonly string _filePath;
        private readonly string _originalFilePath;
        private readonly int _level;

        public Parser(List<Token> tokens, string basePath, string filePath, string? originalFilePath = null, int level = 0)
        {
            _tokens = tokens;
            _current = 0;
            _basePath = basePath;
            _filePath = filePath;
            _originalFilePath = originalFilePath ?? filePath;
            _level = level;
        }

        public Dictionary<string, Circuit> ParseCircuits()
        {
            var circuits = new Dictionary<string, Circuit>();

            while (!IsAtEnd())
            {
                if (Match(TokenType.IMPORT))
                {
                    ParseImport(circuits);
                }
                else if (Match(TokenType.CIRCUIT))
                {
                    var definitionLine = Previous().Line;
                    var circuit = ParseCircuit(circuits, definitionLine);
                    circuits[circuit.Name] = circuit;
                }
                else
                {
                    throw new DSLInvalidSyntaxException(Peek().Line, Peek().Column, "Expected 'import' or 'circuit'");
                }
            }

            return circuits;
        }

        private void ParseImport(Dictionary<string, Circuit> circuits)
        {
            Consume(TokenType.STRING, "Expected string after 'import'");
            string filename = Previous().Value+".circuit";

            try
            {
                string importPath = Path.Combine(_basePath, filename);
                string importedDsl = File.ReadAllText(importPath);

                var lexer = new Lexer(importedDsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, _basePath, importPath, importPath, _level + 1);
                var importedCircuits = parser.ParseCircuits();

                foreach (var kvp in importedCircuits)
                {
                    circuits[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                throw new DSLImportException(filename, ex);
            }
        }

        private Circuit ParseCircuit(Dictionary<string, Circuit> circuits, int definitionLine)
        {
            Consume(TokenType.IDENTIFIER, "Expected circuit name");
            string circuitName = Previous().Value;

            var circuit = new Circuit
            {
                Name = circuitName,
                FilePath = _originalFilePath,
                DefinitionLine = definitionLine,
                Level = _level
            };

            Consume(TokenType.LBRACE, "Expected '{' after circuit name");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                if (Match(TokenType.INPUTS))
                {
                    int startLine = Previous().Line;
                    int startColumn = Previous().Column;
                    ParseInputs(circuit);
                    int endLine = Previous().Line;
                    circuit.Blocks["inputs"] = new BlockInfo { StartLine = startLine, StartColumn = startColumn, EndLine = endLine };
                }
                else if (Match(TokenType.OUTPUTS))
                {
                    int startLine = Previous().Line;
                    int startColumn = Previous().Column;
                    ParseOutputs(circuit);
                    int endLine = Previous().Line;
                    circuit.Blocks["outputs"] = new BlockInfo { StartLine = startLine, StartColumn = startColumn, EndLine = endLine };
                }
                else if (Match(TokenType.GATES))
                {
                    int startLine = Previous().Line;
                    int startColumn = Previous().Column;
                    ParseGates(circuit, circuits);
                    int endLine = Previous().Line;
                    circuit.Blocks["gates"] = new BlockInfo { StartLine = startLine, StartColumn = startColumn, EndLine = endLine };
                }
                else if (Match(TokenType.LOOKUP_TABLES))
                {
                    int startLine = Previous().Line;
                    int startColumn = Previous().Column;
                    var luts = ParseLookupTables();
                    foreach (var (lutName, lutBlock, lutTable) in luts)
                    {
                        circuit.Blocks[lutName] = lutBlock;
                        circuit.LookupTables[lutName] = lutTable;
                    }
                    int endLine = Previous().Line;
                    circuit.Blocks["lookup_tables"] = new BlockInfo { StartLine = startLine, StartColumn = startColumn, EndLine = endLine };
                }
                else if (Match(TokenType.CONNECTIONS))
                {
                    int startLine = Previous().Line;
                    ParseConnections(circuit);
                    int endLine = Previous().Line;
                    circuit.Blocks["connections"] = new BlockInfo { StartLine = startLine, EndLine = endLine };
                }
                else
                {
                    throw new DSLInvalidSyntaxException(Peek().Line, Peek().Column, "Expected block keyword");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after circuit body");

            return circuit;
        }

        private void ParseInputs(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'inputs'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                if (Match(TokenType.IDENTIFIER))
                {
                    string name = Previous().Value;
                    int defLine = Previous().Line;
                    int defCol = Previous().Column;

                    if (Match(TokenType.LBRACKET))
                    {
                        Consume(TokenType.NUMBER, "Expected number in array size");
                        int size = int.Parse(Previous().Value);
                        Consume(TokenType.RBRACKET, "Expected ']' after array size");

                        for (int i = 0; i < size; i++)
                        {
                            string indexedName = $"{name}[{i}]";
                            circuit.ExternalInputs[indexedName] = false;
                        }
                        circuit.InputNames.Add(new PortInfo { Name = name, BitWidth = size, DefinitionLine = defLine, DefinitionColumn = defCol });
                    }
                    else
                    {
                        circuit.ExternalInputs[name] = false;
                        circuit.InputNames.Add(new PortInfo { Name = name, BitWidth = 1, DefinitionLine = defLine, DefinitionColumn = defCol });
                    }
                }

                if (!Check(TokenType.RBRACE))
                {
                    Consume(TokenType.COMMA, "Expected ',' between inputs");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after inputs");
        }

        private void ParseOutputs(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'outputs'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected output name");
                string name = Previous().Value;
                int defLine = Previous().Line;
                int defCol = Previous().Column;

                if (Match(TokenType.LBRACKET))
                {
                    Consume(TokenType.NUMBER, "Expected number in array size");
                    int size = int.Parse(Previous().Value);
                    Consume(TokenType.RBRACKET, "Expected ']' after array size");

                    for (int i = 0; i < size; i++)
                    {
                        string indexedName = $"{name}[{i}]";
                        circuit.ExternalOutputs[indexedName] = null;
                    }
                    circuit.OutputNames.Add(new PortInfo { Name = name, BitWidth = size, DefinitionLine = defLine, DefinitionColumn = defCol });
                }
                else
                {
                    circuit.ExternalOutputs[name] = null;
                    circuit.OutputNames.Add(new PortInfo { Name = name, BitWidth = 1, DefinitionLine = defLine, DefinitionColumn = defCol });
                }

                if (!Check(TokenType.RBRACE))
                {
                    Consume(TokenType.COMMA, "Expected ',' between outputs");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after outputs");
        }

        private void ParseGates(Circuit circuit, Dictionary<string, Circuit> circuits)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'gates'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected gate name");
                string gateName = Previous().Value;
                int definitionLine = Previous().Line;
                int definitionColumn = Previous().Column;

                Consume(TokenType.EQUALS, "Expected '=' after gate name");

                Consume(TokenType.IDENTIFIER, "Expected gate type");
                string gateType = Previous().Value;

                Gate gate;
                if (gateType == "Circuit")
                {
                    Consume(TokenType.LPAREN, "Expected '(' after 'Circuit'");
                    Consume(TokenType.STRING, "Expected circuit name");
                    string circuitName = Previous().Value;
                    Consume(TokenType.RPAREN, "Expected ')' after circuit name");

                    if (!circuits.TryGetValue(circuitName, out var subCircuit))
                    {
                        throw new DSLInvalidGateException(gateName, $"Circuit '{circuitName}' not found");
                    }

                    gate = new CircuitGate(subCircuit);
                }
                else if (gateType == "LookupTable")
                {
                    Consume(TokenType.LPAREN, "Expected '(' after 'LookupTable'");
                    Consume(TokenType.STRING, "Expected table name");
                    string tableName = Previous().Value;
                    Consume(TokenType.RPAREN, "Expected ')' after table name");

                    gate = CreateLookupTableGate(tableName);
                }
                else
                {
                    // Regular gates like AND(), OR(), etc.
                    Consume(TokenType.LPAREN, "Expected '(' after gate type");
                    Consume(TokenType.RPAREN, "Expected ')' after gate type");

                    if (!GateFactory.TryGetValue(gateType, out var factory))
                    {
                        throw new DSLInvalidGateException(gateName, $"Unknown gate type '{gateType}'");
                    }
                    gate = factory();
                }

                gate.DefinitionLine = definitionLine;
                gate.DefinitionColumn = definitionColumn;
                circuit.AddGate(gateName, gate);

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after gates");
        }

        private List<(string lutName, BlockInfo lutBlock, Dictionary<string, bool[]> lutTable)> ParseLookupTables()
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'lookup_tables'");

            var blocks = new List<(string lutName, BlockInfo lutBlock, Dictionary<string, bool[]> lutTable)>();

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected table name");
                string tableName = Previous().Value;
                int defLine = Previous().Line;
                int defCol = Previous().Column;

                Consume(TokenType.EQUALS, "Expected '=' after table name");
                Consume(TokenType.LBRACE, "Expected '{' after '='");

                var table = new Dictionary<string, bool[]>();

                while (!Check(TokenType.RBRACE) && !IsAtEnd())
                {
                    Consume(TokenType.NUMBER, "Expected input pattern");
                    string input = Previous().Value;

                    Consume(TokenType.ARROW, "Expected '->' after input");

                    Consume(TokenType.NUMBER, "Expected output pattern");
                    string outputStr = Previous().Value;

                    var output = new bool[outputStr.Length];
                    for (int i = 0; i < outputStr.Length; i++)
                    {
                        // Treat output string as MSB first (leftmost digit is highest bit)
                        output[i] = (outputStr[outputStr.Length - 1 - i] == '1');
                    }

                    table[input] = output;

                    if (!Check(TokenType.RBRACE))
                    {
                        // Optional comma
                        Match(TokenType.COMMA);
                    }
                }

                LookupTables[tableName] = table;

                Consume(TokenType.RBRACE, "Expected '}' after table entries");
                blocks.Add((tableName, new BlockInfo { StartLine = defLine, StartColumn = defCol, EndLine = Previous().Line }, table));

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after lookup_tables");

            return blocks;
        }

        private void ParseConnections(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'connections'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                // Parse source
                string source = ParseSource();

                Consume(TokenType.ARROW, "Expected '->' in connection");

                // Parse target
                var (target, targetLine, targetColumn) = ParseTarget();

                // Parse the connection: source -> target
                // For now, implement basic connection parsing
                ParseConnection(circuit, source, target, targetLine, targetColumn);

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after connections");
        }

        private void ParseConnection(Circuit circuit, string source, string target, int targetLine, int targetColumn)
        {
            object sourceObj;
            Gate? targetGate = null;
            int targetInputIndex;

            // Parse source
            if (circuit.ExternalInputs.ContainsKey(source))
            {
                sourceObj = source; // external input name
            }
            else if (circuit.InputNames.Any(p => p.Name == source))
            {
                sourceObj = source; // multi-bit external input base name
            }
            else if (source.Contains('.'))
            {
                // gate.output format
                var parts = source.Split('.');
                if (parts.Length == 2)
                {
                    if (!circuit.NamedGates.TryGetValue(parts[0], out var gate))
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Source gate '{parts[0]}' not found");
                    }

                    if (parts[1] == "out")
                    {
                        sourceObj = gate;
                    }
                    else if (parts[1].StartsWith("out[") && parts[1].EndsWith("]"))
                    {
                        var indexStr = parts[1].Substring(4, parts[1].Length - 5);
                        if (int.TryParse(indexStr, out int outputIndex))
                        {
                            if (gate is CircuitGate cg)
                            {
                                if (outputIndex >= cg.Outputs.Count)
                                {
                                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Output index {outputIndex} out of range for gate '{parts[0]}'");
                                }
                                var outputGate = new SubcircuitOutputGate(cg, outputIndex);
                                var outputGateName = $"{parts[0]}_out_{outputIndex}";
                                circuit.AddGate(outputGateName, outputGate);
                                sourceObj = outputGate;
                            }
                            else if (gate is LookupTableGate ltg)
                            {
                                if (outputIndex >= ltg.Outputs.Count)
                                {
                                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Output index {outputIndex} out of range for gate '{parts[0]}'");
                                }
                                var outputGate = new LookupTableOutputGate(ltg, outputIndex);
                                var outputGateName = $"{parts[0]}_out_{outputIndex}";
                                circuit.AddGate(outputGateName, outputGate);
                                sourceObj = outputGate;
                            }
                            else
                            {
                                throw new DSLInvalidConnectionException($"{source} -> {target}", $"Gate '{parts[0]}' does not support indexed outputs");
                            }
                        }
                        else
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid output index: {indexStr}");
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid source format: {source}");
                    }
                }
                else if (parts.Length == 3 && parts[1] == "out")
                {
                    if (!circuit.NamedGates.TryGetValue(parts[0], out var gate))
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Source gate '{parts[0]}' not found");
                    }

                    string outputName = parts[2];
                    if (gate is CircuitGate cg)
                    {
                        int outputIndex = cg.OutputNames.IndexOf(outputName);
                        if (outputIndex == -1)
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Output name '{outputName}' not found in subcircuit '{parts[0]}'");
                        }
                        int bitWidth = cg.OutputBitWidths[outputIndex];
                        var outputGate = new SubcircuitOutputGate(cg, outputIndex, bitWidth);
                        var outputGateName = $"{parts[0]}_out_{outputIndex}";
                        circuit.AddGate(outputGateName, outputGate);
                        sourceObj = outputGate;
                    }
                    else if (gate is LookupTableGate ltg)
                    {
                        // For lookup tables, we don't have named outputs, so this might not apply
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Named outputs not supported for lookup table gate '{parts[0]}'");
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Gate '{parts[0]}' does not support named outputs");
                    }
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid source format: {source}");
                }
            }
            else
            {
                throw new DSLInvalidConnectionException($"{source} -> {target}", $"Unknown source: {source}");
            }

            // Parse target
            if (target.Contains('.'))
            {
                var parts = target.Split('.');
                if (parts.Length == 2 && parts[1].StartsWith("in[") && parts[1].EndsWith("]"))
                {
                    // Index syntax: gate.in[index]
                    var indexStr = parts[1].Substring(3, parts[1].Length - 4);
                    if (int.TryParse(indexStr, out targetInputIndex))
                    {
                        if (circuit.NamedGates.TryGetValue(parts[0], out targetGate))
                        {
                            // Connect
                            circuit.Connect(sourceObj, targetGate, targetInputIndex);
                        }
                        else
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Target gate '{parts[0]}' not found");
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid input index: {indexStr}");
                    }
                }
                else if (parts.Length == 3 && parts[1] == "in")
                {
                    // Name syntax: gate.in.name
                    string inputName = parts[2];
                    if (circuit.NamedGates.TryGetValue(parts[0], out targetGate))
                    {
                        if (targetGate is CircuitGate cg)
                        {
                            targetInputIndex = cg.InputNames.IndexOf(inputName);
                            if (targetInputIndex == -1)
                            {
                                throw new DSLInvalidConnectionException($"{source} -> {target}", $"Input name '{inputName}' not found in subcircuit '{parts[0]}'");
                            }
                            circuit.Connect(sourceObj, targetGate, targetInputIndex);
                        }
                        else
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Named inputs not supported for non-subcircuit gate '{parts[0]}'");
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Target gate '{parts[0]}' not found");
                    }
                }
                else if (parts.Length == 2 && parts[1] == "in")
                {
                    // Multi-bit input
                    if (circuit.NamedGates.TryGetValue(parts[0], out targetGate))
                    {
                        int targetBitWidth = targetGate.Inputs.Count;
                        int sourceBitWidth = 0;

                        // Check if source is multi-bit external input
                        if (circuit.ExternalInputs.ContainsKey(source))
                        {
                            var inputInfo = circuit.InputNames.FirstOrDefault(p => p.Name == source);
                            if (inputInfo != null)
                            {
                                sourceBitWidth = inputInfo.BitWidth;
                            }
                        }
                        else if (circuit.InputNames.Any(p => p.Name == source))
                        {
                            var inputInfo = circuit.InputNames.First(p => p.Name == source);
                            sourceBitWidth = inputInfo.BitWidth;
                        }
                        // Check if source is multi-bit gate output
                        else if (source.Contains('.') && circuit.NamedGates.TryGetValue(source.Split('.')[0], out var sourceGate) && source.Split('.')[1] == "out")
                        {
                            sourceBitWidth = sourceGate.Outputs.Count;
                        }

                        if (sourceBitWidth > 1 && sourceBitWidth == targetBitWidth)
                        {
                            // Expand connections
                            for (int i = 0; i < sourceBitWidth; i++)
                            {
                                string expandedSource = circuit.ExternalInputs.ContainsKey(source) ? $"{source}[{i}]" : $"{source}[{i}]";
                                string expandedTarget = $"{parts[0]}.in[{i}]";
                                ParseConnection(circuit, expandedSource, expandedTarget, targetLine, targetColumn);
                            }
                            return;
                        }
                        else if (sourceBitWidth != targetBitWidth)
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Bitwidth mismatch: {sourceBitWidth} vs {targetBitWidth}");
                        }
                        else
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Multi-bit connection requires matching bitwidths > 1");
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Target gate '{parts[0]}' not found");
                    }
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid target format: {target}");
                }
            }
            else if (circuit.OutputNames.Any(p => p.Name == target && p.BitWidth > 1))
            {
                var outputInfo = circuit.OutputNames.First(p => p.Name == target);
                if (sourceObj is Gate sourceGate && sourceGate.Outputs.Count == outputInfo.BitWidth)
                {
                    // Expand connections
                    for (int i = 0; i < outputInfo.BitWidth; i++)
                    {
                        string expandedSource = source.Contains('.') ? $"{source.Split('.')[0]}.out[{i}]" : $"{source}[{i}]";
                        string expandedTarget = $"{target}[{i}]";
                        ParseConnection(circuit, expandedSource, expandedTarget, targetLine, targetColumn);
                    }
                    return;
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", "Source must be a gate with matching output bitwidth");
                }
            }
            else if (circuit.ExternalOutputs.ContainsKey(target))
            {
                if (sourceObj is Gate sourceGate)
                {
                    circuit.ExternalOutputs[target] = sourceGate;
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", "External outputs must be connected to gate outputs");
                }
            }
            else
            {
                throw new DSLInvalidConnectionException(targetLine, targetColumn, $"{source} -> {target}", $"Unknown target: {target}");
            }
        }

        private string ParseSource()
        {
            Consume(TokenType.IDENTIFIER, "Expected source identifier");
            string source = Previous().Value;

            // Handle array notation like value[0]
            if (Match(TokenType.LBRACKET))
            {
                Consume(TokenType.NUMBER, "Expected array index");
                string index = Previous().Value;
                Consume(TokenType.RBRACKET, "Expected ']' after array index");
                source += $"[{index}]";
            }

            if (Match(TokenType.DOT))
            {
                Consume(TokenType.IDENTIFIER, "Expected 'out' after dot");
                if (Previous().Value != "out")
                {
                    throw new DSLInvalidSyntaxException(Previous().Line, Previous().Column, "Expected 'out' after dot in source");
                }

                if (Match(TokenType.LBRACKET))
                {
                    Consume(TokenType.NUMBER, "Expected output index");
                    string index = Previous().Value;
                    Consume(TokenType.RBRACKET, "Expected ']' after output index");
                    source += $".out[{index}]";
                }
                else if (Match(TokenType.DOT))
                {
                    Consume(TokenType.IDENTIFIER, "Expected output name");
                    string name = Previous().Value;
                    source += $".out.{name}";
                }
                else
                {
                    source += ".out";
                }
            }

            return source;
        }

        private (string target, int line, int column) ParseTarget()
        {
            Consume(TokenType.IDENTIFIER, "Expected target identifier");
            var targetToken = Previous();
            string target = targetToken.Value;

            // Handle array notation like sum[0]
            if (Match(TokenType.LBRACKET))
            {
                Consume(TokenType.NUMBER, "Expected array index");
                string index = Previous().Value;
                Consume(TokenType.RBRACKET, "Expected ']' after array index");
                target += $"[{index}]";
            }

            if (Match(TokenType.DOT))
            {
                Consume(TokenType.IDENTIFIER, "Expected 'in' after dot");
                if (Previous().Value != "in")
                {
                    throw new DSLInvalidSyntaxException(Previous().Line, Previous().Column, "Expected 'in' after dot in target");
                }

                if (Match(TokenType.LBRACKET))
                {
                    // Index syntax: in[index]
                    Consume(TokenType.NUMBER, "Expected input index");
                    string index = Previous().Value;
                    Consume(TokenType.RBRACKET, "Expected ']' after input index");
                    target += $".in[{index}]";
                }
                else if (Match(TokenType.DOT))
                {
                    // Name syntax: in.name
                    Consume(TokenType.IDENTIFIER, "Expected input name");
                    string name = Previous().Value;
                    target += $".in.{name}";
                }
                else
                {
                    target += ".in";
                }
            }

            return (target, targetToken.Line, targetToken.Column);
        }

        private bool Match(TokenType type)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
            return false;
        }

        private string TokenTypeToString(TokenType type)
        {
            switch (type)
            {
                case TokenType.LBRACE: return "{";
                case TokenType.RBRACE: return "}";
                case TokenType.LBRACKET: return "[";
                case TokenType.RBRACKET: return "]";
                case TokenType.LPAREN: return "(";
                case TokenType.RPAREN: return ")";
                case TokenType.ARROW: return "->";
                case TokenType.COMMA: return ",";
                case TokenType.EQUALS: return "=";
                case TokenType.DOT: return ".";
                case TokenType.IDENTIFIER: return "identifier";
                case TokenType.STRING: return "string";
                case TokenType.NUMBER: return "number";
                case TokenType.CIRCUIT: return "circuit";
                case TokenType.INPUTS: return "inputs";
                case TokenType.OUTPUTS: return "outputs";
                case TokenType.GATES: return "gates";
                case TokenType.CONNECTIONS: return "connections";
                case TokenType.LOOKUP_TABLES: return "lookup_tables";
                case TokenType.IMPORT: return "import";
                default: return type.ToString().ToLower();
            }
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
            {
                return Advance();
            }

            // Report error at the position of the previous token + its length
            var prevToken = Previous();
            int errorLine = prevToken.Line;
            int errorColumn = prevToken.Column + prevToken.Value.Length;

            throw new DSLInvalidSyntaxException(errorLine, errorColumn, $"Expected '{TokenTypeToString(type)}'");
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.EOF;
        }

        private Token Peek()
        {
            return _tokens[_current];
        }

        private Token Previous()
        {
            return _tokens[_current - 1];
        }
    }
}