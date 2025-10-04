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
            for (int i = 0; i < bits.Length; i++)
            {
                var bitInputName = $"{inputName}[{i}]";
                if (circuit.ExternalInputs.ContainsKey(bitInputName))
                {
                    circuit.ExternalInputs[bitInputName] = bits[i];
                }
                else
                {
                    throw new ArgumentException($"Input {bitInputName} not found in circuit");
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
                        Gates = circuitEntry.Value.NamedGates.Where(g => !string.IsNullOrEmpty(g.Value.Type)).ToDictionary(g => g.Key, g => new GateInfo { Type = g.Value.Type, DefinitionLine = g.Value.DefinitionLine, DefinitionColumn = g.Value.DefinitionColumn }),
                        LookupTables = circuitEntry.Value.LookupTables.ToDictionary(lut => lut.Key, lut => {
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

        static void RunSimulationMode(string dslFile, string basePath, string[] args)
        {
            var dsl = File.ReadAllText(dslFile);

            Circuit circuit;
            try
            {
                var lexer = new Lexer(dsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, basePath, dslFile);
                var circuits = parser.ParseCircuits();
                circuit = circuits.LastOrDefault().Value;
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
                    if (arg == "--verify" || arg.StartsWith("--base-path=") || arg.StartsWith("--synthesize=") || arg.StartsWith("--out="))
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
                                Console.WriteLine($"Invalid ticks value: {valueStr}. Must be a positive integer.");
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
                                    Console.WriteLine($"Unknown input: {inputName}");
                                }
                            }
                            else
                            {
                                // Try to parse as multi-bit value
                                try
                                {
                                    var bits = ParseMultiBitValue(valueStr);
                                    if (bits.Length == 1 && circuit.ExternalInputs.ContainsKey(inputName))
                                    {
                                        circuit.ExternalInputs[inputName] = bits[0];
                                    }
                                    else
                                    {
                                        SetMultiBitInput(circuit, inputName, bits);
                                    }
                                }
                                catch (ArgumentException ex)
                                {
                                    Console.WriteLine($"Invalid value for {inputName}: {valueStr} ({ex.Message})");
                                }
                            }
                        }
                    }
                }
            }

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
            for (int i = 0; i < ticks; i++)
            {
                circuit.Tick();
            }

            // Output results
            Console.WriteLine($"Simulation Results (after {ticks} ticks):");
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
                    Console.WriteLine($"  {key}: {(inputValue ? 1 : 0)}");
                }
            }

            // Display grouped multi-bit inputs
            foreach (var group in inputGroups)
            {
                var baseName = group.Key;
                var bits = group.Value.OrderBy(x => x.index).Select(x => x.value).ToArray();

                // Convert bits to binary string (MSB first)
                var binaryStr = string.Join("", bits.Reverse().Select(b => b ? "1" : "0"));

                Console.WriteLine($"  {baseName}: {binaryStr}");
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
                    Console.WriteLine($"  {key}: {(outputValue ? 1 : 0)}");
                }
            }

            // Display grouped multi-bit outputs
            foreach (var group in outputGroups)
            {
                var baseName = group.Key;
                var bits = group.Value.OrderBy(x => x.index).Select(x => x.value).ToArray();

                // Convert bits to binary string (MSB first)
                var binaryStr = string.Join("", bits.Reverse().Select(b => b ? "1" : "0"));

                // Convert to decimal for display
                var decimalValue = 0;
                for (int i = 0; i < bits.Length; i++)
                {
                    if (bits[i])
                    {
                        decimalValue |= (1 << i);
                    }
                }

                Console.WriteLine($"  {baseName}: {binaryStr}");
            }
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CircuitSimulator <dsl-file> [--<input>=<value>]... [--ticks=N] [--verify] [--info]");
                Console.WriteLine("       CircuitSimulator --synthesize=\"expression\" [--out=<file>]");
                Console.WriteLine("Examples:");
                Console.WriteLine("  CircuitSimulator circuit.circuit --a=true --ticks=10");
                Console.WriteLine("  CircuitSimulator circuit.circuit --a=true --b=false --ticks=5");
                Console.WriteLine("  CircuitSimulator circuit.circuit --verify  # For LSP validation");
                Console.WriteLine("  CircuitSimulator circuit.circuit --info    # For LSP hover info");
                Console.WriteLine("  CircuitSimulator --synthesize=\"xor(and(a,b),or(a,c))\"  # Synthesize circuit from expression");
                Console.WriteLine("  CircuitSimulator --synthesize=\"xor(and(a,b),or(a,c))\" --out=synth.circuit  # Save to file");
                return;
            }

            var dslFile = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
            var isVerifyMode = args.Contains("--verify");
            var isInfoMode = args.Contains("--info");
            var isSynthesizeMode = false;
            string? synthesizeExpression = null;
            string? outFile = null;

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
            RunSimulationMode(dslFile, basePath, args);
        }
    }
}
