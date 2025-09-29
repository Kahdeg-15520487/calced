using System;
using System.Collections.Generic;
using System.Linq;

namespace CircuitSimulator
{
    public abstract class Gate
    {
        public List<bool> Inputs { get; set; } = new List<bool>();
        public bool Output { get; protected set; }

        public abstract void Compute();
    }

    public class AndGate : Gate
    {
        public override void Compute()
        {
            Output = Inputs.All(i => i);
        }
    }

    public class OrGate : Gate
    {
        public override void Compute()
        {
            Output = Inputs.Any(i => i);
        }
    }

    public class NotGate : Gate
    {
        public override void Compute()
        {
            if (Inputs.Count != 1) throw new InvalidOperationException("NOT gate must have exactly one input.");
            Output = !Inputs[0];
        }
    }

    public class NandGate : Gate
    {
        public override void Compute()
        {
            Output = !Inputs.All(i => i);
        }
    }

    public class NorGate : Gate
    {
        public override void Compute()
        {
            Output = !Inputs.Any(i => i);
        }
    }

    public class XorGate : Gate
    {
        public override void Compute()
        {
            Output = Inputs.Count(i => i) % 2 == 1;
        }
    }

    public class XnorGate : Gate
    {
        public override void Compute()
        {
            Output = Inputs.Count(i => i) % 2 == 0;
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
            Output = _q;
        }
    }
}