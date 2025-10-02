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

            Circuit circuit;
            try
            {
                circuit = RegexParser.Parse(dsl, basePath, useNewParser: true);
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
    }
}
