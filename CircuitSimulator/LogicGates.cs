namespace CircuitSimulator
{
    public abstract class Gate
    {
        public List<bool> Inputs { get; set; } = [];
        public List<bool> Outputs { get; protected set; } = [];

        public bool Output => Outputs.Count > 0 && Outputs[0];

        public abstract void Compute();
    }

    public class AndGate : Gate
    {
        public override void Compute()
        {
            Outputs = [Inputs.All(i => i)];
        }
    }

    public class OrGate : Gate
    {
        public override void Compute()
        {
            Outputs = [Inputs.Any(i => i)];
        }
    }

    public class NotGate : Gate
    {
        public override void Compute()
        {
            if (Inputs.Count != 1) throw new InvalidOperationException("NOT gate must have exactly one input.");
            Outputs = [!Inputs[0]];
        }
    }

    public class NandGate : Gate
    {
        public override void Compute()
        {
            Outputs = [!Inputs.All(i => i)];
        }
    }

    public class NorGate : Gate
    {
        public override void Compute()
        {
            Outputs = [!Inputs.Any(i => i)];
        }
    }

    public class XorGate : Gate
    {
        public override void Compute()
        {
            Outputs = [Inputs.Count(i => i) % 2 == 1];
        }
    }

    public class XnorGate : Gate
    {
        public override void Compute()
        {
            Outputs = [Inputs.Count(i => i) % 2 == 0];
        }
    }

    public class DFlipFlop : Gate
    {
        private bool _q = false;

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

        public CircuitGate(Circuit subCircuit)
        {
            SubCircuit = subCircuit;
            // Initialize outputs list with the same size as subcircuit outputs
            Outputs = [.. new bool[subCircuit.ExternalOutputs.Count]];
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

        public SubcircuitOutputGate(CircuitGate parentCircuitGate, int outputIndex)
        {
            ParentCircuitGate = parentCircuitGate;
            OutputIndex = outputIndex;
            Outputs = [false]; // Initialize with one output
        }

        public override void Compute()
        {
            // This gate's output is the specific output from the parent subcircuit
            Outputs = [(OutputIndex < ParentCircuitGate.Outputs.Count && ParentCircuitGate.Outputs[OutputIndex])];
        }
    }

    public class LookupTableGate : Gate
    {
        private Dictionary<string, bool[]> LookupTable { get; }

        public LookupTableGate(Dictionary<string, bool[]> lookupTable)
        {
            LookupTable = lookupTable;
            // Initialize outputs based on the first table entry
            if (lookupTable.Count > 0)
            {
                var firstOutput = lookupTable.Values.First();
                Outputs = [.. firstOutput];
            }
            else
            {
                Outputs = [false];
            }
        }

        public override void Compute()
        {
            // Convert inputs to binary string key
            string key = string.Join("", Inputs.Select(i => i ? "1" : "0"));
            
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