using System.Collections.Generic;
using System.IO;
using Xunit;
using CircuitSimulator.Core;

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

        [Fact]
        public void SequentialCircuit_BehavesCorrectlyOverTicks()
        {
            // Load the sequential test circuit
            var dsl = @"
circuit DflipFlop {
    inputs { d, clk }
    outputs { q }
    gates {
        nand1 = NAND()  // clock inverter
        nand2 = NAND()  // master latch
        nand3 = NAND()  // master latch
        nand4 = NAND()  // slave latch
        nand5 = NAND()  // slave latch
    }
    connections {
        clk -> nand1.in[0]
        clk -> nand1.in[1]  // ~clk = nand1.out
        
        d -> nand2.in[0]
        nand1.out -> nand2.in[1]  // d & ~clk
        
        nand2.out -> nand3.in[0]
        nand1.out -> nand3.in[1]  // master latch feedback
        
        nand3.out -> nand4.in[0]
        clk -> nand4.in[1]  // slave latch
        
        nand4.out -> nand5.in[0]
        clk -> nand5.in[1]  // slave latch feedback
        
        nand5.out -> q
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "sequential_test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            Assert.NotNull(circuit);
            Assert.True(circuit.ExternalOutputs.ContainsKey("q"));
            var qGate = circuit.ExternalOutputs["q"];
            Assert.NotNull(qGate);

            // Initial state: q should be false
            Assert.False(qGate.Output);

            // Set data=1, clk=0, tick -> q still false (no rising edge)
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Set clk=1, tick -> q becomes true (rising edge captures data)
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Set data=0, clk=0, tick -> q still true (holds value)
            circuit.ExternalInputs["d"] = false;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Set clk=1, tick -> q becomes false (rising edge captures new data)
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Additional iterations: Set data=1, clk=0, tick -> q still false
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(qGate.Output);

            // clk=1, tick -> q becomes true
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);
        }
    }
}