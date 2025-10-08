namespace CircuitSimulator.Core
{
    public abstract class Gate
    {
        public List<bool> Inputs { get; set; } = [];
        public List<bool> Outputs { get; protected set; } = [];
        public int DefinitionLine { get; set; } = 0;
        public int DefinitionColumn { get; set; } = 0;
        public string Type { get; set; } = "";

        public bool Output => Outputs.Count > 0 && Outputs[0];

        public abstract void Compute();
    }

    public class AndGate : Gate
    {
        public AndGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "AND";
        }

        public override void Compute()
        {
            Outputs = [Inputs.All(i => i)];
        }
    }

    public class OrGate : Gate
    {
        public OrGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "OR";
        }

        public override void Compute()
        {
            Outputs = [Inputs.Any(i => i)];
        }
    }

    public class NotGate : Gate
    {
        public NotGate()
        {
            Inputs = [false];
            Outputs = [false];
            Type = "NOT";
        }

        public override void Compute()
        {
            if (Inputs.Count != 1) throw new InvalidOperationException("NOT gate must have exactly one input.");
            Outputs = [!Inputs[0]];
        }
    }

    public class NandGate : Gate
    {
        public NandGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "NAND";
        }

        public override void Compute()
        {
            Outputs = [!Inputs.All(i => i)];
        }
    }

    public class NorGate : Gate
    {
        public NorGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "NOR";
        }

        public override void Compute()
        {
            Outputs = [!Inputs.Any(i => i)];
        }
    }

    public class XorGate : Gate
    {
        public XorGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "XOR";
        }

        public override void Compute()
        {
            Outputs = [Inputs.Count(i => i) % 2 == 1];
        }
    }

    public class XnorGate : Gate
    {
        public XnorGate()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "XNOR";
        }

        public override void Compute()
        {
            Outputs = [Inputs.Count(i => i) % 2 == 0];
        }
    }

    public class DFlipFlop : Gate
    {
        private bool _q = false;

        public DFlipFlop()
        {
            Inputs = [false, false];
            Outputs = [false];
            Type = "DFF";
        }

        public override void Compute()
        {
            if (Inputs.Count >= 2 && Inputs[1]) // CLK
            {
                _q = Inputs[0]; // D
            }
            Outputs = [_q];
        }
    }

    public class CircuitGate : Gate
    {
        private Circuit SubCircuit { get; }

        public List<string> InputNames => SubCircuit.InputNames.Select(p => p.Name).ToList();
        public List<string> OutputNames => SubCircuit.OutputNames.Select(p => p.Name).ToList();
        public List<int> OutputBitWidths => SubCircuit.OutputNames.Select(p => p.BitWidth).ToList();

        public CircuitGate(Circuit subCircuit)
        {
            SubCircuit = subCircuit;
            // Initialize inputs and outputs
            Inputs = new List<bool>(new bool[subCircuit.ExternalInputs.Count]);
            Outputs = [.. new bool[subCircuit.ExternalOutputs.Count]];
            Type = "Circuit:" + subCircuit.Name;
        }

        public override void Compute()
        {
            // Map this gate's inputs to subcircuit's external inputs
            var inputNames = SubCircuit.ExternalInputs.Keys.ToList();
            for (int i = 0; i < Inputs.Count && i < inputNames.Count; i++)
            {
                SubCircuit.ExternalInputs[inputNames[i]] = Inputs[i];
            }

            // Simulate the subcircuit
            SubCircuit.Tick();

            // Map subcircuit's external outputs to this gate's outputs
            var outputNames = SubCircuit.ExternalOutputs.Keys.ToList();
            Outputs.Clear();
            foreach (var name in outputNames)
            {
                var gate = SubCircuit.ExternalOutputs[name];
                Outputs.Add(gate?.Output ?? false);
            }
        }
    }

    public class SubcircuitOutputGate : Gate
    {
        private CircuitGate ParentCircuitGate { get; }
        private int OutputIndex { get; }
        private int BitWidth { get; }

        public SubcircuitOutputGate(CircuitGate parentCircuitGate, int outputIndex, int bitWidth = 1)
        {
            ParentCircuitGate = parentCircuitGate;
            OutputIndex = outputIndex;
            BitWidth = bitWidth;
            Outputs = new List<bool>(new bool[bitWidth]); // Initialize with the correct number of outputs
        }

        public override void Compute()
        {
            // This gate's output is the specific output from the parent subcircuit
            if (BitWidth == 1)
            {
                Outputs = [(OutputIndex < ParentCircuitGate.Outputs.Count && ParentCircuitGate.Outputs[OutputIndex])];
            }
            else
            {
                // For multi-bit outputs, copy the range starting from OutputIndex
                Outputs = new List<bool>(new bool[BitWidth]);
                for (int i = 0; i < BitWidth && OutputIndex + i < ParentCircuitGate.Outputs.Count; i++)
                {
                    Outputs[i] = ParentCircuitGate.Outputs[OutputIndex + i];
                }
            }
        }
    }

    public class LookupTableGate : Gate
    {
        private Dictionary<string, bool[]> LookupTable { get; }

        public LookupTableGate(Dictionary<string, bool[]> lookupTable, string tableName)
        {
            LookupTable = lookupTable;
            Type = "LookupTable:" + tableName;
            // Initialize inputs and outputs based on the table
            if (lookupTable.Count > 0)
            {
                var firstKey = lookupTable.Keys.First();
                var firstOutput = lookupTable[firstKey];
                Inputs = new List<bool>(new bool[firstKey.Length]);
                Outputs = [.. firstOutput];
            }
            else
            {
                Inputs = [];
                Outputs = [false];
            }
        }

        public override void Compute()
        {
            // Convert inputs to binary string key (MSB first)
            string key = string.Join("", Inputs.AsEnumerable().Reverse().Select(i => i ? "1" : "0"));

            // Look up the output values
            if (LookupTable.TryGetValue(key, out bool[]? output))
            {
                Outputs = [.. output];
            }
            else
            {
                // Default to all false if key not found
                Outputs = [.. new bool[Outputs.Count]];
            }
        }
    }

    public class LookupTableOutputGate : Gate
    {
        private LookupTableGate ParentLookupTableGate { get; }
        private int OutputIndex { get; }

        public LookupTableOutputGate(LookupTableGate parentLookupTableGate, int outputIndex)
        {
            ParentLookupTableGate = parentLookupTableGate;
            OutputIndex = outputIndex;
            Outputs = [false]; // Initialize with one output
        }

        public override void Compute()
        {
            // This gate's output is the specific output from the parent lookup table gate
            Outputs = [OutputIndex < ParentLookupTableGate.Outputs.Count && ParentLookupTableGate.Outputs[OutputIndex]];
        }
    }
}