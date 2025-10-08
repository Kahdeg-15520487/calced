import { TextDocumentPositionParams, MarkupKind, MarkupContent } from 'vscode-languageserver/node';

import { AnnotationInfo } from './annotationInfo';
import { connection, documents } from './connection';
import { getCircuitInfo } from './documents';
import { getWordRangeAtPosition, parseConnectionReference, getBlockInfoAtPosition, findCircuitInfoForGateType, findLookupTableInfo } from './utils';
import * as url from 'url';
import * as path from 'path';

export function initializeHoverHandler(): void {
	// Hover support for showing information about symbols
	connection.onHover(
		(textDocumentPosition: TextDocumentPositionParams) => {
			const document = documents.get(textDocumentPosition.textDocument.uri);
			if (!document) {
				return null;
			}

			const position = textDocumentPosition.position;
			const text = document.getText();
			const offset = document.offsetAt(position);

			// Find the word at cursor position
			const wordRange = getWordRangeAtPosition(text, offset);
			if (!wordRange) {
				return null;
			}

			const word = text.substring(wordRange.start, wordRange.end);
			const lines = text.split('\n');
			const lineIndex = document.positionAt(offset).line;
			const currentLine = lines[lineIndex];

			// Provide hover information for different symbols
			// Get circuit info for the current document
			const documentPath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
			const documentDir = path.dirname(documentPath);
			const circuitInfos = getCircuitInfo(text, documentDir, documentPath);
			const currentCircuit = circuitInfos?.find(c => c.FilePath === documentPath);

			// Check in cached circuit's blocks first
			const blockInfo = getBlockInfoAtPosition(document, wordRange.start);

			switch (blockInfo?.blockName) {
				case 'connections':
					if (word === 'connections') {
						return {
							contents: {
								kind: MarkupKind.Markdown,
								value: AnnotationInfo['connections']
							}
						};
					}
					// Handle connections block - parse connection reference and show info
					const connectionRef = parseConnectionReference(word, text, offset);
					if (connectionRef) {
						switch (connectionRef.target) {
							case 'gate':
								// Show gate info
								// Check current circuit first
								const currentGate = currentCircuit?.Gates[word];
								if (currentGate) {
									return {
										contents: {
											kind: MarkupKind.Markdown,
											value: `**Gate:** ${word}\n\nType: ${currentGate.Type}`
										}
									};
								}
								// Check other circuits
								if (circuitInfos) {
									for (const circuitInfo of circuitInfos) {
										if (circuitInfo.Gates && circuitInfo.Gates[word]) {
											const gateInfo = circuitInfo.Gates[word];
											return {
												contents: {
													kind: MarkupKind.Markdown,
													value: `**Gate:** ${circuitInfo.Name}.${word}\n\nType: ${gateInfo.Type}`
												}
											};
										}
									}
								}
								break;
							case 'port':
								{
									// Check if port is current circuit's ports
									const portName = connectionRef.portName!;
									const portInfo = currentCircuit?.Inputs.find(p => p.Name === portName) || currentCircuit?.Outputs.find(p => p.Name === portName);
									if (portInfo) {
										return {
											contents: {
												kind: MarkupKind.Markdown,
												value: `**Port:** ${portInfo.Name}\n\nBit Width: ${portInfo.BitWidth}`
											}
										};
									}
									// Check if the gate is builtin or a subcircuit
									const gateInfo = currentCircuit?.Gates[connectionRef.gateName!];
									const isSubcircuit = gateInfo?.Type.startsWith('Circuit');
									if (!isSubcircuit) {
										// Built-in gate - no port info available
										return {
											contents: {
												kind: MarkupKind.Markdown,
												value: `**Port:** ${connectionRef.gateName}.${portName}`
											}
										};
									} else {
										const gateType = (currentCircuit?.Gates[connectionRef.gateName!]?.Type)?.replace('Circuit:', '') || null;
										const refCircuitInfo = findCircuitInfoForGateType(gateType!, circuitInfos!);
										if (refCircuitInfo) {
											const portInfo = (connectionRef.direction === 'in' ? refCircuitInfo.Inputs : refCircuitInfo.Outputs).find((p: any) => p.Name === portName);
											if (portInfo) {
												return {
													contents: {
														kind: MarkupKind.Markdown,
														value: `**Port:** ${refCircuitInfo.Name}.${portInfo.Name}\n\nBit Width: ${portInfo.BitWidth}`
													}
												};
											}
										}
									}
								}
								break;
							case 'direction':
								{
									// Check if the gate is builtin or a subcircuit
									const gateInfo = currentCircuit?.Gates[connectionRef.gateName!];
									const isSubcircuit = gateInfo?.Type.startsWith('Circuit');
									const isLookupTable = gateInfo?.Type.startsWith('LookupTable');
									const isBuiltin = gateInfo && !isSubcircuit && !isLookupTable;
									if (isBuiltin) {
										// Built-in gate - no block info available
										return {
											contents: {
												kind: MarkupKind.Markdown,
												value: `**Block:** ${connectionRef.direction === 'in' ? 'input' : 'output'} of builtin gate ${connectionRef.gateName!}`
											}
										};
									} else if (isSubcircuit) {
										const gateType = (currentCircuit?.Gates[connectionRef.gateName!]?.Type)?.replace('Circuit:', '') || null;
										const refCircuitInfo = findCircuitInfoForGateType(gateType!, circuitInfos!);
										if (refCircuitInfo) {
											const blockInfo = connectionRef.direction === 'in' ? refCircuitInfo.Blocks?.['inputs'] : refCircuitInfo.Blocks?.['outputs'];
											if (blockInfo) {
												return {
													contents: {
														kind: MarkupKind.Markdown,
														value: `**Block:** ${connectionRef.direction === 'in' ? 'input' : 'output'} of circuit: ${refCircuitInfo.Name}`
													}
												};
											}
										}
									} else if (isLookupTable) {
										// Lookup table
										// Get lookup table info
										const refTableInfo = findLookupTableInfo(connectionRef.gateName!, circuitInfos!);
										if (refTableInfo) {
											return {
												contents: {
													kind: MarkupKind.Markdown,
													value: `**Block:** ${connectionRef.direction === 'in' ? 'input' : 'output'} of lookup table: ${connectionRef.gateName!}\n\nBit Width: ${connectionRef.direction === 'in' ? refTableInfo.InputWidth : refTableInfo.OutputWidth}`
												}
											};
										}
									}
								}
								break;
							default:
								console.log(`Unhandled connection ref target type: ${connectionRef.target}`);
								break;
						}
					} else {
						const portInCurrent = currentCircuit?.Inputs.find(p => p.Name === word) || currentCircuit?.Outputs.find(p => p.Name === word);
						if (portInCurrent) {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Port:** ${portInCurrent.Name}\n\nBit Width: ${portInCurrent.BitWidth}`
								}
							};
						}
					}
					break;
				case 'gates':
					{
						// Show keyword info for Circuit() and LookupTable()
						if (word === 'Circuit' || word === 'LookupTable') {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Keyword:** ${word}()\n\nReference another circuit or lookup table as a subcircuit.`
								}
							};
						}

						// Show builtin gate info
						if (AnnotationInfo[word]) {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Gate:** ${word}\n\n${AnnotationInfo[word]}`
								}
							};
						}

						// Show subcircuit info
						const gatesWithType = Object.values(currentCircuit?.Gates || {}).filter(g => g.Type.endsWith(word));
						if (gatesWithType.length > 0) {
							const gateInfo = gatesWithType[0];
							const isSubcircuit = gateInfo.Type.startsWith('Circuit');
							const isLookupTable = gateInfo.Type.startsWith('LookupTable');

							if (isSubcircuit) {
								const refCircuitInfo = findCircuitInfoForGateType(gateInfo.Type.replace('Circuit:', ''), circuitInfos!);
								if (refCircuitInfo) {
									const inputsStr = refCircuitInfo.Inputs.map((p: any) => p.BitWidth === 1 ? p.Name : `${p.Name} [${p.BitWidth}]`).join(', ');
									const outputsStr = refCircuitInfo.Outputs.map((p: any) => p.BitWidth === 1 ? p.Name : `${p.Name} [${p.BitWidth}]`).join(', ');
									return {
										contents: {
											kind: MarkupKind.Markdown,
											value: `**Subcircuit:** ${word}\n\n**Inputs:** ${inputsStr || 'none'}\n\n**Outputs:** ${outputsStr || 'none'}`
										}
									};
								} else {
									return {
										contents: {
											kind: MarkupKind.Markdown,
											value: `**Subcircuit:** ${word}\n\nCircuit definition not found.`
										}
									};
								}
							}

							if (isLookupTable) {
								const refTableInfo = findLookupTableInfo(word, circuitInfos!);
								if (refTableInfo) {
									const entries = Object.entries(refTableInfo.TruthTable);
									const tableRows = entries.map(([input, output]) => `| ${input} | ${output} |`).join('\n');
									const content: MarkupContent = {
										kind: MarkupKind.Markdown,
										value: `**Lookup Table:** ${word}\n\n**Input Width:** ${refTableInfo.InputWidth}\n\n**Output Width:** ${refTableInfo.OutputWidth}\n\n**Truth Table:**\n\`\`\`\n${tableRows}\n\`\`\``
									};
									return {
										contents: content
									};
								}
							} else {
								return {
									contents: {
										kind: MarkupKind.Markdown,
										value: `**Lookup Table:** ${word}\n\nLookup table definition not found.`
									}
								}
							}
						}
					}

					break;
				case 'inputs':
					{
						if (word === 'inputs') {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: AnnotationInfo['inputs']
								}
							};
						}
						// Show input port info
						const portInfo = currentCircuit?.Inputs.find(p => p.Name === word);
						if (portInfo) {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Input Port:** ${portInfo.Name}\n\nBit Width: ${portInfo.BitWidth}`
								}
							};
						}
					}
					break;
				case 'outputs':
					{
						if (word === 'outputs') {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: AnnotationInfo['outputs']
								}
							};
						}
						// Show output port info
						const portInfo = currentCircuit?.Outputs.find(p => p.Name === word);
						if (portInfo) {

							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Output Port:** ${portInfo.Name}\n\nBit Width: ${portInfo.BitWidth}`
								}
							};
						}
					}
					break;
				case 'lookup_tables':
					{
						// Show keyword lookup table info
						if (word === 'lookup_tables') {
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: AnnotationInfo['lookup_tables']
								}
							};
						}

						// Show lookup table definition info
						const tableInfo = currentCircuit?.LookupTables[word];
						if (tableInfo) {
							const entries = Object.entries(tableInfo.TruthTable);
							const tableRows = entries.map(([input, output]) => `| ${input} | ${output} |`).join('\n');
							return {
								contents: {
									kind: MarkupKind.Markdown,
									value: `**Lookup Table:** ${word}\n\n**Input Width:** ${tableInfo.InputWidth}\n\n**Output Width:** ${tableInfo.OutputWidth}\n\n**Truth Table:**\n\`\`\`\n${tableRows}\n\`\`\``
								}
							};
						}
					}
					break;
				default:
					// Not inside a known block - could be top-level keywords or unknown context
					console.log(`Hover: Unhandled or unknown block context with word ${word} at position: ${wordRange.start}-${wordRange.end}`);
					if (word === 'circuit') {
						return {
							contents: {
								kind: MarkupKind.Markdown,
								value: '**Keyword:** circuit\n\nDefines a new circuit block.'
							}
						};
					}
					if (word === 'import') {
						return {
							contents: {
								kind: MarkupKind.Markdown,
								value: '**Keyword:** import\n\nImports another circuit file.'
							}
						};
					}

					// Handle default case
					if (currentLine.trim().startsWith('import')) {
						// Handle import statements
						const importMatch = currentLine.match(/import\s+"([^"]+)"/);
						if (importMatch) {
							const importPath = importMatch[1];
							const importedCircuitInfo = circuitInfos?.find(c => path.basename(c.FilePath) === `${importPath}.circuit`);
							if (importedCircuitInfo) {
								const inputsStr = importedCircuitInfo.Inputs.map((p: any) => p.BitWidth === 1 ? p.Name : `${p.Name} [${p.BitWidth}]`).join(', ');
								const outputsStr = importedCircuitInfo.Outputs.map((p: any) => p.BitWidth === 1 ? p.Name : `${p.Name} [${p.BitWidth}]`).join(', ');
								return {
									contents: {
										kind: MarkupKind.Markdown,
										value: `**Import:** ${importPath}\n\n**Inputs:** ${inputsStr || 'none'}\n\n**Outputs:** ${outputsStr || 'none'}\n\nImports circuit defined in file: ${importedCircuitInfo.FilePath}`
									}
								};
							}
						}
					}
					break;
			}

			// No relevant hover info found
			return null;
		}
	);
}