---
mode: agent
---
Define the task to achieve, including specific requirements, constraints, and success criteria.

create a program that consist of the following features:
1. digital sequential circuit combined from logic gates (AND, OR, NOT, NAND, NOR, XOR, XNOR)
2. ability to simulate the circuit and display the output based on given inputs
3. DSL (Domain-Specific Language) to define the circuit structure and connections
4. visualization of the circuit and its state at each tick 
5. simple wrapper that take the DSL as input and simulate the circuit tick by tick then output the result as a table showing the state of each gate at each tick
6. ability to save and load circuit definitions from files
7. the simulator must be able to emulate a simple 8 bit cpu designed using the DSL

The program should be implemented in C#. Each feature should be developed incrementally, with unit tests created for each component to ensure correctness. The final deliverable should include the complete source code, documentation on how to use the DSL, and examples of circuit definitions, including an example of an 8-bit CPU. The program should be modular to allow for future extensions, such as adding more complex gates or features.