# DSL Syntax Brainstorm for Circuit Simulator

## Proposed Syntax Options

### Option 1: Simple Line-Based Syntax
```
# Define gates
gate and1 AND
gate or1 OR
gate not1 NOT
gate dff1 DFF

# Define connections
connect and1 to or1.0
connect not1 to or1.1
connect or1 to dff1.0

# Define inputs
input in1
input clk

# Connect inputs
connect in1 to and1.0
connect clk to dff1.1

# Define outputs
output out1
connect dff1 to out1
```

### Option 2: Block-Based Syntax
```
circuit MyCircuit {
    gates {
        and1 = AND()
        or1 = OR()
        not1 = NOT()
        dff1 = DFF()
    }

    connections {
        and1.out -> or1.in[0]
        not1.out -> or1.in[1]
        or1.out -> dff1.d
        clk -> dff1.clk
    }

    inputs {
        in1 -> and1.in[0]
        clk
    }

    outputs {
        out1 <- dff1.q
    }
}
```

### Option 3: JSON-Based (for simplicity)
```json
{
  "gates": [
    {"name": "and1", "type": "AND"},
    {"name": "or1", "type": "OR"}
  ],
  "connections": [
    {"from": "and1", "to": "or1", "input": 0}
  ],
  "inputs": ["in1"],
  "outputs": ["out1"]
}
```

### Option 4: Verilog-inspired
```
module MyCircuit(in1, clk, out1);
    input in1, clk;
    output out1;

    wire w1, w2;

    AND and1(.a(in1), .b(1'b1), .out(w1));
    OR or1(.a(w1), .b(1'b0), .out(w2));
    DFF dff1(.d(w2), .clk(clk), .q(out1));
endmodule
```

## Decision
We chose **Option 2: Block-Based Syntax** for its clean, structured organization and better readability for complex circuits.

Refined syntax:
```
circuit CircuitName {
    inputs { name1, name2, bus[8] }  # single signals and buses
    
    outputs { out1, out2 }
    
    gates {
        gate1 = GateType()
        gate2 = GateType()
    }
    
    subcircuits {
        sub1 = SubCircuitType()
        sub2 = SubCircuitType()
    }
    
    connections {
        source -> target.input[index]
        gate1.out -> gate2.in[0]
        input1 -> gate1.in[0]
        sub1.output -> sub2.input
        constant1 -> gate.in[1]  # for constants 0/1
    }
}
```

Key advantages:
- Clear separation of concerns (gates, connections, I/O)
- Hierarchical support with subcircuits
- Bus/array notation for multi-bit signals
- Easy to parse with block structure
- Extensible for future features

## Hierarchical Circuits Extension

Full support for reusable components:

```
circuit FullAdder {
    inputs { a, b, cin }
    outputs { sum, cout }
    
    gates {
        xor1 = XOR()
        and1 = AND()
        or1 = OR()
    }
    
    connections {
        a -> xor1.in[0]
        b -> xor1.in[1]
        # ... full adder logic
    }
}

circuit FourBitAdder {
    inputs { a[4], b[4], cin }
    outputs { sum[4], cout }
    
    subcircuits {
        fa[4] = FullAdder()  # array of subcircuits
    }
    
    connections {
        a[0] -> fa[0].a
        b[0] -> fa[0].b
        cin -> fa[0].cin
        fa[0].sum -> sum[0]
        fa[0].cout -> fa[1].cin
        # ... ripple carry chain
    }
}
```