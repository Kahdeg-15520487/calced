import { gateInfo } from './gateInfo';

import {
	createConnection,
	TextDocuments,
	Diagnostic,
	DiagnosticSeverity,
	ProposedFeatures,
	InitializeParams,
	DidChangeConfigurationNotification,
	CompletionItem,
	CompletionItemKind,
	TextDocumentPositionParams,
	TextDocumentSyncKind,
	InitializeResult
} from 'vscode-languageserver/node';

import {
	TextDocument
} from 'vscode-languageserver-textdocument';

import { execFile, execFileSync } from 'child_process';
import * as crypto from 'crypto';
import * as path from 'path';
import * as fs from 'fs';
import * as url from 'url';

// Circuit information for hover tooltips
interface GateInfo {
	Type: string;
	DefinitionLine: number;
}

interface CircuitInfo {
	Name: string;
	Inputs: string[];
	Outputs: string[];
	FilePath: string;
	DefinitionLine: number;
	Gates: { [name: string]: GateInfo };
}

// Create a connection for the server, using Node's IPC as a transport.
// Also include all preview / proposed LSP features.
const connection = createConnection(ProposedFeatures.all);

// Create a simple text document manager.
const documents: TextDocuments<TextDocument> = new TextDocuments(TextDocument);

// Map to store circuit definitions for hover tooltips
const circuitDefinitions: Map<string, any> = new Map();

// Cache for getCircuitInfo results to avoid redundant calls
interface CircuitInfoCacheEntry {
	contentHash: string;
	basePath: string;
	result: CircuitInfo[] | null;
	timestamp: number;
}
const circuitInfoCache: Map<string, CircuitInfoCacheEntry> = new Map();
const MAX_CACHE_SIZE = 50; // Limit cache size to prevent memory leaks

let hasConfigurationCapability = false;
let hasWorkspaceFolderCapability = false;
let hasDiagnosticRelatedInformationCapability = false;

connection.onInitialize((params: InitializeParams) => {
	connection.console.log('Circuit Language Server: Initializing...');
	const capabilities = params.capabilities;

	// Does the client support the `workspace/configuration` request?
	// If not, we fall back using global settings.
	hasConfigurationCapability = !!(
		capabilities.workspace && !!capabilities.workspace.configuration
	);
	hasWorkspaceFolderCapability = !!(
		capabilities.workspace && !!capabilities.workspace.workspaceFolders
	);
	hasDiagnosticRelatedInformationCapability = !!(
		capabilities.textDocument &&
		capabilities.textDocument.publishDiagnostics &&
		capabilities.textDocument.publishDiagnostics.relatedInformation
	);

	const result: InitializeResult = {
		capabilities: {
			textDocumentSync: TextDocumentSyncKind.Incremental,
			// Tell the client that this server supports code completion.
			completionProvider: {
				resolveProvider: true,
				triggerCharacters: ['.', '-', '>', '=']
			},
			// Enable hover support
			hoverProvider: true,
			// Enable definition support
			definitionProvider: true
		}
	};
	if (hasWorkspaceFolderCapability) {
		result.capabilities.workspace = {
			workspaceFolders: {
				supported: true
			}
		};
	}
	return result;
});

connection.onInitialized(() => {
	connection.console.log('Circuit Language Server: Initialized successfully!');
	if (hasConfigurationCapability) {
		// Register for all configuration changes.
		connection.client.register(DidChangeConfigurationNotification.type, undefined);
	}
});

// The example settings
interface ExampleSettings {
	maxNumberOfProblems: number;
}

// The global settings, used when the `workspace/configuration` request is not supported by the client.
// Please note that this is not the case when using this server with the client provided in this example
// but could happen with other clients.
const defaultSettings: ExampleSettings = { maxNumberOfProblems: 1000 };
let globalSettings: ExampleSettings = defaultSettings;

// Cache the settings of all open documents
const documentSettings: Map<string, Thenable<ExampleSettings>> = new Map();

