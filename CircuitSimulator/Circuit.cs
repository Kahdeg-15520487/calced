using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CircuitSimulator
{
    public class Circuit
    {
        public string Name { get; set; } = "";
        public List<Gate> Gates { get; } = new List<Gate>();
        // Connections: key is the gate, value is list of sources (Gate or string for external input)
        public Dictionary<Gate, List<object?>> Connections { get; } = new Dictionary<Gate, List<object?>>();
        // Named lookup for gates
        public Dictionary<string, Gate> NamedGates { get; } = new Dictionary<string, Gate>();
        // Reverse lookup
        private Dictionary<Gate, string> GateToName { get; } = new Dictionary<Gate, string>();
        // External inputs and outputs
        public Dictionary<string, bool> ExternalInputs { get; } = new Dictionary<string, bool>();
        public List<string> InputNames { get; } = new List<string>();
        public Dictionary<string, Gate?> ExternalOutputs { get; } = new Dictionary<string, Gate?>();
        public List<string> OutputNames { get; } = new List<string>();

        public void AddGate(string name, Gate gate)
        {
            Gates.Add(gate);
            NamedGates[name] = gate;
            GateToName[gate] = name;
            Connections[gate] = new List<object?>();
        }

        // Connect source (Gate or external input name) to targetGate's input at index
        public void Connect(object source, Gate targetGate, int inputIndex)
        {
            if (!Connections.ContainsKey(targetGate))
            {
                Connections[targetGate] = new List<object?>();
            }
            while (Connections[targetGate].Count <= inputIndex)
            {
                Connections[targetGate].Add(null);
            }
            Connections[targetGate][inputIndex] = source;
        }

        // Simulate one tick: propagate signals
        public void Tick()
        {
            // For combinational circuits, do multiple passes to propagate signals
            for (int pass = 0; pass < 3; pass++)
            {
                // First, set inputs based on connections
                foreach (var gate in Gates)
                {
                    if (Connections.ContainsKey(gate))
                    {
                        var sources = Connections[gate];
                        gate.Inputs.Clear();
                        foreach (var source in sources)
                        {
                            if (source is Gate gateSource)
                            {
                                gate.Inputs.Add(gateSource.Outputs[0]);
                            }
                            else if (source is string inputName)
                            {
                                gate.Inputs.Add(ExternalInputs[inputName]);
                            }
                            else
                            {
                                // For unconnected inputs, assume false
                                gate.Inputs.Add(false);
                            }
                        }
                    }
                }

                // Compute CircuitGates first
                foreach (var gate in Gates)
                {
                    if (gate is CircuitGate)
                    {
                        gate.Compute();
                    }
                }

                // Then compute LookupTableGates
                foreach (var gate in Gates)
                {
                    if (gate is LookupTableGate)
                    {
                        gate.Compute();
                    }
                }

                // Then compute other gates
                foreach (var gate in Gates)
                {
                    if (!(gate is CircuitGate) && !(gate is LookupTableGate) && !(gate is SubcircuitOutputGate) && !(gate is LookupTableOutputGate))
                    {
                        gate.Compute();
                    }
                }

                // Then compute SubcircuitOutputGates and LookupTableOutputGates
                foreach (var gate in Gates)
                {
                    if (gate is SubcircuitOutputGate || gate is LookupTableOutputGate)
                    {
                        gate.Compute();
                    }
                }
            }
        }

        // Text-based visualization
        public string Visualize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Circuit Visualization:");
            sb.AppendLine("Gates:");
            foreach (var kvp in NamedGates)
            {
                var typeName = Parser.TypeToName.TryGetValue(kvp.Value.GetType(), out var name) ? name : kvp.Value.GetType().Name;
                sb.AppendLine($"  {kvp.Key}: {typeName}");
            }
            sb.AppendLine("Connections:");
            foreach (var kvp in Connections)
            {
                var gate = kvp.Key;
                var sources = kvp.Value;
                var gateName = GateToName.TryGetValue(gate, out var gn) ? gn : "unknown";
                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    string sourceName;
                    if (source is Gate g && GateToName.TryGetValue(g, out var sn))
                    {
                        sourceName = sn;
                    }
                    else if (source is string s)
                    {
                        sourceName = s;
                    }
                    else
                    {
                        continue;
                    }
                    sb.AppendLine($"  {sourceName} -> {gateName}[{i}]");
                }
            }
            sb.AppendLine("External Inputs:");
            foreach (var kvp in ExternalInputs)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine("External Outputs:");
            foreach (var kvp in ExternalOutputs)
            {
                var gate = kvp.Value;
                var gateName = gate != null && GateToName.TryGetValue(gate, out var name) ? name : "null";
                sb.AppendLine($"  {kvp.Key}: {gateName}");
            }
            return sb.ToString();
        }

        // For sequential, might need more, but start here
    }
}