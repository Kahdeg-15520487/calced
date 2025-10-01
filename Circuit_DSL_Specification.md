# Circuit DSL Specification

## Overview

The Circuit DSL (Domain Specific Language) is a text-based format for defining digital circuits composed of logic gates and subcircuits. Circuits are defined in `.circuit` files and can reference other circuits through imports.

## File Structure

```
[import statements...]

circuit CircuitName {
    inputs { input_list }
    outputs { output_list }
    gates { gate_definitions }
    connections { connection_definitions }
}
```

## Syntax Elements

### Import Statements

```
import "filename.circuit"
```

- Imports circuits from external files
- Must appear before circuit definitions
- Referenced circuits become available for use as subcircuits

### Circuit Declaration

```
circuit CircuitName {
    // circuit body
}
```

- `CircuitName` must be a valid identifier (alphanumeric + underscore)
- Multiple circuits can be defined per file

### Inputs Block

```
inputs { name1, name2, array_name[size] }
```

- Single inputs: `input_name`
- Array inputs: `array_name[size]` creates indexed inputs `array_name[0]` through `array_name[size-1]`
- Size must be a positive integer

### Outputs Block

```
outputs { name1, name2 }
```

- Defines circuit output pins
- Simple names only (no arrays)

### Lookup Tables Block

```
lookup_tables {
    table_name = {
        input_binary -> output_binary
        input_binary -> output_binary
        ...
    }
}
```

- Defines custom truth tables for arbitrary logic functions
- `input_binary` is a binary string representing all input combinations (e.g., "00", "01", "10", "11" for 2 inputs)
- `output_binary` is a binary string representing multiple output bits (e.g., "10" for 2-bit output)
- Tables are referenced by name in gate definitions
- Input count is determined by the length of input binary strings
- Output count is determined by the length of output binary strings

### Gates Block

```
gates {
    gate_name = GateType()
    subcircuit_name = Circuit("CircuitName")
    custom_name = LookupTable("table_name")
}
```

#### Custom Gates

```
custom_name = LookupTable("table_name")
```

- Creates a gate that uses the specified lookup table
- Input count is determined by the table's input binary string length
- Output count is determined by the table's output binary string length
- Individual outputs can be accessed using `gate_name.out[index]` syntax

#### Built-in Gate Types

| Gate | Description | Inputs | Outputs |
|------|-------------|--------|---------|
| `AND()` | Logical AND | 2 | 1 |
| `OR()` | Logical OR | 2 | 1 |
| `NOT()` | Inverter | 1 | 1 |
| `NAND()` | NAND gate | 2 | 1 |
| `NOR()` | NOR gate | 2 | 1 |
| `XOR()` | Exclusive OR | 2 | 1 |
| `XNOR()` | Exclusive NOR | 2 | 1 |
| `DFF()` | D Flip-Flop | 1 (data) + 1 (clock) | 1 |

#### Subcircuits

```
subcircuit_name = Circuit("CircuitName")
```

- References a circuit defined in the current file or imported files
- Circuit must exist at parse time

### Connections Block

```
connections {
    source -> target.in[index]
    source -> output_name
}
```

#### Connection Types

1. **Gate Input Connections:**
   ```
   source -> gate_name.in[index]
   ```
   - Connects to specific input pin of a gate
   - `index` is 0-based pin number

2. **Circuit Output Connections:**
   ```
   source -> output_name
   ```
   - Routes internal signals to circuit outputs

#### Source Formats

- **Gate outputs:** `gate_name.out`
- **Subcircuit outputs:** `subcircuit_name.out` or `subcircuit_name.out[index]`
- **Circuit inputs:** `input_name` or `array_input[index]`

## Comments

- Lines beginning with `//` are treated as comments
- Comments can appear anywhere in the file

## Examples

### Basic Gate Circuit

```
circuit BasicTest {
    inputs { a, b }
    outputs { result }
    gates {
        and1 = AND()
    }
    connections {
        a -> and1.in[0]
        b -> and1.in[1]
        and1.out -> result
    }
}
```

### Circuit with Arrays

```
circuit MultiBitTest {
    inputs { value[2] }
    outputs { result }
    gates {
        or1 = OR()
    }
    connections {
        value[0] -> or1.in[0]
        value[1] -> or1.in[1]
        or1.out -> result
    }
}
```

### Circuit with Multi-Bit Lookup Tables

```
circuit ALU {
    inputs { a, b, op }
    outputs { result0, result1 }
    lookup_tables {
        alu_table = {
            000 -> 00  // NOP
            001 -> 01  // Load 1
            010 -> 10  // Load 2
            011 -> 11  // Load 3
            100 -> 10  // Add: 1+0=1 (01 in binary, but simplified)
            101 -> 11  // Add+1: 1+0+1=2 (10 in binary, but simplified)
            110 -> 00  // AND: 1&0=0
            111 -> 01  // OR: 1|0=1
        }
    }
    gates {
        alu = LookupTable("alu_table")
    }
    connections {
        a -> alu.in[0]
        b -> alu.in[1]
        op -> alu.in[2]
        alu.out[0] -> result0
        alu.out[1] -> result1
    }
}
```

## Error Handling

The parser throws specific exceptions for common errors:

- `DSLImportException`: Failed to import external circuit file
- `DSLInvalidSyntaxException`: Malformed syntax in any block
- `DSLInvalidGateException`: Unknown gate type, missing circuit reference, or undefined lookup table
- `DSLInvalidConnectionException`: Invalid connection syntax or missing source/target

All exceptions inherit from `DSLParseException` for unified error handling.

## Implementation Notes

- Circuit names are case-sensitive
- Gate and signal names follow C# identifier rules
- Array indices are 0-based
- Subcircuits are instantiated as `CircuitGate` objects
- Connections are validated at parse time for existence of sources and targets</content>
<parameter name="filePath">j:\workspace2\c#\calced\Circuit_DSL_Specification.md