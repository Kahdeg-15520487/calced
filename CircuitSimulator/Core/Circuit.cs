using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CircuitSimulator.LSP;

namespace CircuitSimulator.Core
{
    public class Circuit
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public int DefinitionLine { get; set; } = 0;
        public int Level { get; set; } = 0;
        public List<Gate> Gates { get; } = new List<Gate>();
        // Connections: key is the gate, value is list of sources (Gate or string for external input)
        public Dictionary<Gate, List<object?>> Connections { get; } = new Dictionary<Gate, List<object?>>();
        // Named lookup for gates
        public Dictionary<string, Gate> NamedGates { get; } = new Dictionary<string, Gate>();
        // Reverse lookup
        private Dictionary<Gate, string> GateToName { get; } = new Dictionary<Gate, string>();
        // External inputs and outputs
        public Dictionary<string, bool> ExternalInputs { get; } = new Dictionary<string, bool>();
        public List<PortInfo> InputNames { get; } = new List<PortInfo>();
        public Dictionary<string, Gate?> ExternalOutputs { get; } = new Dictionary<string, Gate?>();
        public List<PortInfo> OutputNames { get; } = new List<PortInfo>();

        // Block information for LSP
        public Dictionary<string, BlockInfo> Blocks { get; } = new Dictionary<string, BlockInfo>();

        // Lookup tables for this circuit
        public Dictionary<string, Dictionary<string, bool[]>> LookupTables { get; } = new Dictionary<string, Dictionary<string, bool[]>>();

        // Clock-aware simulation
        public bool PreviousClock { get; set; } = false;
        public Dictionary<string, bool> PreviousExternalOutputs { get; } = new();

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
            // Clock-aware simulation: detect rising edge
            bool currentClock = ExternalInputs.ContainsKey("clk") ? ExternalInputs["clk"] : false;
            bool clockRising = currentClock && !PreviousClock;
            PreviousClock = currentClock;

            // Save current external outputs before convergence
            PreviousExternalOutputs.Clear();
            foreach (var kv in ExternalOutputs)
            {
                if (kv.Value != null)
                {
                    PreviousExternalOutputs[kv.Key] = kv.Value.Output;
                }
            }

            // Dynamic convergence detection: run until no changes or max passes
            Dictionary<Gate, bool> previousOutputs = Gates.ToDictionary(g => g, g => g.Outputs[0]);
            bool changed = true;
            int pass = 0;
            const int MAX_PASSES = 100;
            while (changed && pass < MAX_PASSES)
            {
                changed = false;
                pass++;

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

                // Check for changes
                foreach (var gate in Gates)
                {
                    if (gate.Outputs[0] != previousOutputs[gate])
                    {
                        changed = true;
                        previousOutputs[gate] = gate.Outputs[0];
                    }
                }
            }

            // For sequential circuits, only update external outputs on rising clock edge
            if (currentClock && !clockRising)
            {
                foreach (var kv in PreviousExternalOutputs)
                {
                    if (ExternalOutputs.ContainsKey(kv.Key) && ExternalOutputs[kv.Key] is Gate gate)
                    {
                        gate.Outputs[0] = kv.Value;
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