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

            // Simulate for specified number of ticks
            for (int i = 0; i < ticks; i++)
            {
                circuit.Tick();
            }

            // Output results
            Console.WriteLine($"Simulation Results (after {ticks} ticks):");
            Console.WriteLine("Inputs:");
            foreach (var kvp in circuit.ExternalInputs)
            {
                Console.WriteLine($"  {kvp.Key}: {(kvp.Value ? 1 : 0)}");
            }
            Console.WriteLine("Outputs:");
            foreach (var kvp in circuit.ExternalOutputs)
            {
                var gate = kvp.Value;
                var outputValue = gate?.Output ?? false;
                Console.WriteLine($"  {kvp.Key}: {(outputValue ? 1 : 0)}");
            }
        }
    }
}
