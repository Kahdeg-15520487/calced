# Circuit DSL Syntax - Implementation

## Current Syntax: Block-Based

```
circuit CircuitName {
    inputs { name1, name2, bus[8] }  # single signals and buses

    outputs { out1, out2 }

    gates {
        gate1 = GateType()
        gate2 = GateType()
        sub1 = Circuit("subcircuit.circuit")
    }

    connections {
        source -> target.in[index]
        gate1.out -> gate2.in[0]
        input1 -> gate1.in[0]
        sub1.out[0] -> gate2.in[1]  # subcircuit output indexing
        gate2.out -> out1
    }
}
```

## Supported Features

### Subcircuits
- **External files**: `Circuit("filename.circuit")`
- **Same file**: `Circuit("CircuitName")` - references circuits defined in the same file

### Multiple Circuits in One File
You can define multiple circuits in a single file and reference them without repetition:

```
circuit HalfAdder {
    inputs { a, b }
    outputs { sum, carry }
    gates {
        xor1 = XOR()
        and1 = AND()
    }
    connections {
        a -> xor1.in[0]; b -> xor1.in[1]
        a -> and1.in[0]; b -> and1.in[1]
        xor1.out -> sum; and1.out -> carry
    }
}

circuit FullAdder {
    inputs { a, b, cin }
    outputs { sum, cout }
    gates {
        ha1 = Circuit("HalfAdder")  // References HalfAdder in same file
        ha2 = Circuit("HalfAdder")
        or1 = OR()
    }
    connections {
        a -> ha1.in[0]; b -> ha1.in[1]
        ha1.out[0] -> ha2.in[0]; cin -> ha2.in[1]
        ha1.out[1] -> or1.in[0]; ha2.out[1] -> or1.in[1]
        ha2.out[0] -> sum; or1.out -> cout
    }
}
```

## Example: Full Adder

```
circuit FullAdder {
    inputs { a, b, cin }
    outputs { sum, cout }
    gates {
        ha1 = Circuit("halfadder.circuit")
        ha2 = Circuit("halfadder.circuit")
        or1 = OR()
    }
    connections {
        a -> ha1.in[0]
        b -> ha1.in[1]
        ha1.out[0] -> ha2.in[0]
        cin -> ha2.in[1]
        ha1.out[1] -> or1.in[0]
        ha2.out[1] -> or1.in[1]
        ha2.out[0] -> sum
        or1.out -> cout
    }
}
```

## Input Parsing
Command-line inputs support multiple formats:
- Boolean: `--input=true` or `--input=false`
- Decimal: `--input=12` (converted to binary)
- Binary: `--input=b1100`
- Hexadecimal: `--input=hC`