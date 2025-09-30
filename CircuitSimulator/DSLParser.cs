using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CircuitSimulator
{
    public class DSLParser
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

        public static Circuit Parse(string dslText, string? circuitName = null)
        {
            var circuits = ParseCircuitsFromText(dslText);
            
            // Return specific circuit if name provided, otherwise return the last one parsed
            if (circuitName != null && circuits.ContainsKey(circuitName))
            {
                return circuits[circuitName];
            }
            return circuits.Values.LastOrDefault() ?? new Circuit();
        }

        private static Dictionary<string, Circuit> ParseCircuitsFromText(string dslText)
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
                    if (match.Success)
                    {
                        var importFile = match.Groups[1].Value;
                        try
                        {
                            var importedDsl = File.ReadAllText(importFile);
                            var importedCircuits = ParseCircuitsFromText(importedDsl);
                            foreach (var kvp in importedCircuits)
                            {
                                circuits[kvp.Key] = kvp.Value;
                                // Don't add imported circuits to parse order
                            }
                        }
                        catch (Exception ex)
                        {
                            // For now, just continue if import fails
                            Console.WriteLine($"Warning: Failed to import {importFile}: {ex.Message}");
                        }
                    }
                    i++;
                }
                else if (lines[i].StartsWith("circuit "))
                {
                    // Parse circuit name
                    var currentCircuitName = Regex.Match(lines[i], @"circuit (\w+)").Groups[1].Value;
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
                            i = ParseGates(circuit, lines, i + 1, circuits);
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

        private static int ParseGates(Circuit circuit, string[] lines, int start, Dictionary<string, Circuit> circuits)
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
                        
                        // First try to find circuit in the same file
                        if (circuits.ContainsKey(circuitRef))
                        {
                            var subCircuit = circuits[circuitRef];
                            var gate = new CircuitGate(subCircuit);
                            circuit.AddGate(name, gate);
                        }
                        else
                        {
                            // Fall back to loading from file
                            var subCircuit = DSLParser.Parse(File.ReadAllText(circuitRef));
                            var gate = new CircuitGate(subCircuit);
                            circuit.AddGate(name, gate);
                        }
                    }
                    else if (GateFactory.TryGetValue(type, out var factory))
                    {
                        var gate = factory();
                        circuit.AddGate(name, gate);
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
                    var index = int.Parse(inputMatch.Groups[3].Value);

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
                            var subcircuitName = parts[0];
                            var outputIndex = int.Parse(parts[1].TrimEnd(']'));
                            if (circuit.NamedGates.TryGetValue(subcircuitName, out var subcircuitGate) && subcircuitGate is CircuitGate circuitGate)
                            {
                                // Create a SubcircuitOutputGate for this specific output
                                var outputGate = new SubcircuitOutputGate(circuitGate, outputIndex);
                                var outputGateName = $"{subcircuitName}_out_{outputIndex}";
                                circuit.AddGate(outputGateName, outputGate);
                                sourceObj = outputGate;
                            }
                        }
                    }

                    if (circuit.NamedGates.TryGetValue(target, out var targetGate) && sourceObj != null)
                    {
                        circuit.Connect(sourceObj, targetGate, index);
                    }
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
                                outputIndex = int.Parse(parts[1].TrimEnd(']'));
                            }
                        }

                        if (circuit.NamedGates.TryGetValue(source, out var sourceGate) && circuit.ExternalOutputs.ContainsKey(target))
                        {
                            // For subcircuits, we need to create a wrapper that extracts the specific output
                            if (sourceGate is CircuitGate circuitGate && outputIndex < circuitGate.Outputs.Count)
                            {
                                // Create a simple gate that outputs the specific index from the subcircuit
                                var outputGate = new SubcircuitOutputGate(circuitGate, outputIndex);
                                circuit.AddGate($"{source}_out_{outputIndex}", outputGate);
                                circuit.ExternalOutputs[target] = outputGate;
                            }
                            else
                            {
                                circuit.ExternalOutputs[target] = sourceGate;
                            }
                        }
                    }
                }
                i++;
            }
            return i + 1;
        }
    }
}