import { CompletionItem, CompletionItemKind, TextDocumentPositionParams, InsertTextFormat } from 'vscode-languageserver/node';

import { connection, documents } from './connection';
import { getCircuitInfo } from './documents';
import * as path from 'path';
import { fileURLToPath } from 'url';

// Helper function to get gate type from gate name
function getGateType(text: string, gateName: string): string {
    // Simple regex to find gate definition like "gateName = GATE_TYPE()"
    const gateRegex = new RegExp(`\\b${gateName}\\s*=\\s*(\\w+)\\s*\\(`, 'g');
    const match = gateRegex.exec(text);
    return match ? match[1] : '';
}

// Helper function to get ports for a gate type
function getPortsForGate(gateType: string): string[] {
    switch (gateType.toUpperCase()) {
        case 'NOT':
            return ['in[0]', 'out'];
        case 'AND':
        case 'OR':
        case 'NAND':
        case 'NOR':
        case 'XOR':
        case 'XNOR':
            return ['in[0]', 'in[1]', 'out'];
        case 'DFF':
            return ['d', 'clk', 'out'];
        default:
            return ['in[0]', 'in[1]', 'out']; // Default for unknown gates
    }
}

export function initializeCompletionHandlers(): void {
	// This handler provides context-aware completion items.
	connection.onCompletion(
		(textDocumentPosition: TextDocumentPositionParams): CompletionItem[] => {
			const document = documents.get(textDocumentPosition.textDocument.uri);
			if (!document) {
				return [];
			}

			const position = textDocumentPosition.position;
			const text = document.getText();
			const lines = text.split('\n');
			const currentLine = lines[position.line] || '';
			const lineUpToCursor = currentLine.substring(0, position.character);

			// Get circuit info for completion
			const documentPath = fileURLToPath(textDocumentPosition.textDocument.uri);
			const documentDir = path.dirname(documentPath);
			const circuitInfos = getCircuitInfo(text, documentDir, documentPath);

			// Context-aware suggestions
			const suggestions: CompletionItem[] = [];

			// If we're at the start of a line or after whitespace, suggest top-level keywords
			if (lineUpToCursor.trim() === '' || /^\s*$/.test(lineUpToCursor)) {
				suggestions.push(
					{
						label: 'circuit',
						kind: CompletionItemKind.Snippet,
						insertText: 'circuit ${1:CircuitName} {\n\tinputs { ${2:input1} }\n\toutputs { ${3:output1} }\n\tgates {\n\t\t${4:gate1} = ${5:AND}()\n\t}\n\tconnections {\n\t\t${6:input1} -> ${7:gate1}.in[0]\n\t\t${8:gate1}.out -> ${9:output1}\n\t}\n}',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Create a new circuit with basic structure'
					},
					{
						label: 'import',
						kind: CompletionItemKind.Snippet,
						insertText: 'import "${1:filename.circuit}"',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Import another circuit file'
					}
				);
			}

			// Inside circuit block
			if (text.includes('circuit') && /circuit\s+\w+\s*\{[^}]*$/.test(text.substring(0, document.offsetAt(position)))) {
				suggestions.push(
					{
						label: 'inputs',
						kind: CompletionItemKind.Snippet,
						insertText: 'inputs { ${1:input_name} }',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Define circuit inputs'
					},
					{
						label: 'outputs',
						kind: CompletionItemKind.Snippet,
						insertText: 'outputs { ${1:output_name} }',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Define circuit outputs'
					},
					{
						label: 'lookup_tables',
						kind: CompletionItemKind.Snippet,
						insertText: 'lookup_tables {\n\t${1:table_name} = {\n\t\t${2:00} -> ${3:0}\n\t\t${4:01} -> ${5:1}\n\t}\n}',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Define lookup tables for custom logic'
					},
					{
						label: 'gates',
						kind: CompletionItemKind.Snippet,
						insertText: 'gates {\n\t${1:gate_name} = ${2:AND}()\n}',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Define gates and subcircuits'
					},
					{
						label: 'connections',
						kind: CompletionItemKind.Snippet,
						insertText: 'connections {\n\t${1:source} -> ${2:target}\n}',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Define signal connections'
					}
				);
			}

			// Inside gates block
			if (lineUpToCursor.includes('=') || /gates\s*\{[^}]*$/.test(text.substring(0, document.offsetAt(position)))) {
				const gateTypes = [
					{ name: 'AND', desc: 'Logical AND gate (2 inputs, 1 output)' },
					{ name: 'OR', desc: 'Logical OR gate (2 inputs, 1 output)' },
					{ name: 'NOT', desc: 'Inverter gate (1 input, 1 output)' },
					{ name: 'NAND', desc: 'NAND gate (2 inputs, 1 output)' },
					{ name: 'NOR', desc: 'NOR gate (2 inputs, 1 output)' },
					{ name: 'XOR', desc: 'Exclusive OR gate (2 inputs, 1 output)' },
					{ name: 'XNOR', desc: 'Exclusive NOR gate (2 inputs, 1 output)' },
					{ name: 'DFF', desc: 'D Flip-Flop (1 data + 1 clock input, 1 output)' }
				];

				gateTypes.forEach(gate => {
					suggestions.push({
						label: gate.name + '()',
						kind: CompletionItemKind.Function,
						insertText: gate.name + '()',
						documentation: gate.desc
					});
				});

				suggestions.push(
					{
						label: 'Circuit',
						kind: CompletionItemKind.Function,
						insertText: 'Circuit("${1:CircuitName}")',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Reference another circuit as a subcircuit'
					}
				);

				// Add available circuit names for Circuit() completion
				if (circuitInfos) {
					circuitInfos.forEach(circuit => {
						suggestions.push({
							label: `Circuit("${circuit.Name}")`,
							kind: CompletionItemKind.Function,
							insertText: `Circuit("${circuit.Name}")`,
							documentation: `Reference circuit ${circuit.Name} (Level ${circuit.Level})`
						});
					});
				}

				suggestions.push(
					{
						label: 'LookupTable',
						kind: CompletionItemKind.Function,
						insertText: 'LookupTable("${1:table_name}")',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Use a custom lookup table'
					}
				);
			}

			// Inside Circuit("...") - suggest circuit names
			if (/Circuit\("[^"]*$/.test(lineUpToCursor)) {
				if (circuitInfos) {
					circuitInfos.forEach(circuit => {
						suggestions.push({
							label: circuit.Name,
							kind: CompletionItemKind.Function,
							insertText: circuit.Name,
							documentation: `Circuit ${circuit.Name} (Level ${circuit.Level})`
						});
					});
				}
			}

			// Inside connections block - suggest connection syntax
			if (/connections\s*\{[^}]*$/.test(text.substring(0, document.offsetAt(position)))) {
				suggestions.push(
					{
						label: '-> connection',
						kind: CompletionItemKind.Snippet,
						insertText: '${1:source} -> ${2:target}.in[${3:0}]',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Connect source to gate input'
					},
					{
						label: '-> output',
						kind: CompletionItemKind.Snippet,
						insertText: '${1:gate}.out -> ${2:output_name}',
						insertTextFormat: InsertTextFormat.Snippet,
						documentation: 'Connect gate output to circuit output'
					}
				);
			}

			// Add common patterns
			if (lineUpToCursor.includes('.')) {
				// Check if it's gate.port completion
				const dotIndex = lineUpToCursor.lastIndexOf('.');
				const beforeDot = lineUpToCursor.substring(0, dotIndex).trim();
				const gateName = beforeDot.split(/\s+/).pop();

				if (gateName) {
					// Simple gate type detection (can be enhanced with parsing)
					const gateType = getGateType(text, gateName);
					const ports = getPortsForGate(gateType);

					ports.forEach((port: string) => {
						suggestions.push({
							label: port,
							kind: CompletionItemKind.Property,
							insertText: port,
							documentation: `Port for ${gateType} gate`
						});
					});

					// Fallback to generic suggestions if no specific ports
					if (ports.length === 0) {
						suggestions.push(
							{
								label: 'in[0]',
								kind: CompletionItemKind.Property,
								insertText: 'in[${1:0}]',
								insertTextFormat: InsertTextFormat.Snippet,
								documentation: 'Gate input pin'
							},
							{
								label: 'out',
								kind: CompletionItemKind.Property,
								insertText: 'out',
								documentation: 'Gate output pin'
							}
						);
					}
				} else {
					// Fallback for non-gate completions
					suggestions.push(
						{
							label: 'in[0]',
							kind: CompletionItemKind.Property,
							insertText: 'in[${1:0}]',
							insertTextFormat: InsertTextFormat.Snippet,
							documentation: 'Gate input pin'
						},
						{
							label: 'out',
							kind: CompletionItemKind.Property,
							insertText: 'out',
							documentation: 'Gate output pin'
						}
					);
				}
			}

			return suggestions;
		}
	);

	// This handler resolves additional information for the item selected in
	// the completion list.
	connection.onCompletionResolve(
		(item: CompletionItem): CompletionItem => {
			// Most documentation is already set in onCompletion
			// This can be used for lazy loading of expensive completion details
			return item;
		}
	);
}