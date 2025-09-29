using System.Collections.Generic;
using Xunit;

namespace CircuitSimulator.Tests
{
    public class CircuitTests
    {
        [Fact]
        public void AddGate_AddsGateToCircuit()
        {
            var circuit = new Circuit();
            var gate = new AndGate();
            circuit.AddGate("and1", gate);
            Assert.Contains(gate, circuit.Gates);
            Assert.Contains(gate, circuit.Connections.Keys);
            Assert.Equal(gate, circuit.NamedGates["and1"]);
        }

        [Fact]
        public void Connect_ConnectsGatesCorrectly()
        {
            var circuit = new Circuit();
            var source = new AndGate();
            var target = new OrGate();
            circuit.AddGate("and1", source);
            circuit.AddGate("or1", target);
            circuit.Connect(source, target, 0);
            Assert.Equal(source, circuit.Connections[target][0]);
        }

        [Fact]
        public void Tick_PropagatesSignals()
        {
            var circuit = new Circuit();
            var andGate = new AndGate();
            var orGate = new OrGate();
            circuit.AddGate("and1", andGate);
            circuit.AddGate("or1", orGate);
            circuit.Connect(andGate, orGate, 0);

            // Set andGate inputs manually (since no external inputs yet)
            andGate.Inputs = new List<bool> { true, true };
            andGate.Compute(); // Manually compute for now

            circuit.Tick();

            Assert.True(orGate.Inputs[0]); // Should be andGate.Output
            Assert.True(orGate.Output); // OR with true and false (unconnected is false)
        }

        [Fact]
        public void DFlipFlop_UpdatesOnClock()
        {
            var dff = new DFlipFlop();
            dff.Inputs = new List<bool> { true, false }; // D=true, CLK=false
            dff.Compute();
            Assert.False(dff.Output); // No change

            dff.Inputs = new List<bool> { true, true }; // CLK=true
            dff.Compute();
            Assert.True(dff.Output); // Now Q=true

            dff.Inputs = new List<bool> { false, false }; // D=false, CLK=false
            dff.Compute();
            Assert.True(dff.Output); // Still true

            dff.Inputs = new List<bool> { false, true }; // CLK=true
            dff.Compute();
            Assert.False(dff.Output); // Now Q=false
        }
    }
}