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
	DefinitionColumn: number;
}

interface PortInfo {
	Name: string;
	BitWidth: number;
	DefinitionLine: number;
	DefinitionColumn: number;
}

interface BlockInfo {
	StartLine: number;
	EndLine: number;
}

interface CircuitInfo {
	Name: string;
	Inputs: PortInfo[];
	Outputs: PortInfo[];
	FilePath: string;
	DefinitionLine: number;
	Gates: { [name: string]: GateInfo };
	Blocks: { [name: string]: BlockInfo };
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
	originalFilePath?: string;
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
	getCircuitInfo(newContent, documentDir, documentPath);

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
function getCircuitInfo(content: string, basePath: string, originalFilePath?: string): CircuitInfo[] | null {
	// Create a cache key based on content hash, basePath, and originalFilePath
	const contentHash = crypto.createHash('md5').update(content).digest('hex');
	const cacheKey = `${basePath}:${contentHash}:${originalFilePath || ''}`;

	// Check if we have a cached result
	const cached = circuitInfoCache.get(cacheKey);
	if (cached && cached.basePath === basePath && cached.originalFilePath === originalFilePath) {
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
		const args = [tempFile, '--info', `--base-path=${basePath}`];
		if (originalFilePath) {
			args.push(`--original-file-path=${originalFilePath}`);
		}
		const stdout = execFileSync(simulatorPath, args, { encoding: 'utf8' });
		const result = JSON.parse(stdout);

		// Cache the result
		circuitInfoCache.set(cacheKey, {
			contentHash,
			basePath,
			originalFilePath,
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
			const inputsStr = circuitInfoDup!.Inputs.map((p: PortInfo) => p.Name).join(', ');
			const outputsStr = circuitInfoDup!.Outputs.map((p: PortInfo) => p.Name).join(', ');
			const hoverText = `**Circuit: ${circuitInfoDup!.Name}**\n\n` +
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
		const circuitInfosHover = getCircuitInfo(text, documentDir, documentPath);
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
		const circuitInfosGate = getCircuitInfo(text, documentDir, documentPath);
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

		// Get circuit info for the current document
		const documentPath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
		const documentDir = path.dirname(documentPath);
		const circuitInfos = getCircuitInfo(text, documentDir, documentPath);

		// Check in cached circuit's blocks first
		const blockInfo = getBlockInfoAtPosition(document, wordRange.start);
		if (blockInfo) {
			console.log(`Found block info for word |${word}|: ${JSON.stringify(blockInfo)}`);
		}

		switch (blockInfo?.blockName) {
			case 'inputs':
				// Handle input block - go to first connection where this input is used
				if (circuitInfos) {
					for (const circuitInfo of circuitInfos) {
						if (circuitInfo.Inputs.some(p => p.Name === word)) {
							const lines = text.split('\n');
							const connectionsBlock = circuitInfo.Blocks?.['connections'];
							if (connectionsBlock) {
								for (let i = connectionsBlock.StartLine - 1; i <= connectionsBlock.EndLine - 1; i++) {
									if (lines[i].includes(word + ' ->')) {
										const charIndex = lines[i].indexOf(word);
										return {
											uri: textDocumentPosition.textDocument.uri,
											range: {
												start: { line: i, character: charIndex },
												end: { line: i, character: charIndex + word.length }
											}
										};
									}
								}
							}
							break; // Found the circuit with this input
						}
					}
				}
				break;
			case 'outputs':
				// Handle output block - go to first connection where this output is used
				if (circuitInfos) {
					for (const circuitInfo of circuitInfos) {
						if (circuitInfo.Outputs.some(p => p.Name === word)) {
							const lines = text.split('\n');
							const connectionsBlock = circuitInfo.Blocks?.['connections'];
							if (connectionsBlock) {
								for (let i = connectionsBlock.StartLine - 1; i <= connectionsBlock.EndLine - 1; i++) {
									if (lines[i].includes('-> ' + word)) {
										const charIndex = lines[i].indexOf(word);
										return {
											uri: textDocumentPosition.textDocument.uri,
											range: {
												start: { line: i, character: charIndex },
												end: { line: i, character: charIndex + word.length }
											}
										};
									}
								}
							}
							break; // Found the circuit with this output
						}
					}
				}
				break;
			case 'connections':
				// Handle connections block - parse connection reference and go to definition
				const connectionRef = parseConnectionReference(word, text, offset);
				console.log(`Parsed connection reference for word |${word}|: ${JSON.stringify(connectionRef)}`);
				if (connectionRef && circuitInfos) {
					console.log(1);
					// First, try to find the gate in the current document's circuit
					const currentCircuit = circuitInfos.find(c => c.FilePath === url.fileURLToPath(textDocumentPosition.textDocument.uri));
					if (currentCircuit && connectionRef.target === 'gate' && currentCircuit.Gates && currentCircuit.Gates[connectionRef.gateName]) {
						const gateInfo = currentCircuit.Gates[connectionRef.gateName];
						const definitionLine = gateInfo.DefinitionLine - 1;
						const definitionColumn = gateInfo.DefinitionColumn - 1;
						console.log(`Navigating to gate ${connectionRef.gateName} definition line: ${definitionLine}, column: ${definitionColumn} in ${currentCircuit.FilePath}`);
						return {
							uri: textDocumentPosition.textDocument.uri,
							range: {
								start: { line: definitionLine, character: definitionColumn },
								end: { line: definitionLine, character: definitionColumn + word.length }
							}
						};
					}
					
					// If not found in current circuit, search all circuits
					for (const circuitInfo of circuitInfos) {
						console.log(2);
						if (connectionRef.target === 'gate' && circuitInfo.Gates && circuitInfo.Gates[connectionRef.gateName]) {
							const gateInfo = circuitInfo.Gates[connectionRef.gateName];
							const definitionLine = gateInfo.DefinitionLine - 1;
							const definitionColumn = gateInfo.DefinitionColumn - 1;
							console.log(`Navigating to gate ${connectionRef.gateName} definition line: ${definitionLine}, column: ${definitionColumn} in ${circuitInfo.FilePath}`);
							return {
								uri: textDocumentPosition.textDocument.uri,
								range: {
									start: { line: definitionLine, character: definitionColumn },
									end: { line: definitionLine, character: definitionColumn + word.length }
								}
							};
						} else if (connectionRef.target === 'port' && connectionRef.portName) {
							console.log(3);
							// Find the gate first
							const gateInfo = circuitInfo.Gates?.[connectionRef.gateName];
							if (gateInfo) {
								let targetCircuit = circuitInfo;
								let targetUri = textDocumentPosition.textDocument.uri;
								
								// If the gate is a Circuit type, navigate to the referenced circuit
								if (gateInfo.Type.startsWith('Circuit:')) {
									const circuitName = gateInfo.Type.substring('Circuit:'.length);
									const referencedCircuit = circuitInfos.find(c => c.Name === circuitName);
									if (referencedCircuit) {
										targetCircuit = referencedCircuit;
										targetUri = url.pathToFileURL(referencedCircuit.FilePath).toString();
									}
								}
								
								// Look for the port in the target circuit's inputs or outputs
								const portInfo = targetCircuit.Inputs.find(p => p.Name === connectionRef.portName) || 
												targetCircuit.Outputs.find(p => p.Name === connectionRef.portName);
								if (portInfo) {
									const definitionLine = portInfo.DefinitionLine - 1;
									const definitionColumn = portInfo.DefinitionColumn - 1;
									console.log(`Navigating to port ${connectionRef.portName} definition line: ${definitionLine}, column: ${definitionColumn} in ${targetCircuit.FilePath}`);
									return {
										uri: targetUri,
										range: {
											start: { line: definitionLine, character: definitionColumn },
											end: { line: definitionLine, character: definitionColumn + portInfo.Name.length }
										}
									};
								}
							}
						} else if (connectionRef.target === 'direction' && connectionRef.direction) {
							console.log(5);
							// Go to inputs/outputs block of the referenced circuit
							const gateInfo = circuitInfo.Gates?.[connectionRef.gateName];
							if (gateInfo && gateInfo.Type.startsWith('Circuit:')) {
								console.log(6);
								const circuitName = gateInfo.Type.substring('Circuit:'.length);
								const referencedCircuit = circuitInfos.find(c => c.Name === circuitName);
								if (referencedCircuit) {
									console.log(7);
									const blockName = connectionRef.direction;
									const block = referencedCircuit.Blocks?.[blockName];
									if (block) {
										console.log(`Navigating to ${blockName} block definition line: ${block.StartLine - 1} in ${referencedCircuit.FilePath}`);
										return {
											uri: url.pathToFileURL(referencedCircuit.FilePath).toString(),
											range: {
												start: { line: block.StartLine - 1, character: 0 },
												end: { line: block.StartLine - 1, character: blockName.length }
											}
										};
									}
								}
							}
						}
					}
				} else if (circuitInfos) {
					console.log(8);
					// Handle direct input/output port references in connections
					for (const circuitInfo of circuitInfos) {
						if (circuitInfo.Inputs.some(p => p.Name === word)) {
							console.log(9);
							const inputsBlock = circuitInfo.Blocks?.['inputs'];
							if (inputsBlock) {
								console.log(10);
								const lines = text.split('\n');
								for (let i = inputsBlock.StartLine - 1; i < inputsBlock.EndLine; i++) {
									const line = lines[i];
									const wordIndex = line.indexOf(word);
									if (wordIndex !== -1) {
										console.log(`Navigating to input ${word} definition line: ${i} in ${circuitInfo.FilePath}`);
										return {
											uri: textDocumentPosition.textDocument.uri,
											range: {
												start: { line: i, character: wordIndex },
												end: { line: i, character: wordIndex + word.length }
											}
										};
									}
								}
							}
							break; // Found the circuit with this input
						} else if (circuitInfo.Outputs.some(p => p.Name === word)) {
							console.log(11);
							const outputsBlock = circuitInfo.Blocks?.['outputs'];
							if (outputsBlock) {
								console.log(12);
								const lines = text.split('\n');
								for (let i = outputsBlock.StartLine - 1; i < outputsBlock.EndLine; i++) {
									const line = lines[i];
									const wordIndex = line.indexOf(word);
									if (wordIndex !== -1) {
										console.log(`Navigating to output ${word} definition line: ${i} in ${circuitInfo.FilePath}`);
										return {
											uri: textDocumentPosition.textDocument.uri,
											range: {
												start: { line: i, character: wordIndex },
												end: { line: i, character: wordIndex + word.length }
											}
										};
									}
								}
							}
							break; // Found the circuit with this output
						}
					}
				}
				break;
			case 'gates':
				// Handle gates block - go to gate definition
				if (circuitInfos) {
					for (const circuitInfo of circuitInfos) {
						if (circuitInfo.Gates && circuitInfo.Gates[word]) {
							const gateInfo = circuitInfo.Gates[word];
							const definitionLine = gateInfo.DefinitionLine - 1;
							const definitionColumn = gateInfo.DefinitionColumn - 1;
							return {
								uri: textDocumentPosition.textDocument.uri,
								range: {
									start: { line: definitionLine, character: definitionColumn },
									end: { line: definitionLine, character: definitionColumn + word.length }
								}
							};
						}
					}
				}
				break;
			default:
				// Handle default case
				break;
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
function parseConnectionReference(word: string, text: string, offset: number): { target: 'gate' | 'port' | 'direction'; gateName: string; direction?: 'in' | 'out'; portName?: string } | null {
	// Find all connection references that contain the cursor position
	const allMatches: Array<{ match: RegExpExecArray, direction: 'in' | 'out', hasPort: boolean }> = [];

	const inPattern = /(\w+)\.in\.(\w+)/g;
	const outPattern = /(\w+)\.out\.(\w+)/g;
	const inOnlyPattern = /(\w+)\.in\b/g;
	const outOnlyPattern = /(\w+)\.out\b/g;

	let match;

	// Find all matches that contain the cursor
	while ((match = inPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({ match, direction: 'in', hasPort: true });
		}
	}

	while ((match = outPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({ match, direction: 'out', hasPort: true });
		}
	}

	while ((match = inOnlyPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({ match, direction: 'in', hasPort: false });
		}
	}

	while ((match = outOnlyPattern.exec(text)) !== null) {
		if (match.index <= offset && offset <= match.index + match[0].length) {
			allMatches.push({ match, direction: 'out', hasPort: false });
		}
	}

	// Find the best match (prefer longer port names)
	let bestMatch: { target: 'gate' | 'port' | 'direction'; gateName: string; direction?: 'in' | 'out'; portName?: string } | null = null;

	for (const m of allMatches) {
		console.log(`Evaluating match: ${m.match}`);

		const portName = m.hasPort ? m.match[2] : undefined;
		const gateName = m.match[1];
		const direction = m.direction;
		const matchStr = m.match[0];
		const matchStart = m.match.index;

		// Determine which part the cursor is on based on word match first
		let target: 'gate' | 'port' | 'direction' = 'port'; // default to port
		if (word === gateName) {
			target = 'gate';
			console.log(`Cursor on gate name: ${gateName}`);
		} else if (portName && word === portName) {
			target = 'port';
			console.log(`Cursor on port name: ${portName}`);
		} else if (word === 'in' || word === 'out') {
			target = 'direction';
			console.log(`Cursor on direction: ${word}`);
		} else {
			// Fallback to position-based detection
			const gateEnd = matchStart + gateName.length;
			const portStart = matchStart + matchStr.lastIndexOf('.') + 1;

			if (offset <= gateEnd) {
				target = 'gate';
			} else if (portName && offset >= portStart) {
				target = 'port';
			}
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

// Helper function to get block info at a given position
function getBlockInfoAtPosition(document: TextDocument, offset: number): { blockName: string, blockInfo: BlockInfo } | null {

	const pos = document!.positionAt(offset);
	const line = pos.line; // 0-based

	// Get circuit info for the current document
	const documentPath = url.fileURLToPath(document!.uri);
	const documentDir = path.dirname(documentPath);
	const circuitInfos = getCircuitInfo(document!.getText(), documentDir, documentPath);

	if (!circuitInfos) {
		return null;
	}

	// Check each circuit's blocks
	for (const circuitInfo of circuitInfos) {
		if (circuitInfo.Blocks) {
			for (const [blockName, blockInfo] of Object.entries(circuitInfo.Blocks)) {
				// Convert 1-based lines to 0-based
				const startLine = blockInfo.StartLine - 1;
				const endLine = blockInfo.EndLine - 1;
				if (line >= startLine && line <= endLine) {
					return { blockName, blockInfo };
				}
			}
		}
	}

	return null;
}

// Make the text document manager listen on the connection
// for open, change and close text document events
documents.listen(connection);

// Listen on the connection
connection.listen();