import { TextDocumentPositionParams } from 'vscode-languageserver/node';

import { connection, documents } from './connection';
import { getCircuitInfo as getCircuitInfos } from './documents';
import { getWordRangeAtPosition, parseConnectionReference, getBlockInfoAtPosition, findCircuitInfoForGateType } from './utils';
import { AnnotationInfo } from './annotationInfo';
import * as url from 'url';
import * as path from 'path';

export function initializeDefinitionHandler(): void {
	// Definition support for go to definition
	connection.onDefinition(
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

			// Get circuit info for the current document
			const documentPath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
			const documentDir = path.dirname(documentPath);
			const circuitInfos = getCircuitInfos(text, documentDir, documentPath);
			const currentCircuit = circuitInfos?.find(c => c.FilePath === documentPath);

			// Check in cached circuit's blocks first
			const blockInfo = getBlockInfoAtPosition(document, wordRange.start);

			switch (blockInfo?.blockName) {
				case 'connections':
					// Handle connections block - parse connection reference and go to definition
					const connectionRef = parseConnectionReference(word, text, offset);

					if (connectionRef) {
						switch (connectionRef.target) {
							case 'gate':
								// Go to gate definition
								// Check current circuit first
								const currentGate = currentCircuit?.Gates[word];
								if (currentGate) {
									const defLine = currentGate.DefinitionLine - 1;
									const defCol = currentGate.DefinitionColumn - 1;
									return {
										uri: textDocumentPosition.textDocument.uri,
										range: {
											start: { line: defLine, character: defCol },
											end: { line: defLine, character: defCol + word.length }
										}
									};
								}
								// Check other circuits
								if (circuitInfos) {
									for (const circuitInfo of circuitInfos) {
										if (circuitInfo.Gates && circuitInfo.Gates[word]) {
											const gateInfo = circuitInfo.Gates[word];
											const definitionLine = gateInfo.DefinitionLine - 1;
											const definitionColumn = gateInfo.DefinitionColumn - 1;
											return {
												uri: url.pathToFileURL(circuitInfo.FilePath).href,
												range: {
													start: { line: definitionLine, character: definitionColumn },
													end: { line: definitionLine, character: definitionColumn + word.length }
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
									// Check if the gate is builtin or a subcircuit
									const gateInfo = currentCircuit?.Gates[connectionRef.gateName!];
									const isSubcircuit = gateInfo?.Type.startsWith('Circuit');
									if (!isSubcircuit) {
										// Built-in gate - no port definitions to go to
										return null;
									} else {
										const gateType = (currentCircuit?.Gates[connectionRef.gateName!]?.Type)?.replace('Circuit:', '') || null;
										const refCircuitInfo = findCircuitInfoForGateType(gateType!, circuitInfos!);
										if (refCircuitInfo) {
											const portInfo = (connectionRef.direction === 'in' ? refCircuitInfo.Inputs : refCircuitInfo.Outputs).find(p => p.Name === portName);
											if (portInfo) {
												const defLine = portInfo.DefinitionLine - 1;
												const defCol = portInfo.DefinitionColumn - 1;
												return {
													uri: url.pathToFileURL(refCircuitInfo.FilePath).href,
													range: {
														start: { line: defLine, character: defCol },
														end: { line: defLine, character: defCol + portName.length }
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
									if (!isSubcircuit) {
										// Built-in gate - no block definitions to go to
										return null;
									} else {
										const gateType = (currentCircuit?.Gates[connectionRef.gateName!]?.Type)?.replace('Circuit:', '') || null;
										const refCircuitInfo = findCircuitInfoForGateType(gateType!, circuitInfos!);
										if (refCircuitInfo) {
											const blockInfo = connectionRef.direction === 'in' ? refCircuitInfo.Blocks?.['inputs'] : refCircuitInfo.Blocks?.['outputs'];
											if (blockInfo) {
												// Go to the start of the inputs/outputs block
												const defLine = blockInfo.StartLine - 1;
												const defCol = blockInfo.StartColumn - 1;
												return {
													uri: url.pathToFileURL(refCircuitInfo.FilePath).href,
													range: {
														start: { line: defLine, character: defCol },
														end: { line: defLine, character: defCol + connectionRef.direction!.length }
													}
												};
											}
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
							const defLine = portInCurrent.DefinitionLine - 1;
							const defCol = portInCurrent.DefinitionColumn - 1;
							return {
								uri: textDocumentPosition.textDocument.uri,
								range: {
									start: { line: defLine, character: defCol },
									end: { line: defLine, character: defCol + word.length }
								}
							};
						}
					}

					break;
				case 'gates':
					// Check if word is a builtin gate
					if (AnnotationInfo[word]) {
						return {
							uri: 'circuit-builtin:///' + word + '.circuit',
							range: {
								start: { line: 0, character: 0 },
								end: { line: 0, character: 0 }
							}
						};
					}

					// Check if word is a circuit name referenced in gates
					if (circuitInfos) {
						for (const circuitInfo of circuitInfos) {
							for (const gateName in circuitInfo.Gates) {
								const gateType = circuitInfo.Gates[gateName].Type;
								if (gateType === `Circuit:${word}`) {
									// Go to the circuit definition
									const targetCircuit = circuitInfos.find(c => c.Name === word);
									if (targetCircuit) {
										const defLine = targetCircuit.DefinitionLine - 1;
										const defCol = 0; // Circuit definitions don't have column info
										return {
											uri: url.pathToFileURL(targetCircuit.FilePath).href,
											range: {
												start: { line: defLine, character: defCol },
												end: { line: defLine, character: defCol + word.length }
											}
										};
									}
								}
							}
						}
					}

					// Check if word is a lookup table name referenced in gates
					if (circuitInfos) {
						for (const circuitInfo of circuitInfos) {
							for (const gateName in circuitInfo.Gates) {
								const gateType = circuitInfo.Gates[gateName].Type;
								if (gateType === `LookupTable:${word}`) {
									// Go find the lookup_tables block in current circuit
									const lutInfo = currentCircuit?.LookupTables[word];
									if (lutInfo) {
										return {
											uri: textDocumentPosition.textDocument.uri,
											range: {
												start: { line: lutInfo.DefinitionLine - 1, character: lutInfo.DefinitionColumn - 1 },
												end: { line: lutInfo.DefinitionLine - 1, character: lutInfo.DefinitionColumn - 1 + word.length }
											}
										};
									}
								}
							}
						}
					}
					break;
				default:
					// Handle default case
					if (currentLine.trim().startsWith('import')) {
						// Handle import statements
						const importMatch = currentLine.match(/import\s+"([^"]+)"/);
						if (importMatch) {
							const importPath = importMatch[1];
							const importedCircuitInfo = circuitInfos?.find(c => path.basename(c.FilePath) === `${importPath}.circuit`);
							if (importedCircuitInfo) {
								const defLine = importedCircuitInfo.DefinitionLine - 1;
								const defCol = "import \"".length; // Position after import "
								return {
									uri: url.pathToFileURL(importedCircuitInfo.FilePath).href,
									range: {
										start: { line: defLine, character: defCol },
										end: { line: defLine, character: defCol + importPath.length }
									}
								};
							}
						}
					}
					break;
			}

			return null;
		}
	);
}