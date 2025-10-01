using System;
using System.IO;
using System.Linq;

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
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CircuitSimulator <dsl-file> [--<input>=<value>]... [--ticks=N]");
                Console.WriteLine("Examples:");
                Console.WriteLine("  CircuitSimulator circuit.circuit --a=true --ticks=10");
                Console.WriteLine("  CircuitSimulator circuit.circuit --a=true --b=false --ticks=5");
                return;
            }

            var dslFile = args[0];
            if (!File.Exists(dslFile))
            {
                Console.WriteLine($"File not found: {dslFile}");
                return;
            }

            var dsl = File.ReadAllText(dslFile);
            var basePath = Path.GetDirectoryName(Path.GetFullPath(dslFile)) ?? ".";
            var circuit = RegexParser.Parse(dsl, basePath, useNewParser: true);

            // Parse input values and ticks
            int ticks = 3; // default
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--"))
                {
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
                
                var tickOutputs = new Dictionary<string, bool>();
                foreach (var kvp in circuit.ExternalOutputs)
                {
                    var gate = kvp.Value;
                    tickOutputs[kvp.Key] = gate?.Output ?? false;
                }
                stateHistory.Add((i + 1, new Dictionary<string, bool>(circuit.ExternalInputs), tickOutputs));
            }

            // Generate markdown table report
            GenerateMarkdownReport(stateHistory);
        }

        static void GenerateMarkdownReport(List<(int Tick, Dictionary<string, bool> Inputs, Dictionary<string, bool> Outputs)> stateHistory)
        {
            if (stateHistory.Count == 0) return;

            var firstState = stateHistory[0];
            var inputNames = firstState.Inputs.Keys.OrderBy(k => k).ToList();
            var outputNames = firstState.Outputs.Keys.OrderBy(k => k).ToList();

            // Header
            Console.WriteLine("\n## Simulation Report");
            Console.WriteLine();
            
            // Table header
            var header = "| Tick | " + string.Join(" | ", inputNames.Select(n => $"**{n}**")) + 
                        " | " + string.Join(" | ", outputNames.Select(n => $"**{n}**")) + " |";
            var separator = "|------|" + string.Join("", inputNames.Select(_ => "------|")) + 
                           string.Join("", outputNames.Select(_ => "------|"));

            Console.WriteLine(header);
            Console.WriteLine(separator);

            // Table rows
            foreach (var (tick, inputs, outputs) in stateHistory)
            {
                var inputValues = string.Join(" | ", inputNames.Select(name => inputs.TryGetValue(name, out var value) ? (value ? "1" : "0") : "X"));
                var outputValues = string.Join(" | ", outputNames.Select(name => outputs.TryGetValue(name, out var value) ? (value ? "1" : "0") : "X"));
                Console.WriteLine($"| {tick} | {inputValues} | {outputValues} |");
            }

            Console.WriteLine();
            Console.WriteLine($"**Total ticks simulated:** {stateHistory.Count - 1}");
            Console.WriteLine($"**Final state after {stateHistory.Count - 1} ticks:**");
            var finalState = stateHistory.Last();
            Console.WriteLine("**Inputs:** " + string.Join(", ", inputNames.Select(name => $"{name}={(finalState.Inputs[name] ? 1 : 0)}")));
            Console.WriteLine("**Outputs:** " + string.Join(", ", outputNames.Select(name => $"{name}={(finalState.Outputs[name] ? 1 : 0)}")));
        }
    }
}
