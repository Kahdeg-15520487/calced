using System;
using System.Collections.Generic;
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

        public static Circuit Parse(string dslText)
        {
            var circuit = new Circuit();
            var lines = dslText.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#")).ToArray();

            int i = 0;
            while (i < lines.Length)
            {
                if (lines[i].StartsWith("circuit "))
                {
                    // Parse circuit name
                    var circuitName = Regex.Match(lines[i], @"circuit (\w+)").Groups[1].Value;
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
                            i = ParseGates(circuit, lines, i + 1);
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
                }
                i++;
            }

            return circuit;
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

        private static int ParseGates(Circuit circuit, string[] lines, int start)
        {
            int i = start;
            while (i < lines.Length && lines[i] != "}")
            {
                var match = Regex.Match(lines[i], @"(\w+)\s*=\s*(\w+)\(\)");
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var type = match.Groups[2].Value;
                    if (GateFactory.TryGetValue(type, out var factory))
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
                    if (source.EndsWith(".out"))
                    {
                        source = source.Substring(0, source.Length - 4);
                    }
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
                        if (source.EndsWith(".out"))
                        {
                            source = source.Substring(0, source.Length - 4);
                        }
                        var target = outputMatch.Groups[2].Value.Trim();

                        if (circuit.NamedGates.TryGetValue(source, out var sourceGate) && circuit.ExternalOutputs.ContainsKey(target))
                        {
                            circuit.ExternalOutputs[target] = sourceGate;
                        }
                    }
                }
                i++;
            }
            return i + 1;
        }
    }
}