using System.Collections.Generic;
using Xunit;
using CircuitSimulator.Core;

namespace CircuitSimulator.Tests
{
    public class DSLParserTests
    {
        [Fact]
        public void Parse_SimpleCircuit_CreatesCorrectCircuit()
        {
            var dsl = @"
circuit TestCircuit {
    inputs { a, b }
    outputs { out1 }
    gates {
        and1 = AND()
        or1 = OR()
    }
    connections {
        a -> and1.in[0]
        b -> and1.in[1]
        and1.out -> or1.in[0]
        or1.out -> out1
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            Assert.Equal(2, circuit.Gates.Count);
            Assert.True(circuit.NamedGates.ContainsKey("and1"));
            Assert.True(circuit.NamedGates.ContainsKey("or1"));
            Assert.True(circuit.ExternalInputs.ContainsKey("a"));
            Assert.True(circuit.ExternalInputs.ContainsKey("b"));
            Assert.True(circuit.ExternalOutputs.ContainsKey("out1"));
        }

        [Fact]
        public void Parse_ArrayInputs_ExpandsCorrectly()
        {
            var dsl = @"
circuit TestCircuit {
    inputs { data[2] }
    gates {
        and1 = AND()
    }
    connections {
        data[0] -> and1.in[0]
        data[1] -> and1.in[1]
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            Assert.True(circuit.ExternalInputs.ContainsKey("data[0]"));
            Assert.True(circuit.ExternalInputs.ContainsKey("data[1]"));
            Assert.Equal(2, circuit.Connections[circuit.NamedGates["and1"]].Count);
        }

        [Fact]
        public void Parse_ComputesCorrectly()
        {
            var dsl = @"
circuit TestCircuit {
    inputs { a, b }
    outputs { out1 }
    gates {
        and1 = AND()
    }
    connections {
        a -> and1.in[0]
        b -> and1.in[1]
        and1.out -> out1
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            // Set external inputs
            circuit.ExternalInputs["a"] = true;
            circuit.ExternalInputs["b"] = false;

            circuit.Tick();

            Assert.False(circuit.NamedGates["and1"].Output);
        }

        [Fact]
        public void Visualize_ReturnsCorrectText()
        {
            var dsl = @"
circuit TestCircuit {
    inputs { a }
    outputs { out1 }
    gates {
        not1 = NOT()
    }
    connections {
        a -> not1.in[0]
        not1.out -> out1
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            var visualization = circuit.Visualize();

            // Debug
            Console.WriteLine("Visualization:");
            Console.WriteLine(visualization);

            Assert.Contains("Gates:", visualization);
            Assert.Contains("not1: NOT", visualization);
            Assert.Contains("Connections:", visualization);
            Assert.Contains("a -> not1[0]", visualization);
            Assert.Contains("External Inputs:", visualization);
            Assert.Contains("a: False", visualization);
            Assert.Contains("External Outputs:", visualization);
            Assert.Contains("out1: not1", visualization);
        }

        [Fact]
        public void Parse_WithNewParser_SimpleCircuit_CreatesCorrectCircuit()
        {
            var dsl = @"
circuit TestCircuit {
    inputs { a, b }
    outputs { out1 }
    gates {
        and1 = AND()
    }
    connections {
        a -> and1.in[0]
        b -> and1.in[1]
        and1.out -> out1
    }
}
";
            var lexer = new Lexer(dsl);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, ".", "test.circuit");
            var circuits = parser.ParseCircuits();
            var circuit = circuits.LastOrDefault().Value;

            Assert.Equal("TestCircuit", circuit.Name);
            Assert.Single(circuit.Gates);
            Assert.True(circuit.NamedGates.ContainsKey("and1"));
            Assert.True(circuit.ExternalInputs.ContainsKey("a"));
            Assert.True(circuit.ExternalInputs.ContainsKey("b"));
            Assert.True(circuit.ExternalOutputs.ContainsKey("out1"));
        }
    }
}