connection.onDidChangeConfiguration((change: any) => {
	if (hasConfigurationCapability) {
		// Reset all cached document settings
		documentSettings.clear();
	} else {
		globalSettings = <ExampleSettings>(
			(change.settings.languageServerExample || defaultSettings)
		);
	}

	// Revalidate all open text documents
	documents.all().forEach(validateTextDocument);
});

function getDocumentSettings(resource: string): Thenable<ExampleSettings> {
	if (!hasConfigurationCapability) {
		return Promise.resolve(globalSettings);
	}
	let result = documentSettings.get(resource);
	if (!result) {
		result = connection.workspace.getConfiguration({
			scopeUri: resource,
			section: 'languageServerExample'
		});
		documentSettings.set(resource, result);
	}
	return result;
}

// Only keep settings for open documents
documents.onDidClose((e: any) => {
	documentSettings.delete(e.document.uri);
	// Clear circuit info cache for this document
	const documentUri = e.document.uri;
	for (const [key, entry] of circuitInfoCache.entries()) {
		if (key.includes(documentUri) || key.includes(url.fileURLToPath(documentUri))) {
			circuitInfoCache.delete(key);
		}
	}
});

// The content of a text document has changed. This event is emitted
// when the text document first opened or when its content has changed.
documents.onDidChangeContent((change: any) => {
	// Update circuit info cache with new content instead of clearing
	const documentUri = change.document.uri;
	const documentPath = url.fileURLToPath(documentUri);
	const documentDir = path.dirname(documentPath);
	const newContent = change.document.getText();

	// Pre-compute and cache the circuit info for the updated document
	getCircuitInfo(newContent, documentDir);

	validateTextDocument(change.document);
});

async function validateTextDocument(textDocument: TextDocument): Promise<void> {
	connection.console.log(`Circuit Language Server: Validating document ${textDocument.uri}`);

	// Save the document to a temporary file for CircuitSimulator to parse
	const tempDir = path.join(__dirname, '..', '..', 'temp');
	if (!fs.existsSync(tempDir)) {
		fs.mkdirSync(tempDir, { recursive: true });
	}

	const tempFile = path.join(tempDir, `temp_${Date.now()}.circuit`);
	fs.writeFileSync(tempFile, textDocument.getText());

	const diagnostics: Diagnostic[] = [];

	try {
		// Path to the bundled CircuitSimulator.exe
		const simulatorPath = path.join(__dirname, '..', '..', 'bin', 'CircuitSimulator.exe');

		// Get the directory of the original document for import resolution
		const documentDir = path.dirname(url.fileURLToPath(textDocument.uri));

		await new Promise<void>((resolve, reject) => {
			execFile(simulatorPath, [tempFile, '--verify', `--base-path=${documentDir}`], (error, stdout, stderr) => {
				try {
					if (stdout) {
						const parsedDiagnostics = JSON.parse(stdout);
						for (const diag of parsedDiagnostics) {
							diagnostics.push({
								severity: diag.severity === 'error' ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
								range: {
									start: { line: diag.Line, character: diag.Column },
									end: { line: diag.Line, character: diag.Column + diag.Length }
								},
								message: diag.Message,
								source: 'circuit'
							});
						}
					}
				} catch (parseError) {
					// If JSON parsing fails, create a diagnostic for the parse error
					diagnostics.push({
						severity: DiagnosticSeverity.Error,
						range: {
							start: { line: 0, character: 0 },
							end: { line: 0, character: 1 }
						},
						message: `Parser error: ${stderr || error?.message || 'Unknown error'}`,
						source: 'circuit'
					});
				}
				resolve();
			});
		});
	} catch (err) {
		diagnostics.push({
			severity: DiagnosticSeverity.Error,
			range: {
				start: { line: 0, character: 0 },
				end: { line: 0, character: 1 }
			},
			message: `Failed to run CircuitSimulator: ${err}`,
			source: 'circuit'
		});
	} finally {
		// Clean up temp file
		if (fs.existsSync(tempFile)) {
			fs.unlinkSync(tempFile);
		}
	}

	// Send the computed diagnostics to VS Code.
	connection.sendDiagnostics({ uri: textDocument.uri, diagnostics });
}

