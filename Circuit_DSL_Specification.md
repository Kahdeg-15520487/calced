# Circuit DSL Specification

## Overview

The Circuit DSL (Domain Specific Language) is a text-based format for defining digital circuits composed of logic gates and subcircuits. Circuits are defined in `.circuit` files and can reference other circuits through imports.

The DSL supports both **combinational** and **sequential** circuits. Sequential circuits include a special `clk` input that triggers output updates on rising clock edges, enabling proper simulation of flip-flops, registers, and other state-holding elements.

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
inputs { name1, name2, array_name[size], clk }
```

- Single inputs: `input_name`
- Array inputs: `array_name[size]` creates indexed inputs `array_name[0]` through `array_name[size-1]`
- Size must be a positive integer
- **Special Input: `clk`** - When present, marks the circuit as sequential. External outputs only update on rising clock edges (0→1 transitions), simulating edge-triggered behavior like flip-flops.

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
| `DFF()` | **Sequential** D Flip-Flop | 2 (data + clock) | 1 |

**Sequential Gates:**
- `DFF()`: Captures data input on rising clock edge, holds value until next clock edge
- Requires both data and clock inputs
- Output only changes on clock rising edges (0→1)

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

## Sequential Circuit Behavior

Circuits become **sequential** when they include a `clk` input. Sequential circuits exhibit different simulation behavior:

### Clock Edge Detection
- The simulator automatically detects **rising clock edges** (transitions from 0 to 1 on the `clk` input)
- Clock state is tracked internally to identify edge transitions

### Edge-Triggered Updates
- **Combinational circuits**: Outputs update immediately when inputs change
- **Sequential circuits**: External outputs only update on rising clock edges
- Internal gate computations occur on every simulation tick, but outputs are held constant until the next clock edge

### Sequential Gate Behavior
- Sequential gates like `DFF()` only change state on clock edges
- This enables proper simulation of flip-flops, registers, and other state-holding elements
- Multiple sequential gates in the same circuit synchronize their updates to the same clock edge
- **Implementation Note**: NAND-based SR latches may not converge to correct initial states in combinational simulation. Use NOR-based SR latches for reliable sequential behavior.

### Simulation Timing
- `Tick()` method advances simulation by one time step
- For sequential circuits: internal state converges, but outputs only update if clock rose
- For combinational circuits: outputs update immediately to reflect new input values

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

### Sequential Circuit with DFF

```
circuit Register {
    inputs { data, clk }
    outputs { q }
    gates {
        dff1 = DFF()
    }
    connections {
        data -> dff1.in[0]  // Data input
        clk -> dff1.in[1]   // Clock input
        dff1.out -> q       // Output
    }
}
```

- This circuit captures the `data` input value on each rising edge of `clk`
- The output `q` holds its value between clock edges
- Demonstrates edge-triggered sequential behavior

### NAND-based DFF Implementation

```
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
}
```

- NAND-based D flip-flop using SR latch with clock gating
- `not_d`: Inverts data input
- `nand_s`: Active-low set signal (0 when data=1 and clk=1)
- `nand_r`: Active-low reset signal (0 when data=0 and clk=1)
- `nand_q`/`nand_qbar`: Cross-coupled NAND SR latch
- **⚠️ Limitation**: NAND SR latches have convergence issues in combinational simulation and may not reach correct initial states. NOR-based implementations are recommended for reliable sequential behavior.

### Recommended NOR-based DFF Implementation

```
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
}
```

- NOR-based D flip-flop using SR latch with clock gating
- Provides reliable convergence and proper sequential behavior
- Recommended for implementing DFFs in the Circuit DSL

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
- Connections are validated at parse time for existence of sources and targets
- **Sequential circuits** (with `clk` input) use edge-triggered simulation
- Clock edge detection enables proper timing simulation for state-holding elements
- Internal convergence occurs on every tick, but outputs update only on clock edges for sequential circuits
- **NAND SR latches may have convergence issues** - use NOR SR latches for reliable sequential behavior</content>
<parameter name="filePath">j:\workspace2\c#\calced\Circuit_DSL_Specification.md