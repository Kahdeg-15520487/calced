using System;
using System.IO;
using System.Linq;

namespace CircuitSimulator
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CircuitSimulator <dsl-file> [--<input>=<value>]...");
                Console.WriteLine("Example: CircuitSimulator circuit.dsl --a=true --b=false");
                return;
            }

            var dslFile = args[0];
            if (!File.Exists(dslFile))
            {
                Console.WriteLine($"File not found: {dslFile}");
                return;
            }

            var dsl = File.ReadAllText(dslFile);
            var circuit = DSLParser.Parse(dsl);

            // Parse input values
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--"))
                {
                    var parts = arg.Substring(2).Split('=');
                    if (parts.Length == 2)
                    {
                        var inputName = parts[0];
                        var valueStr = parts[1];
                        if (bool.TryParse(valueStr, out var value))
                        {
                            if (circuit.ExternalInputs.ContainsKey(inputName))
                            {
                                circuit.ExternalInputs[inputName] = value;
                            }
                            else
                            {
                                Console.WriteLine($"Unknown input: {inputName}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Invalid value for {inputName}: {valueStr}");
                        }
                    }
                }
            }

            // Simulate
            circuit.Tick();

            // Output results
            Console.WriteLine("Simulation Results:");
            Console.WriteLine("Inputs:");
            foreach (var kvp in circuit.ExternalInputs)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
            Console.WriteLine("Outputs:");
            foreach (var kvp in circuit.ExternalOutputs)
            {
                var gate = kvp.Value;
                var outputValue = gate?.Output ?? false;
                Console.WriteLine($"  {kvp.Key}: {outputValue}");
            }
        }
    }
}
