using System.Collections.Generic;
using Xunit;

namespace CircuitSimulator.Tests
{
    public class LogicGatesTests
    {
        [Fact]
        public void AndGate_AllTrue_ReturnsTrue()
        {
            var gate = new AndGate();
            gate.Inputs = new List<bool> { true, true, true };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void AndGate_HasFalse_ReturnsFalse()
        {
            var gate = new AndGate();
            gate.Inputs = new List<bool> { true, false, true };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void OrGate_HasTrue_ReturnsTrue()
        {
            var gate = new OrGate();
            gate.Inputs = new List<bool> { false, true, false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void OrGate_AllFalse_ReturnsFalse()
        {
            var gate = new OrGate();
            gate.Inputs = new List<bool> { false, false, false };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void NotGate_TrueInput_ReturnsFalse()
        {
            var gate = new NotGate();
            gate.Inputs = new List<bool> { true };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void NotGate_FalseInput_ReturnsTrue()
        {
            var gate = new NotGate();
            gate.Inputs = new List<bool> { false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void NandGate_AllTrue_ReturnsFalse()
        {
            var gate = new NandGate();
            gate.Inputs = new List<bool> { true, true };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void NandGate_HasFalse_ReturnsTrue()
        {
            var gate = new NandGate();
            gate.Inputs = new List<bool> { true, false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void NorGate_AllFalse_ReturnsTrue()
        {
            var gate = new NorGate();
            gate.Inputs = new List<bool> { false, false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void NorGate_HasTrue_ReturnsFalse()
        {
            var gate = new NorGate();
            gate.Inputs = new List<bool> { false, true };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void XorGate_OddTrue_ReturnsTrue()
        {
            var gate = new XorGate();
            gate.Inputs = new List<bool> { true, false, false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void XorGate_EvenTrue_ReturnsFalse()
        {
            var gate = new XorGate();
            gate.Inputs = new List<bool> { true, true, false };
            gate.Compute();
            Assert.False(gate.Output);
        }

        [Fact]
        public void XnorGate_EvenTrue_ReturnsTrue()
        {
            var gate = new XnorGate();
            gate.Inputs = new List<bool> { true, true, false };
            gate.Compute();
            Assert.True(gate.Output);
        }

        [Fact]
        public void XnorGate_OddTrue_ReturnsFalse()
        {
            var gate = new XnorGate();
            gate.Inputs = new List<bool> { true, false, false };
            gate.Compute();
            Assert.False(gate.Output);
        }
    }
}