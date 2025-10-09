using System.Text.Json;
using CircuitSimulator.Core;
using CircuitSimulator.LSP;

namespace CircuitSimulator
{
    class Program
    {
        static bool[] ParseMultiBitValue(string valueStr)
        {
            string binaryStr;

            if (valueStr.StartsWith("b"))
            {
                // Binary: b10101
                binaryStr = valueStr.Substring(1);
            }
            else if (valueStr.StartsWith("h"))
            {
                // Hexadecimal: h1a
                var hexStr = valueStr.Substring(1);
                var decimalValue = Convert.ToInt32(hexStr, 16);
                binaryStr = Convert.ToString(decimalValue, 2);
            }
            else if (valueStr.StartsWith("d"))
            {
                // Decimal: d12
                var decimalStr = valueStr.Substring(1);
                var decimalValue = int.Parse(decimalStr);
                binaryStr = Convert.ToString(decimalValue, 2);
            }
            else
            {
                // Default to decimal
                var decimalValue = int.Parse(valueStr);
                binaryStr = Convert.ToString(decimalValue, 2);
            }

            // Convert binary string to bool array (LSB first)
            var bits = new bool[binaryStr.Length];
            for (int i = 0; i < binaryStr.Length; i++)
            {
                bits[i] = binaryStr[binaryStr.Length - 1 - i] == '1';
            }

            return bits;
        }

        static void SetMultiBitInput(Circuit circuit, string inputName, bool[] bits)
        {
            // Determine expected bitwidth
            int expectedBitWidth = 0;
            if (circuit.ExternalInputs.ContainsKey(inputName))
            {
                // Single-bit input
                expectedBitWidth = 1;
            }
            else
            {
                // Multi-bit input, count [i]
                while (circuit.ExternalInputs.ContainsKey($"{inputName}[{expectedBitWidth}]"))
                {
                    expectedBitWidth++;
                }
            }

            if (expectedBitWidth == 0)
            {
                throw new ArgumentException($"Input {inputName} not found in circuit");
            }

            // Adjust bits to match expected bitwidth by padding left with 0
            if (bits.Length < expectedBitWidth)
            {
                var paddedBits = new bool[expectedBitWidth];
                bits.CopyTo(paddedBits, 0);
                // Left padding with false (0) is already done since array is initialized to false
                bits = paddedBits;
            }
            else if (bits.Length > expectedBitWidth)
            {
                throw new ArgumentException($"Supplied input bitwidth ({bits.Length}) does not match circuit input bitwidth ({expectedBitWidth})");
            }

            // Set the inputs
            if (expectedBitWidth == 1 && circuit.ExternalInputs.ContainsKey(inputName))
            {
                circuit.ExternalInputs[inputName] = bits[0];
            }
            else
            {
                for (int i = 0; i < bits.Length; i++)
                {
                    var bitInputName = $"{inputName}[{i}]";
                    circuit.ExternalInputs[bitInputName] = bits[i];
                }
            }
        }