connection.onDidChangeWatchedFiles((_change: any) => {
	// Monitored files have change in VS Code
	connection.console.log('We received a file change event');
});

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

		// Context-aware suggestions
		const suggestions: CompletionItem[] = [];

		// If we're at the start of a line or after whitespace, suggest top-level keywords
		if (lineUpToCursor.trim() === '' || /^\s*$/.test(lineUpToCursor)) {
			suggestions.push(
				{
					label: 'circuit',
					kind: CompletionItemKind.Snippet,
					insertText: 'circuit ${1:CircuitName} {\n\tinputs { ${2:input1} }\n\toutputs { ${3:output1} }\n\tgates {\n\t\t${4:gate1} = ${5:AND}()\n\t}\n\tconnections {\n\t\t${6:input1} -> ${7:gate1}.in[0]\n\t\t${8:gate1}.out -> ${9:output1}\n\t}\n}',
					documentation: 'Create a new circuit with basic structure'
				},
				{
					label: 'import',
					kind: CompletionItemKind.Snippet,
					insertText: 'import "${1:filename.circuit}"',
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
					documentation: 'Define circuit inputs'
				},
				{
					label: 'outputs',
					kind: CompletionItemKind.Snippet,
					insertText: 'outputs { ${1:output_name} }',
					documentation: 'Define circuit outputs'
				},
				{
					label: 'lookup_tables',
					kind: CompletionItemKind.Snippet,
					insertText: 'lookup_tables {\n\t${1:table_name} = {\n\t\t${2:00} -> ${3:0}\n\t\t${4:01} -> ${5:1}\n\t}\n}',
					documentation: 'Define lookup tables for custom logic'
				},
				{
					label: 'gates',
					kind: CompletionItemKind.Snippet,
					insertText: 'gates {\n\t${1:gate_name} = ${2:AND}()\n}',
					documentation: 'Define gates and subcircuits'
				},
				{
					label: 'connections',
					kind: CompletionItemKind.Snippet,
					insertText: 'connections {\n\t${1:source} -> ${2:target}\n}',
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
					documentation: 'Reference another circuit as a subcircuit'
				},
				{
					label: 'LookupTable',
					kind: CompletionItemKind.Function,
					insertText: 'LookupTable("${1:table_name}")',
					documentation: 'Use a custom lookup table'
				}
			);
		}

		// Inside connections block - suggest connection syntax
		if (/connections\s*\{[^}]*$/.test(text.substring(0, document.offsetAt(position)))) {
			suggestions.push(
				{
					label: '-> connection',
					kind: CompletionItemKind.Snippet,
					insertText: '${1:source} -> ${2:target}.in[${3:0}]',
					documentation: 'Connect source to gate input'
				},
				{
					label: '-> output',
					kind: CompletionItemKind.Snippet,
					insertText: '${1:gate}.out -> ${2:output_name}',
					documentation: 'Connect gate output to circuit output'
				}
			);
		}

		// Add common patterns
		if (lineUpToCursor.includes('.')) {
			suggestions.push(
				{
					label: 'in[0]',
					kind: CompletionItemKind.Property,
					insertText: 'in[${1:0}]',
					documentation: 'Gate input pin'
				},
				{
					label: 'out',
					kind: CompletionItemKind.Property,
					insertText: 'out',
					documentation: 'Gate output pin'
				},
				{
					label: 'out[0]',
					kind: CompletionItemKind.Property,
					insertText: 'out[${1:0}]',
					documentation: 'Indexed gate output pin'
				}
			);
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

// Helper function to get circuit information using CircuitSimulator
function getCircuitInfo(content: string, basePath: string): CircuitInfo[] | null {
	// Create a cache key based on content hash and basePath
	const contentHash = crypto.createHash('md5').update(content).digest('hex');
	const cacheKey = `${basePath}:${contentHash}`;

	// Check if we have a cached result
	const cached = circuitInfoCache.get(cacheKey);
	if (cached && cached.basePath === basePath) {
		return cached.result;
	}

	try {
		const simulatorPath = path.join(__dirname, '..', '..', 'bin', 'CircuitSimulator.exe');
		const tempDir = path.join(__dirname, '..', '..', 'temp');
		if (!fs.existsSync(tempDir)) {
			fs.mkdirSync(tempDir, { recursive: true });
		}
		const tempFile = path.join(tempDir, `info_${Date.now()}.circuit`);
		fs.writeFileSync(tempFile, content);
		const stdout = execFileSync(simulatorPath, [tempFile, '--info', `--base-path=${basePath}`], { encoding: 'utf8' });
		const result = JSON.parse(stdout);

		// Cache the result
		circuitInfoCache.set(cacheKey, {
			contentHash,
			basePath,
			result,
			timestamp: Date.now()
		});

		// Evict oldest entries if cache is too large
		if (circuitInfoCache.size > MAX_CACHE_SIZE) {
			let oldestKey: string | null = null;
			let oldestTime = Date.now();
			for (const [key, entry] of circuitInfoCache.entries()) {
				if (entry.timestamp < oldestTime) {
					oldestTime = entry.timestamp;
					oldestKey = key;
				}
			}
			if (oldestKey) {
				circuitInfoCache.delete(oldestKey);
			}
		}

		fs.unlinkSync(tempFile);
		return result;
	} catch (error) {
		// Cache the null result to avoid repeated failures
		circuitInfoCache.set(cacheKey, {
			contentHash,
			basePath,
			result: null,
			timestamp: Date.now()
		});
		return null;
	}
}

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

		// Provide hover information for different symbols
		const info = gateInfo[word];
		if (info) {
			return {
				contents: {
					kind: 'markdown',
					value: info
				}
			};
		}

		// Check if hovering over a circuit name in Circuit() calls
		// First check if it's already in our cached definitions
		let circuitInfoDup = circuitDefinitions.get(word);
		if (circuitInfoDup) {
			const inputsStr = circuitInfoDup!.inputs.join(', ');
			const outputsStr = circuitInfoDup!.outputs.join(', ');
			const hoverText = `**Circuit: ${circuitInfoDup!.name}**\n\n` +
				`**Inputs:** ${inputsStr || 'none'}\n\n` +
				`**Outputs:** ${outputsStr || 'none'}\n\n` +
				`*Defined in: ${path.basename(circuitInfoDup!.filePath)}*`;
			return {
				contents: {
					kind: 'markdown',
					value: hoverText
				}
			};
		}		
		
		// If not found, try to get circuit info by calling C# program
		const documentPath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
		const documentDir = path.dirname(documentPath);
		const circuitInfosHover = getCircuitInfo(text, documentDir);
		if (circuitInfosHover) {
			const foundCircuit = circuitInfosHover.find((info) => info.Name === word);
			if (foundCircuit) {
				const inputsStr = foundCircuit.Inputs.join(', ');
				const outputsStr = foundCircuit.Outputs.join(', ');
				const hoverText = `**Circuit: ${foundCircuit.Name}**\n\n` +
					`**Inputs:** ${inputsStr || 'none'}\n\n` +
					`**Outputs:** ${outputsStr || 'none'}\n\n` +
					`*Defined in: ${path.basename(foundCircuit.FilePath)}*`;

				// Cache the result
				circuitDefinitions.set(word, {
					name: foundCircuit.Name,
					inputs: foundCircuit.Inputs,
					outputs: foundCircuit.Outputs,
					filePath: foundCircuit.FilePath,
					definitionLine: foundCircuit.DefinitionLine,
					gates: foundCircuit.Gates
				});
				return {
					contents: {
						kind: 'markdown',
						value: hoverText
					}
				};
			}
		}

		// Check if it's a gate instance
		const circuitInfosGate = getCircuitInfo(text, documentDir);
		if (circuitInfosGate) {
			for (const circuitInfo of circuitInfosGate) {
				if (circuitInfo.Gates && circuitInfo.Gates[word]) {
					const gateType = circuitInfo.Gates[word].Type;
					if (gateType.startsWith('Circuit:')) {
						const circuitName = gateType.substring('Circuit:'.length);
						// Show circuit tooltip
						let targetCircuit = circuitDefinitions.get(circuitName);
						if (!targetCircuit) {
							targetCircuit = circuitInfosGate.find((c) => c.Name === circuitName);
						}
						if (targetCircuit) {
							const inputsStr = targetCircuit.Inputs?.join(', ') || '';
							const outputsStr = targetCircuit.Outputs?.join(', ') || '';
							const hoverText = `**Circuit: ${targetCircuit.Name}**\n\n` +
								`**Inputs:** ${inputsStr || 'none'}\n\n` +
								`**Outputs:** ${outputsStr || 'none'}\n\n` +
								`*Defined in: ${path.basename(targetCircuit.FilePath || '')}*`;
							return {
								contents: {
									kind: 'markdown',
									value: hoverText
								}
							};
						} else {
							return {
								contents: {
									kind: 'plaintext',
									value: `Circuit not found: ${circuitName}`
								}
							};
						}
					} else {
						// Built-in gate
						const gateTooltip = gateInfo[gateType];
						if (gateTooltip) {
							return {
								contents: {
									kind: 'markdown',
									value: gateTooltip
								}
							};
						} else {
							return {
								contents: {
									kind: 'plaintext',
									value: `Unknown gate type: ${gateType}`
								}
							};
						}
					}
				}
			}
		}

		return null;
	}
);

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

		// Check if this is a connection reference like gateName.in.portName or gateName.out.portName
		const connectionRef = parseConnectionReference(text, offset);
		console.log(`Parsed connection reference: ${JSON.stringify(connectionRef)} from word "${word}"`);
		if (connectionRef) {
			if (connectionRef.target === 'gate') {
				// Navigate to the gate definition in the current circuit
				const documentPathGate = url.fileURLToPath(textDocumentPosition.textDocument.uri);
				const documentDirGate = path.dirname(documentPathGate);
				const circuitInfosGate = getCircuitInfo(text, documentDirGate);
				if (circuitInfosGate) {
					// Find the gate definition in any circuit
					for (const circuitInfo of circuitInfosGate) {
						if (circuitInfo.Gates && circuitInfo.Gates[connectionRef.gateName]) {
							const gateInfo = circuitInfo.Gates[connectionRef.gateName];
							const definitionLine = gateInfo.DefinitionLine - 1;

							let targetFilePath = circuitInfo.FilePath;
							if (circuitInfo.FilePath === 'temp') { // Since we don't create temp file anymore
								targetFilePath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
							}

							try {
								const fileContent = fs.readFileSync(targetFilePath, 'utf8');
								const lines = fileContent.split('\n');
								const lineContent = lines[definitionLine];
								if (lineContent) {
									const gateRange = parseGateDeclaration(lineContent, connectionRef.gateName);
									if (gateRange) {
										console.log(`Returning range for gate ${connectionRef.gateName}: uri=${url.pathToFileURL(targetFilePath).toString()}, line=${definitionLine}, chars=${gateRange.start}-${gateRange.end}`);
										return {
											uri: url.pathToFileURL(targetFilePath).toString(),
											range: {
												start: { line: definitionLine, character: gateRange.start },
												end: { line: definitionLine, character: gateRange.end }
											}
										};
									}
								}
							} catch (error) {
								// Fall back to hardcoded range if file reading fails
							}

							// Fallback to hardcoded range
							console.log(`Returning fallback range for gate ${connectionRef.gateName}: uri=${url.pathToFileURL(targetFilePath).toString()}, line=${definitionLine}`);
							return {
								uri: url.pathToFileURL(targetFilePath).toString(),
								range: {
									start: { line: definitionLine, character: 0 },
									end: { line: definitionLine, character: connectionRef.gateName.length }
								}
							};
						}
					}
				}
			} else if (connectionRef.target === 'port') {
				// Navigate to the port in the referenced circuit (existing logic)
				const documentPathPort = url.fileURLToPath(textDocumentPosition.textDocument.uri);
				const documentDirPort = path.dirname(documentPathPort);
				const circuitInfosPort = getCircuitInfo(text, documentDirPort);
				if (circuitInfosPort) {
					// Find the gate definition
					let targetCircuit: CircuitInfo | undefined;
					let gateType: string | undefined;

					for (const circuitInfo of circuitInfosPort) {
						if (circuitInfo.Gates && circuitInfo.Gates[connectionRef.gateName]) {
							gateType = circuitInfo.Gates[connectionRef.gateName].Type;
							break;
						}
					}

					if (gateType && gateType.startsWith('Circuit:')) {
						const referencedCircuitName = gateType.substring('Circuit:'.length);

						// Find the referenced circuit
						let targetCircuit = circuitInfosPort.find(c => c.Name === referencedCircuitName);

						// Fallback to circuitDefinitions if not found in circuitInfos
						if (!targetCircuit) {
							const circuit = circuitDefinitions.get(referencedCircuitName);
							if (circuit) {
								targetCircuit = {
									Name: circuit.name,
									Inputs: circuit.inputs,
									Outputs: circuit.outputs,
									FilePath: circuit.filePath,
									DefinitionLine: circuit.definitionLine,
									Gates: circuit.gates
								};
							}
						}

						if (targetCircuit) {
							let targetFilePath = targetCircuit.FilePath;
							if (targetCircuit.FilePath === 'temp') { // Since we don't create temp file anymore
								targetFilePath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
							}

							// Read the target circuit file
							const fileContent = fs.readFileSync(targetFilePath, 'utf8');
							const lines = fileContent.split('\n');

							// Find the inputs or outputs block
							const blockType = connectionRef.direction === 'in' ? 'inputs' : 'outputs';
							let blockStartLine = -1;
							let blockEndLine = -1;

							for (let i = 0; i < lines.length; i++) {
								const line = lines[i];
								if (line.includes(`${blockType} {`)) {
									blockStartLine = i;
									// Find the closing brace
									let braceCount = 0;
									for (let j = i; j < lines.length; j++) {
										const innerLine = lines[j];
										braceCount += (innerLine.match(/\{/g) || []).length;
										braceCount -= (innerLine.match(/\}/g) || []).length;
										if (braceCount === 0) {
											blockEndLine = j;
											break;
										}
									}
									break;
								}
							}

							if (blockStartLine !== -1) {
								// Try to find the specific port if portName is provided
								if (connectionRef.portName) {
									const blockContent = lines.slice(blockStartLine, blockEndLine + 1).join('\n');
									const portPos = findPortInBlock(blockContent, connectionRef.portName);
									console.log(`Searching for port "${connectionRef.portName}" (length: ${connectionRef.portName.length}) in ${blockType} block from line ${blockStartLine} to ${blockEndLine} in file ${targetFilePath}`);

									if (portPos) {
										// Calculate the absolute position in the block content
										const portLineIndex = blockContent.substring(0, portPos.start).split('\n').length - 1;
										const portLine = lines[blockStartLine + portLineIndex];
										let portInLinePos = findPortInBlock(portLine, connectionRef.portName);
										
										// Fallback for single-line blocks: use portPos directly if portInLinePos is null
										if (!portInLinePos && blockStartLine === blockEndLine) {
											portInLinePos = portPos;
										}
										
										console.log(`Port ${connectionRef.portName} found in line: ${portLine} at positions ${portInLinePos?.start}-${portInLinePos?.end}`);

										if (portInLinePos) {
											const uri = url.pathToFileURL(targetFilePath).toString();
											const range = {
												start: { line: blockStartLine + portLineIndex, character: portInLinePos.start },
												end: { line: blockStartLine + portLineIndex, character: portInLinePos.end }
											};
											console.log(`Returning range for port ${connectionRef.portName}: uri=${uri}, line=${range.start.line}, chars=${range.start.character}-${range.end.character}`);
											return {
												uri,
												range
											};
										}
									}
								}

								// Fallback: go to the block type name
								console.log("Fallback to block name");
								const lineContent = lines[blockStartLine];
								const blockStartChar = lineContent.indexOf(blockType);
								const uri = url.pathToFileURL(targetFilePath).toString();
								const range = {
									start: { line: blockStartLine, character: blockStartChar },
									end: { line: blockStartLine, character: blockStartChar + blockType.length }
								};
								console.log(`Returning range for ${blockType} block: uri=${uri}, line=${range.start.line}, chars=${range.start.character}-${range.end.character}`);
								return {
									uri,
									range
								};
							}
						}
					}
				}
			}
		}

		// Check if it's a circuit name that we can find the definition for
		const documentPathCircuit = url.fileURLToPath(textDocumentPosition.textDocument.uri);
		const documentDirCircuit = path.dirname(documentPathCircuit);
		const circuitInfosCircuit = getCircuitInfo(text, documentDirCircuit);
		if (circuitInfosCircuit) {
			const foundCircuit = circuitInfosCircuit.find((info) => info.Name === word);
			if (foundCircuit) {
				// Convert the file path to a URI
				let targetFilePath = foundCircuit.FilePath;
				if (foundCircuit.FilePath === 'temp') { // Since we don't create temp file anymore
					targetFilePath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
				}
				const definitionUri = url.pathToFileURL(targetFilePath).toString();

				// Use the exact definition line (convert from 1-based to 0-based for LSP)
				const definitionLine = foundCircuit.DefinitionLine - 1;

				// Read the file to get the exact line content for dynamic range calculation
				try {
					const fileContent = fs.readFileSync(targetFilePath, 'utf8');
					const lines = fileContent.split('\n');
					const lineContent = lines[definitionLine];
					if (lineContent) {
						const circuitRange = parseCircuitDeclaration(lineContent, foundCircuit.Name);
						if (circuitRange) {
							return {
								uri: definitionUri,
								range: {
									start: { line: definitionLine, character: circuitRange.start },
									end: { line: definitionLine, character: circuitRange.end }
								}
							};
						}
					}
				} catch (error) {
					// Fall back to hardcoded range if file reading fails
				}

				// Fallback to hardcoded range
				return {
					uri: definitionUri,
					range: {
						start: { line: definitionLine, character: 8 },
						end: { line: definitionLine, character: 50 }
					}
				};
			}

			// Check if it's a gate in any circuit
			for (const info of circuitInfosCircuit) {
				if (info.Gates && info.Gates[word]) {
					const definitionLine = info.Gates[word].DefinitionLine - 1;
					// Read the file to get the exact line content
					try {
						// For gates in the current file, use the original document path instead of the temp file path
						let targetFilePath = info.FilePath;
						if (info.FilePath === 'temp') { // Since we don't create temp file anymore
							targetFilePath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
						}
						const fileContent = fs.readFileSync(targetFilePath, 'utf8');
						const lines = fileContent.split('\n');
						const lineContent = lines[definitionLine];
						if (lineContent) {
							const gateRange = parseGateDeclaration(lineContent, word);
							if (gateRange) {
								return {
									uri: url.pathToFileURL(targetFilePath).toString(),
									range: {
										start: { line: definitionLine, character: gateRange.start },
										end: { line: definitionLine, character: gateRange.end }
									}
								};
							}
						}
					} catch (error) {
						// Fall back to hardcoded range if file reading fails
					}
					// Fallback to hardcoded range
					return {
						uri: textDocumentPosition.textDocument.uri,
						range: {
							start: { line: definitionLine, character: 0 },
							end: { line: definitionLine, character: word.length }
						}
					};
				}
			}
		}

		return null;
	}
);

