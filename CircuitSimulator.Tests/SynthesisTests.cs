using System;
using System.IO;
using Xunit;

namespace CircuitSimulator.Tests
{
    public class SynthesisTests
    {
        [Fact]
        public void GenerateDSL_SimpleAndGate()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "and(a,b)");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = AND()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("b -> gate0.in[1]", dsl);
            Assert.Contains("gate0.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_XorWithAndOr()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "xor(and(a,b),or(a,c))");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b, c }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = AND()", dsl);
            Assert.Contains("gate1 = OR()", dsl);
            Assert.Contains("gate2 = XOR()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("b -> gate0.in[1]", dsl);
            Assert.Contains("a -> gate1.in[0]", dsl);
            Assert.Contains("c -> gate1.in[1]", dsl);
            Assert.Contains("gate0.out -> gate2.in[0]", dsl);
            Assert.Contains("gate1.out -> gate2.in[1]", dsl);
            Assert.Contains("gate2.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_NotGate()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "not(a)");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = NOT()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("gate0.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_NandGate()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "nand(a,b)");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = NAND()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("b -> gate0.in[1]", dsl);
            Assert.Contains("gate0.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_NorGate()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "nor(a,b)");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = NOR()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("b -> gate0.in[1]", dsl);
            Assert.Contains("gate0.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_XnorGate()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "xnor(a,b)");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = XNOR()", dsl);
            Assert.Contains("a -> gate0.in[0]", dsl);
            Assert.Contains("b -> gate0.in[1]", dsl);
            Assert.Contains("gate0.out -> result", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_ComplexExpression()
        {
            var builder = new Synthesizer();
            string dsl = builder.GenerateDSL("TestCircuit", "or(and(a,b),and(not(c),d))");
            
            // Verify the DSL contains expected elements
            Assert.Contains("circuit TestCircuit {", dsl);
            Assert.Contains("inputs { a, b, c, d }", dsl);
            Assert.Contains("outputs { result }", dsl);
            Assert.Contains("gate0 = AND()", dsl);
            Assert.Contains("gate1 = NOT()", dsl);
            Assert.Contains("gate2 = AND()", dsl);
            Assert.Contains("gate3 = OR()", dsl);
            
            // Verify it parses correctly
            Assert.True(TryParseDSL(dsl));
        }

        [Fact]
        public void GenerateDSL_InvalidOperator_ThrowsException()
        {
            var builder = new Synthesizer();
            Assert.Throws<Exception>(() => builder.GenerateDSL("TestCircuit", "invalid(a,b)"));
        }

        [Fact]
        public void GenerateDSL_EmptyExpression_ThrowsException()
        {
            var builder = new Synthesizer();
            Assert.Throws<Exception>(() => builder.GenerateDSL("TestCircuit", ""));
        }

        [Fact]
        public void GenerateDSL_WrongArity_ThrowsException()
        {
            var builder = new Synthesizer();
            Assert.Throws<Exception>(() => builder.GenerateDSL("TestCircuit", "and(a)"));
            Assert.Throws<Exception>(() => builder.GenerateDSL("TestCircuit", "not(a,b)"));
        }

        private bool TryParseDSL(string dsl)
        {
            try
            {
                var lexer = new Lexer(dsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, ".", "test.circuit");
                var circuits = parser.ParseCircuits();
                return circuits.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}