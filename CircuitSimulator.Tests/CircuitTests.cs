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

        [Theory]
        [InlineData(@"
circuit NandRegister {
    inputs { data, clk }
    outputs { q }
    gates {
        not_d = NAND()      // ~data
        nand_s = NAND()     // S' = ~(data & clk)
        nand_r = NAND()     // R' = ~(~data & clk)
        nand_q = NAND()     // Q = ~(R' & Q')
        nand_qbar = NAND()  // Q' = ~(S' & Q)
    }
    connections {
        data -> not_d.in[0]     // ~data
        data -> not_d.in[1]
        
        data -> nand_s.in[0]    // S' = ~(data & clk)
        clk -> nand_s.in[1]
        
        not_d.out -> nand_r.in[0]  // R' = ~(~data & clk)
        clk -> nand_r.in[1]
        
        nand_r.out -> nand_q.in[0]   // Q = ~(R' & Q')
        nand_qbar.out -> nand_q.in[1]
        
        nand_s.out -> nand_qbar.in[0]  // Q' = ~(S' & Q)
        nand_q.out -> nand_qbar.in[1]
        
        nand_q.out -> q           // Output
    }
}")]
        [InlineData(@"
circuit DflipFlop {
    inputs { d, clk }
    outputs { q }
    gates {
        not_d = NOT()
        and_s = AND()
        and_r = AND()
        nor_q = NOR()
        nor_qbar = NOR()
    }
    connections {
        d -> not_d.in[0]
        d -> and_s.in[0]
        clk -> and_s.in[1]
        not_d.out -> and_r.in[0]
        clk -> and_r.in[1]
        and_r.out -> nor_q.in[0]
        nor_qbar.out -> nor_q.in[1]
        and_s.out -> nor_qbar.in[0]
        nor_q.out -> nor_qbar.in[1]
        nor_q.out -> q
    }
}")]
[InlineData(@"
circuit NorRegister {
    inputs { data, clk }
    outputs { q }
    gates {
        not_d = NOT()       // ~data
        and_s = AND()       // S = data & clk
        and_r = AND()       // R = ~data & clk
        nor_q = NOR()       // Q = ~(R | Q')
        nor_qbar = NOR()    // Q' = ~(S | Q)
    }
    connections {
        data -> not_d.in[0]     // ~data
        
        data -> and_s.in[0]     // S = data & clk
        clk -> and_s.in[1]
        
        not_d.out -> and_r.in[0]   // R = ~data & clk
        clk -> and_r.in[1]
        
        and_r.out -> nor_q.in[0]    // Q = ~(R | Q')
        nor_qbar.out -> nor_q.in[1]
        
        and_s.out -> nor_qbar.in[0] // Q' = ~(S | Q)
        nor_q.out -> nor_qbar.in[1]
        
        nor_q.out -> q            // Output
    }
}")]
        public void SequentialCircuit_BehavesCorrectlyOverTicks(string dsl)
        {
            // Load the sequential test circuit
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "sequential_test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            Assert.NotNull(circuit);
            Assert.True(circuit.ExternalOutputs.ContainsKey("q"));
            var qGate = circuit.ExternalOutputs["q"];
            Assert.NotNull(qGate);

            // Initial state: q is false (converged state of NOR SR latch with default inputs)
            Assert.False(qGate.Output);

            // Set data=1, clk=0, tick -> q still false (hold)
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Set clk=1, tick -> q becomes true (set)
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Set data=0, clk=0, tick -> q still true (hold)
            circuit.ExternalInputs["d"] = false;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Set clk=1, tick -> q becomes false (reset)
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

            // Additional test cycles
            // Cycle 1: d=0, clk=0 -> hold
            circuit.ExternalInputs["d"] = false;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Cycle 1: clk=1 -> reset to 0
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Cycle 2: d=1, clk=0 -> hold
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Cycle 2: clk=1 -> set to 1
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Cycle 3: d=1, clk=0 -> hold
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Cycle 3: clk=1 -> stay 1
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Cycle 4: d=0, clk=0 -> hold
            circuit.ExternalInputs["d"] = false;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Cycle 4: clk=1 -> reset to 0
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Additional cycle: Test that DFF only updates on rising edge
            // Tick 1: d=1, clk=0 -> q stays low (hold)
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(qGate.Output);

            // Tick 2: d=1, clk=1 (rising edge) -> q becomes high (capture)
            circuit.ExternalInputs["d"] = true;
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);

            // Tick 3: clk=1 (no edge), d=0 -> q stays high (hold)
            circuit.ExternalInputs["d"] = false;
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(qGate.Output);
        }

        [Fact]
        public void ClockEdgeDetection_WorksCorrectly()
        {
            var circuit = new Circuit();
            // Add a simple circuit with clk input
            circuit.ExternalInputs["clk"] = false;

            // Initial state
            Assert.False(circuit.PreviousClock);

            // Tick with clk=false (no rising edge)
            circuit.Tick();
            Assert.False(circuit.PreviousClock);

            // Set clk=true (rising edge)
            circuit.ExternalInputs["clk"] = true;
            circuit.Tick();
            Assert.True(circuit.PreviousClock);

            // Tick again with clk=true (no rising edge)
            circuit.Tick();
            Assert.True(circuit.PreviousClock);

            // Set clk=false (falling edge)
            circuit.ExternalInputs["clk"] = false;
            circuit.Tick();
            Assert.False(circuit.PreviousClock);
        }
    }
}