        static void RunVerifyMode(string dslFile, string basePath)
        {
            // LSP verification mode - return JSON diagnostics
            var diagnostics = new List<DiagnosticInfo>();

            try
            {
                var dsl = File.ReadAllText(dslFile);
                var lexer = new Lexer(dsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, basePath, dslFile);
                var circuits = parser.ParseCircuits();
                // If parsing succeeds, no diagnostics
            }
            catch (DSLParseException ex)
            {
                // Extract the specific error message
                string message = ex.Message;

                // For syntax errors with position info, extract just the reason
                if (message.Contains("Invalid syntax at line ") && message.Contains(": "))
                {
                    // Extract the part after "Invalid syntax at line X, column Y: "
                    int colonIndex = message.LastIndexOf(": ");
                    if (colonIndex > 0)
                    {
                        message = message.Substring(colonIndex + 2);
                    }
                }
                // For other errors (connection, gate, etc.), use the full message

                diagnostics.Add(new DiagnosticInfo
                {
                    Message = message,
                    Line = ex.Line > 0 ? ex.Line - 1 : 0, // Convert to 0-based for LSP, default to 0 if no position
                    Column = ex.Column > 0 ? ex.Column - 1 : 0, // Convert to 0-based for LSP, default to 0 if no position
                    Length = Math.Max(1, message.Length / 10), // Approximate length
                    Severity = "error"
                });
            }
            catch (Exception ex)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Message = $"Unexpected error: {ex.Message}",
                    Line = 0,
                    Column = 0,
                    Length = 1,
                    Severity = "error"
                });
            }

            Console.WriteLine(JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = false }));
        }

        static void RunInfoMode(string dslFile, string basePath, string? originalFilePath = null)
        {
            // LSP info mode - return JSON circuit definitions
            var circuitInfos = new List<CircuitInfo>();

            try
            {
                var dsl = File.ReadAllText(dslFile);
                var lexer = new Lexer(dsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, basePath, dslFile, originalFilePath);
                var circuits = parser.ParseCircuits();

                foreach (var circuitEntry in circuits)
                {
                    circuitInfos.Add(new CircuitInfo
                    {
                        Name = circuitEntry.Key,
                        Inputs = circuitEntry.Value.InputNames,
                        Outputs = circuitEntry.Value.OutputNames,
                        FilePath = circuitEntry.Value.FilePath,
                        DefinitionLine = circuitEntry.Value.DefinitionLine,
                        Level = circuitEntry.Value.Level,
                        Gates = circuitEntry.Value.NamedGates.Where(g => !string.IsNullOrEmpty(g.Value.Type)).ToDictionary(g => g.Key, g => new GateInfo { Type = g.Value.Type, DefinitionLine = g.Value.DefinitionLine, DefinitionColumn = g.Value.DefinitionColumn }),
                        LookupTables = circuitEntry.Value.LookupTables.ToDictionary(lut => lut.Key, lut =>
                        {
                            var inputWidth = lut.Value.Keys.FirstOrDefault()?.Length ?? 0;
                            var outputWidth = lut.Value.Values.FirstOrDefault()?.Length ?? 0;
                            var defLine = circuitEntry.Value.Blocks.ContainsKey(lut.Key) ? circuitEntry.Value.Blocks[lut.Key].StartLine : 0;
                            var defCol = circuitEntry.Value.Blocks.ContainsKey(lut.Key) ? circuitEntry.Value.Blocks[lut.Key].StartColumn : 0;
                            circuitEntry.Value.Blocks.Remove(lut.Key);
                            return new LookupTableInfo
                            {
                                Name = lut.Key,
                                DefinitionLine = defLine,
                                DefinitionColumn = defCol,
                                InputWidth = inputWidth,
                                OutputWidth = outputWidth,
                                TruthTable = lut.Value.ToDictionary(entry => entry.Key, entry => string.Join("", entry.Value.Select(b => b ? "1" : "0")))
                            };
                        }),
                        Blocks = circuitEntry.Value.Blocks
                    });
                }
            }
            catch (Exception)
            {
                // If parsing fails, return empty list
                circuitInfos = new List<CircuitInfo>();
            }

            Console.WriteLine(JsonSerializer.Serialize(circuitInfos, new JsonSerializerOptions { WriteIndented = false }));
        }

        static void RunSynthesizeMode(string expression, string? outFile)
        {
            try
            {
                var builder = new Synthesizer();
                string synthesizedDsl = builder.GenerateDSL("SynthesizedCircuit", expression);

                // Verify the synthesized DSL by parsing it
                try
                {
                    var lexer = new Lexer(synthesizedDsl);
                    var tokens = lexer.Tokenize().ToList();
                    var parser = new Parser(tokens, ".", "synthesized.circuit");
                    var circuits = parser.ParseCircuits();
                    // If parsing succeeds, the DSL is valid
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"Synthesis error: Generated DSL is invalid - {parseEx.Message}");
                    Console.WriteLine("Generated DSL:");
                    Console.WriteLine(synthesizedDsl);
                    return;
                }

                if (outFile != null)
                {
                    File.WriteAllText(outFile, synthesizedDsl);
                    Console.WriteLine($"Synthesized circuit saved to {outFile}");
                }
                else
                {
                    Console.WriteLine(synthesizedDsl);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Synthesis error: {ex.Message}");
            }
        }

        static void RunTokensMode(string? dslFile)
        {
            string dsl;
            if (dslFile != null)
            {
                dsl = File.ReadAllText(dslFile);
            }
            else
            {
                // Read from stdin
                dsl = Console.In.ReadToEnd();
            }

            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize(skipCommentToken: false).ToList();

            // Semantic token types: ['circuitKeyword', 'circuitOperator', 'circuitFunction', 'comment', 'string', 'identifier']
            var gateNames = new HashSet<string> { "AND", "OR", "NOT", "NAND", "NOR", "XOR", "XNOR", "DFF", "Circuit", "LookupTable" };

            var semanticTokens = new List<int[]>();
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.EOF) continue;

                int typeIndex;
                switch (token.Type)
                {
                    case TokenType.CIRCUIT:
                    case TokenType.INPUTS:
                    case TokenType.OUTPUTS:
                    case TokenType.GATES:
                    case TokenType.CONNECTIONS:
                    case TokenType.LOOKUP_TABLES:
                    case TokenType.IMPORT:
                        typeIndex = 0; // circuitKeyword
                        break;
                    case TokenType.IDENTIFIER:
                        if (gateNames.Contains(token.Value))
                        {
                            typeIndex = 2; // circuitFunction
                        }
                        else
                        {
                            typeIndex = 5; // identifier
                        }
                        break;
                    case TokenType.STRING:
                        typeIndex = 4; // string
                        break;
                    case TokenType.NUMBER:
                        typeIndex = 5; // identifier
                        break;
                    case TokenType.LBRACE:
                    case TokenType.RBRACE:
                    case TokenType.LBRACKET:
                    case TokenType.RBRACKET:
                    case TokenType.LPAREN:
                    case TokenType.RPAREN:
                    case TokenType.ARROW:
                    case TokenType.COMMA:
                    case TokenType.EQUALS:
                    case TokenType.DOT:
                        typeIndex = 1; // circuitOperator
                        break;
                    case TokenType.COMMENT:
                        typeIndex = 3; // comment
                        break;
                    default:
                        typeIndex = 5; // identifier
                        break;
                }

                // Convert to 0-based
                int line = token.Line - 1;
                int col = token.Column - 1;
                int length = token.Type == TokenType.STRING ? token.Value.Length + 2 : token.Value.Length;
                semanticTokens.Add(new[] { line, col, length, typeIndex, 0 });
            }

            // Output as JSON
            Console.WriteLine(JsonSerializer.Serialize(semanticTokens));
        }

        static void DisplayState(Circuit circuit, int tick, Dictionary<string, string> previousInputBinaries, Dictionary<string, string> previousOutputBinaries)
        {
            Console.WriteLine($"Current State (Tick: {tick}):");
            Console.WriteLine("Inputs:");

            // Group inputs by base name for multi-bit display
            var inputGroups = new Dictionary<string, List<(int index, bool value)>>();
            foreach (var kvp in circuit.ExternalInputs)
            {
                var key = kvp.Key;
                var inputValue = kvp.Value;

                // Check if it's an array input like "a[0]"
                var bracketIndex = key.IndexOf('[');
                if (bracketIndex > 0 && key.EndsWith(']'))
                {
                    var baseName = key.Substring(0, bracketIndex);
                    var indexStr = key.Substring(bracketIndex + 1, key.Length - bracketIndex - 2);
                    if (int.TryParse(indexStr, out var index))
                    {
                        if (!inputGroups.ContainsKey(baseName))
                        {
                            inputGroups[baseName] = new List<(int, bool)>();
                        }
                        inputGroups[baseName].Add((index, inputValue));
                    }
                    else
                    {
                        // Fallback for malformed array names
                        Console.WriteLine($"  {key}: {(inputValue ? 1 : 0)}");
                    }
                }
                else
                {
                    // Single-bit input
                    var valueStr = (inputValue ? "1" : "0");
                    var changed = previousInputBinaries.TryGetValue(key, out var prev) && prev != valueStr;
                    if (changed)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }
                    Console.WriteLine($"  {key}: {valueStr}");
                    if (changed)
                    {
                        Console.ResetColor();
                    }
                    previousInputBinaries[key] = valueStr;
                }
            }

            // Display grouped multi-bit inputs
            foreach (var group in inputGroups)
            {
                var baseName = group.Key;
                var bits = group.Value.OrderBy(x => x.index).Select(x => x.value).ToArray();

                // Convert bits to binary string (MSB first)
                var binaryStr = string.Join("", bits.Reverse().Select(b => b ? "1" : "0"));

                // Check if changed
                var changed = previousInputBinaries.TryGetValue(baseName, out var prev) && prev != binaryStr;
                if (changed)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                Console.WriteLine($"  {baseName}: {binaryStr}");
                if (changed)
                {
                    Console.ResetColor();
                }

                // Update previous
                previousInputBinaries[baseName] = binaryStr;
            }

            Console.WriteLine("Outputs:");

            // Group outputs by base name for multi-bit display
            var outputGroups = new Dictionary<string, List<(int index, bool value)>>();
            foreach (var kvp in circuit.ExternalOutputs)
            {
                var key = kvp.Key;
                var gate = kvp.Value;
                var outputValue = gate?.Output ?? false;

                // Check if it's an array output like "sum[0]"
                var bracketIndex = key.IndexOf('[');
                if (bracketIndex > 0 && key.EndsWith(']'))
                {
                    var baseName = key.Substring(0, bracketIndex);
                    var indexStr = key.Substring(bracketIndex + 1, key.Length - bracketIndex - 2);
                    if (int.TryParse(indexStr, out var index))
                    {
                        if (!outputGroups.ContainsKey(baseName))
                        {
                            outputGroups[baseName] = new List<(int, bool)>();
                        }
                        outputGroups[baseName].Add((index, outputValue));
                    }
                    else
                    {
                        // Fallback for malformed array names
                        Console.WriteLine($"  {key}: {(outputValue ? 1 : 0)}");
                    }
                }
                else
                {
                    // Single-bit output
                    var valueStr = (outputValue ? "1" : "0");
                    var changed = previousOutputBinaries.TryGetValue(key, out var prev) && prev != valueStr;
                    if (changed)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }
                    Console.WriteLine($"  {key}: {valueStr}");
                    if (changed)
                    {
                        Console.ResetColor();
                    }
                    previousOutputBinaries[key] = valueStr;
                }
            }

            // Display grouped multi-bit outputs
            foreach (var group in outputGroups)
            {
                var baseName = group.Key;
                var bits = group.Value.OrderBy(x => x.index).Select(x => x.value).ToArray();

                // Convert bits to binary string (MSB first)
                var binaryStr = string.Join("", bits.Reverse().Select(b => b ? "1" : "0"));

                // Check if changed
                var changed = previousOutputBinaries.TryGetValue(baseName, out var prev) && prev != binaryStr;
                if (changed)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                Console.WriteLine($"  {baseName}: {binaryStr}");
                if (changed)
                {
                    Console.ResetColor();
                }

                // Update previous
                previousOutputBinaries[baseName] = binaryStr;
            }
        }

        static Dictionary<string, string> ParseInteractiveInputs(string inputLine)
        {
            var changes = new Dictionary<string, string>();
            var parts = inputLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kvp = part.Split('=');
                if (kvp.Length == 2)
                {
                    changes[kvp[0]] = kvp[1];
                }
                else
                {
                    changes[kvp[0]] = "";
                }
            }
            return changes;
        }

        static void ApplyInputChanges(Circuit circuit, Dictionary<string, string> changes)
        {
            foreach (var change in changes)
            {
                var inputName = change.Key;
                var valueStr = change.Value;

                if (string.IsNullOrEmpty(valueStr))
                {
                    // toggle the input if no value provided
                    // Check for single-bit input
                    if (circuit.ExternalInputs.ContainsKey(inputName))
                    {
                        circuit.ExternalInputs[inputName] = !circuit.ExternalInputs[inputName];
                    }
                    else
                    {
                        // Check for multi-bit input (e.g., toggle all bits of "a" if "a[0]", "a[1]" exist)
                        var multiBitKeys = circuit.ExternalInputs.Keys.Where(k => k.StartsWith(inputName + "[") && k.EndsWith("]")).ToList();
                        if (multiBitKeys.Count > 0)
                        {
                            foreach (var key in multiBitKeys)
                            {
                                circuit.ExternalInputs[key] = !circuit.ExternalInputs[key];
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Unknown input: {inputName}");
                            Console.ResetColor();
                        }
                    }
                }
                else if (bool.TryParse(valueStr, out var boolValue))
                {
                    // Single boolean input
                    if (circuit.ExternalInputs.ContainsKey(inputName))
                    {
                        circuit.ExternalInputs[inputName] = boolValue;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Unknown input: {inputName}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // Try to parse as multi-bit value
                    try
                    {
                        var bits = ParseMultiBitValue(valueStr);
                        
                        // Check if it's a known input port
                        var portInfo = circuit.InputNames.FirstOrDefault(p => p.Name == inputName);
                        if (portInfo != null)
                        {
                            if (bits.Length != portInfo.BitWidth)
                            {
                                throw new ArgumentException($"Supplied input bitwidth ({bits.Length}) does not match circuit input bitwidth ({portInfo.BitWidth})");
                            }
                            SetMultiBitInput(circuit, inputName, bits);
                        }
                        else if (bits.Length == 1 && circuit.ExternalInputs.ContainsKey(inputName))
                        {
                            // Fallback for single-bit inputs not in InputNames
                            circuit.ExternalInputs[inputName] = bits[0];
                        }
                        else
                        {
                            throw new ArgumentException($"Unknown input: {inputName}");
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Invalid value for {inputName}: {valueStr} ({ex.Message})");
                        Console.ResetColor();
                    }
                }
            }
        }

        static void RunSimulationMode(string dslFile, string basePath, string[] args, bool isInteractive, string? circuitName)
        {
            var dsl = File.ReadAllText(dslFile);

            Circuit circuit;
            try
            {
                var lexer = new Lexer(dsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, basePath, dslFile);
                var circuits = parser.ParseCircuits();
                if (circuitName != null && circuits.ContainsKey(circuitName))
                {
                    circuit = circuits[circuitName];
                }
                else if (circuitName != null)
                {
                    Console.Error.WriteLine($"Circuit '{circuitName}' not found in '{dslFile}'. Available circuits: {string.Join(", ", circuits.Keys)}");
                    return;
                }
                else
                {
                    circuit = circuits.LastOrDefault().Value;
                }
            }
            catch (DSLInvalidSyntaxException ex)
            {
                Console.Error.WriteLine($"Syntax Error in '{dslFile}': {ex.Message}");
                if (ex.Line > 0 && ex.Line <= dsl.Split('\n').Length)
                {
                    var lines = dsl.Split('\n');
                    var errorLine = lines[ex.Line - 1];
                    Console.Error.WriteLine(errorLine);
                    if (ex.Column > 0 && ex.Column <= errorLine.Length + 1)
                    {
                        var caret = new string(' ', ex.Column - 1) + "^";
                        Console.Error.WriteLine(caret);
                    }
                }
                Console.Error.WriteLine("Hint: Check for missing commas, brackets, or incorrect keywords in the circuit definition.");
                return;
            }
            catch (DSLInvalidGateException ex)
            {
                Console.Error.WriteLine($"Gate Error in '{dslFile}': {ex.Message}");
                Console.Error.WriteLine("Hint: Ensure gate types are valid (e.g., AND, OR, Circuit) and parameters are correct.");
                return;
            }
            catch (DSLInvalidConnectionException ex)
            {
                Console.Error.WriteLine($"Connection Error in '{dslFile}': {ex.Message}");
                Console.Error.WriteLine("Hint: Verify connection syntax (e.g., source -> target) and that gates/ports exist.");
                return;
            }
            catch (DSLImportException ex)
            {
                Console.Error.WriteLine($"Import Error in '{dslFile}': {ex.Message}");
                Console.Error.WriteLine("Hint: Check that imported files exist and are valid.");
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected Error in '{dslFile}': {ex.Message}");
                return;
            }

            // Parse input values and ticks
            int ticks = 2; // default for combinational circuits
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--"))
                {
                    // Skip known flags
                    if (arg == "--verify" || arg == "--interactive" || arg.StartsWith("--base-path=") || arg.StartsWith("--circuit=") || arg.StartsWith("--synthesize=") || arg.StartsWith("--out="))
                    {
                        continue;
                    }

                    var parts = arg.Substring(2).Split('=');
                    if (parts.Length == 2)
                    {
                        var paramName = parts[0];
                        var valueStr = parts[1];

                        if (paramName == "ticks")
                        {
                            if (int.TryParse(valueStr, out var ticksValue) && ticksValue > 0)
                            {
                                ticks = ticksValue;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Invalid ticks value: {valueStr}. Must be a positive integer.");
                                Console.ResetColor();
                                return;
                            }
                        }
                        else
                        {
                            // Handle input parameters
                            var inputName = paramName;
                            if (bool.TryParse(valueStr, out var boolValue))
                            {
                                // Single boolean input
                                if (circuit.ExternalInputs.ContainsKey(inputName))
                                {
                                    circuit.ExternalInputs[inputName] = boolValue;
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"Unknown input: {inputName}");
                                    Console.ResetColor();
                                }
                            }
                            else
                            {
                                // Try to parse as multi-bit value
                                try
                                {
                                    var bits = ParseMultiBitValue(valueStr);
                                    
                                    // Check if it's a known input port
                                    var portInfo = circuit.InputNames.FirstOrDefault(p => p.Name == inputName);
                                    if (portInfo != null)
                                    {
                                        if (bits.Length > portInfo.BitWidth)
                                        {
                                            throw new ArgumentException($"Supplied input bitwidth ({bits.Length}) does not match circuit input bitwidth ({portInfo.BitWidth})");
                                        }
                                        if (bits.Length < portInfo.BitWidth)
                                        {
                                            var padded = new bool[portInfo.BitWidth];
                                            bits.CopyTo(padded, portInfo.BitWidth - bits.Length);
                                            bits = padded;
                                        }
                                        SetMultiBitInput(circuit, inputName, bits);
                                    }
                                    else if (bits.Length == 1 && circuit.ExternalInputs.ContainsKey(inputName))
                                    {
                                        // Fallback for single-bit inputs not in InputNames
                                        circuit.ExternalInputs[inputName] = bits[0];
                                    }
                                    else
                                    {
                                        throw new ArgumentException($"Unknown input: {inputName}");
                                    }
                                }
                                catch (ArgumentException ex)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"Invalid value for {inputName}: {valueStr} ({ex.Message})");
                                    Console.ResetColor();
                                }
                            }
                        }
                    }
                }
            }

            var previousInputBinaries = new Dictionary<string, string>();
            var previousOutputBinaries = new Dictionary<string, string>();

            if (isInteractive)
            {
                Console.WriteLine("'a=true b=false' to set inputs, 'a' 'b' to toggle inputs, 'tick' or '.' to simulate, 'exit' to exit): ");
                // Interactive mode
                int currentTick = 0;
                DisplayState(circuit, currentTick, previousInputBinaries, previousOutputBinaries);
                while (true)
                {
                    Console.Write("> ");
                    var command = Console.ReadLine()?.Trim();
                    if (string.IsNullOrEmpty(command))
                    {
                        continue;
                    }
                    if (command.StartsWith("."))
                    {
                        command = command.TrimStart('.');
                        switch (command)
                        {
                            case "exit":
                                return;
                            case "tick":
                            case "":
                                currentTick++;
                                circuit.Tick();
                                DisplayState(circuit, currentTick, previousInputBinaries, previousOutputBinaries);
                                break;
                            default:
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Invalid command. Use 'input=value' to set inputs, 'input' to toggle inputs, 'tick' or '.' to simulate, or 'exit' to exit.");
                                Console.ResetColor();
                                break;
                        }
                    }
                    else
                    {
                        var changes = ParseInteractiveInputs(command);
                        if (changes.Count > 0)
                        {
                            ApplyInputChanges(circuit, changes);
                            DisplayState(circuit, currentTick, previousInputBinaries, previousOutputBinaries);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Unable to parse inputs.");
                            Console.ResetColor();
                        }
                    }
                }
            }
            else
            {
                // Batch mode
                // Simulate for specified number of ticks and collect state history
                var stateHistory = new List<(int Tick, Dictionary<string, bool> Inputs, Dictionary<string, bool> Outputs)>();

                // Record initial state (before any ticks)
                var initialOutputs = new Dictionary<string, bool>();
                foreach (var kvp in circuit.ExternalOutputs)
                {
                    var gate = kvp.Value;
                    initialOutputs[kvp.Key] = gate?.Output ?? false;
                }
                stateHistory.Add((0, new Dictionary<string, bool>(circuit.ExternalInputs), initialOutputs));

                // Simulate and record state after each tick
                for (int j = 0; j < ticks; j++)
                {
                    circuit.Tick();
                }

                // Output results
                Console.WriteLine($"Simulation Results (after {ticks} ticks):");
                DisplayState(circuit, ticks, new Dictionary<string, string>(), new Dictionary<string, string>());
            }
        }
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CircuitSimulator <dsl-file> [--circuit=<name>] [--<input>=<value>]... [--ticks=N] [--interactive]");
                Console.WriteLine("       CircuitSimulator --synthesize=\"expression\" [--out=<file>]");
                Console.WriteLine("       CircuitSimulator --tokens [<dsl-file>]  # Read from stdin if no file");
                Console.WriteLine("Examples:");
                Console.WriteLine("  CircuitSimulator circuit.circuit --a=true --ticks=10");
                Console.WriteLine("  CircuitSimulator circuit.circuit --circuit=MyCircuit --a=true --b=false --ticks=5");
                Console.WriteLine("  CircuitSimulator circuit.circuit --interactive  # Interactive mode for changing inputs");
                Console.WriteLine("  CircuitSimulator circuit.circuit --verify  # For LSP validation");
                Console.WriteLine("  CircuitSimulator circuit.circuit --info    # For LSP hover info");
                Console.WriteLine("  CircuitSimulator circuit.circuit --tokens  # Output semantic tokens for file");
                Console.WriteLine("  echo \"circuit Test {}\" | CircuitSimulator --tokens  # Output tokens from stdin");
                Console.WriteLine("  CircuitSimulator --synthesize=\"xor(and(a,b),or(a,c))\"  # Synthesize circuit from expression");
                Console.WriteLine("  CircuitSimulator --synthesize=\"xor(and(a,b),or(a,c))\" --out=synth.circuit  # Save to file");
                return;
            }

            var isVerifyMode = args.Contains("--verify");
            var isInfoMode = args.Contains("--info");
            var isSynthesizeMode = false;
            var isTokensMode = args.Contains("--tokens");
            var isInteractiveMode = args.Contains("--interactive");
            string? synthesizeExpression = null;
            string? outFile = null;
            string? circuitName = null;

            var dslFile = args.Length > 0 && !args[0].StartsWith("--") ? args[0] :
                          (isTokensMode && args.Length > 1 ? args[1] : null);

            // Parse arguments
            string? customBasePath = null;
            string? originalFilePath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--base-path="))
                {
                    customBasePath = args[i].Substring("--base-path=".Length);
                }
                else if (args[i].StartsWith("--original-file-path="))
                {
                    originalFilePath = args[i].Substring("--original-file-path=".Length);
                }
                else if (args[i].StartsWith("--circuit="))
                {
                    circuitName = args[i].Substring("--circuit=".Length);
                }
                else if (args[i].StartsWith("--synthesize="))
                {
                    isSynthesizeMode = true;
                    synthesizeExpression = args[i].Substring("--synthesize=".Length);
                }
                else if (args[i].StartsWith("--out="))
                {
                    outFile = args[i].Substring("--out=".Length);
                }
            }

            if (isSynthesizeMode)
            {
                if (synthesizeExpression == null)
                {
                    Console.WriteLine("Synthesis expression is required.");
                    return;
                }
                RunSynthesizeMode(synthesizeExpression, outFile);
                return;
            }

            if (isTokensMode)
            {
                RunTokensMode(dslFile);
                return;
            }

            if (dslFile == null)
            {
                Console.WriteLine("DSL file is required for this mode.");
                return;
            }

            var basePath = customBasePath ?? Path.GetDirectoryName(Path.GetFullPath(dslFile)) ?? ".";

            if (isVerifyMode)
            {
                RunVerifyMode(dslFile, basePath);
                return;
            }

            if (isInfoMode)
            {
                RunInfoMode(dslFile, basePath, originalFilePath);
                return;
            }

            // Normal simulation mode
            RunSimulationMode(dslFile, basePath, args, isInteractiveMode, circuitName);
        }
    }
}