function getWordRangeAtPosition(text: string, offset: number): { start: number; end: number } | null {
	const wordRegex = /[a-zA-Z_][a-zA-Z0-9_]*/g;
	let match;

	while ((match = wordRegex.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			return {
				start: match.index,
				end: match.index + match[0].length
			};
		}
	}

	return null;
}

// Helper function to parse gate declaration line and find gate name position
function parseGateDeclaration(line: string, gateName: string): { start: number; end: number } | null {
	// Look for pattern: gateName = ...
	const gatePattern = new RegExp(`^(\\s*)${gateName}\\s*=`, 'm');
	const match = gatePattern.exec(line);
	if (match) {
		const startPos = match[1].length; // Length of leading whitespace
		const endPos = startPos + gateName.length;
		return { start: startPos, end: endPos };
	}
	return null;
}

// Helper function to parse circuit declaration line and find circuit name position
function parseCircuitDeclaration(line: string, circuitName: string): { start: number; end: number } | null {
	// Look for pattern: circuit CircuitName {
	const circuitPattern = /^(\s*)circuit\s+(\w+)\s*\{/;
	const match = circuitPattern.exec(line);
	if (match && match[2] === circuitName) {
		const startPos = match[1].length + 'circuit '.length; // After "circuit "
		const endPos = startPos + circuitName.length;
		return { start: startPos, end: endPos };
	}
	return null;
}

// Helper function to parse connection references like gateName.in.portName
function parseConnectionReference(text: string, offset: number): { target: 'gate' | 'port'; gateName: string; direction?: 'in' | 'out'; portName?: string } | null {
	// Find all connection references that contain the cursor position
	const allMatches: Array<{match: RegExpExecArray, direction: 'in' | 'out', hasPort: boolean}> = [];

	const inPattern = /(\w+)\.in\.(\w+)/g;
	const outPattern = /(\w+)\.out\.(\w+)/g;
	const inOnlyPattern = /(\w+)\.in\b/g;
	const outOnlyPattern = /(\w+)\.out\b/g;

	let match;

	// Find all matches that contain the cursor
	while ((match = inPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({match, direction: 'in', hasPort: true});
		}
	}

	while ((match = outPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({match, direction: 'out', hasPort: true});
		}
	}

	while ((match = inOnlyPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({match, direction: 'in', hasPort: false});
		}
	}

	while ((match = outOnlyPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({match, direction: 'out', hasPort: false});
		}
	}

	// Find the best match (prefer longer port names)
	let bestMatch: { target: 'gate' | 'port'; gateName: string; direction?: 'in' | 'out'; portName?: string } | null = null;

	for (const m of allMatches) {
		const portName = m.hasPort ? m.match[2] : undefined;
		const gateName = m.match[1];
		const direction = m.direction;
		const matchStr = m.match[0];
		const matchStart = m.match.index;

		// Determine which part the cursor is on
		let target: 'gate' | 'port' = 'port'; // default to port
		const gateEnd = matchStart + gateName.length;
		const portStart = matchStart + matchStr.lastIndexOf('.') + 1;

		if (offset <= gateEnd) {
			target = 'gate';
		} else if (portName && offset >= portStart) {
			target = 'port';
		}

		// Prefer matches where the target matches the cursor position
		if (!bestMatch || (portName && (!bestMatch.portName || portName.length > bestMatch.portName.length))) {
			bestMatch = { target, gateName, direction, portName };
		}
	}

	return bestMatch;
}

// Helper function to find port position in inputs/outputs block
function findPortInBlock(blockContent: string, portName: string): { start: number; end: number } | null {
	// Look for the port name in the block, handling spaces and commas
	const portPattern = new RegExp(`\\b${portName}\\b`, 'g');
	const match = portPattern.exec(blockContent);
	if (match) {
		return { start: match.index, end: match.index + portName.length };
	}
	return null;
}

// Make the text document manager listen on the connection
// for open, change and close text document events
documents.listen(connection);

// Listen on the connection
connection.listen();