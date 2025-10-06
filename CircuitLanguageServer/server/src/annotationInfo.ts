// Gate information for hover tooltips
export const AnnotationInfo: { [key: string]: string } = {
	'AND': 'Logical AND gate\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 0\n0 | 1 | 0\n1 | 0 | 0\n1 | 1 | 1\n```',
	'OR': 'Logical OR gate\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 0\n0 | 1 | 1\n1 | 0 | 1\n1 | 1 | 1\n```',
	'NOT': 'Inverter gate\n\nInputs: 1\nOutputs: 1\n\nTruth table:\n```\nA | Y\n0 | 1\n1 | 0\n```',
	'NAND': 'NAND gate (NOT AND)\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 1\n0 | 1 | 1\n1 | 0 | 1\n1 | 1 | 0\n```',
	'NOR': 'NOR gate (NOT OR)\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 1\n0 | 1 | 0\n1 | 0 | 0\n1 | 1 | 0\n```',
	'XOR': 'Exclusive OR gate\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 0\n0 | 1 | 1\n1 | 0 | 1\n1 | 1 | 0\n```',
	'XNOR': 'Exclusive NOR gate\n\nInputs: 2\nOutputs: 1\n\nTruth table:\n```\nA | B | Y\n0 | 0 | 1\n0 | 1 | 0\n1 | 0 | 0\n1 | 1 | 1\n```',
	'DFF': 'D Flip-Flop\n\nInputs: 2 (data + clock)\nOutputs: 1\n\nStores the data input value when clock edge occurs.',
	'circuit': 'Circuit declaration keyword\n\nDefines a new circuit with inputs, outputs, gates, and connections.',
	'inputs': 'Inputs block\n\nDefines the input pins of the circuit.\n\nSyntax: `inputs { name1, name2, array[size] }`',
	'outputs': 'Outputs block\n\nDefines the output pins of the circuit.\n\nSyntax: `outputs { name1, name2 }`',
	'gates': 'Gates block\n\nDefines logic gates and subcircuits.\n\nSyntax: `gates { gate_name = GateType() }`',
	'connections': 'Connections block\n\nDefines signal connections between gates.\n\nSyntax: `connections { source -> target }`',
	'lookup_tables': 'Lookup Tables block\n\nDefines custom truth tables for arbitrary logic.\n\nSyntax: `lookup_tables { name = { input -> output } }`'
};