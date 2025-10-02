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

import { execFile } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as url from 'url';

// Circuit information for hover tooltips
interface CircuitInfo {
	name: string;
	inputs: string[];
	outputs: string[];
	filePath: string;
}

// Create a connection for the server, using Node's IPC as a transport.
// Also include all preview / proposed LSP features.
const connection = createConnection(ProposedFeatures.all);

// Create a simple text document manager.
const documents: TextDocuments<TextDocument> = new TextDocuments(TextDocument);

// Map to store circuit definitions for hover tooltips
const circuitDefinitions: Map<string, CircuitInfo> = new Map();

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
			hoverProvider: true
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
});

// The content of a text document has changed. This event is emitted
// when the text document first opened or when its content has changed.
documents.onDidChangeContent((change: any) => {
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

// Function to parse circuit definitions from a file using C# program
function parseCircuitDefinitions(filePath: string, basePath: string): void {
	try {
		// Path to the bundled CircuitSimulator.exe
		const simulatorPath = path.join(__dirname, '..', '..', 'bin', 'CircuitSimulator.exe');
		
		// Create a temporary file with the circuit content
		const tempDir = path.join(__dirname, '..', '..', 'temp');
		if (!fs.existsSync(tempDir)) {
			fs.mkdirSync(tempDir, { recursive: true });
		}
		
		const tempFile = path.join(tempDir, `info_${Date.now()}.circuit`);
		const content = fs.readFileSync(filePath, 'utf8');
		fs.writeFileSync(tempFile, content);

		// Call C# program with --info mode
		execFile(simulatorPath, [tempFile, '--info', `--base-path=${basePath}`], (error, stdout, stderr) => {
			try {
				if (stdout) {
					const circuitInfos = JSON.parse(stdout);
					for (const info of circuitInfos) {
						circuitDefinitions.set(info.Name, {
							name: info.Name,
							inputs: info.Inputs,
							outputs: info.Outputs,
							filePath: info.FilePath
						});
					}
				}
			} catch (parseError) {
				// Silently ignore parse errors for hover functionality
			} finally {
				// Clean up temp file
				try {
					fs.unlinkSync(tempFile);
				} catch (cleanupError) {
					// Ignore cleanup errors
				}
			}
		});
	} catch (error) {
		// Silently ignore errors for hover functionality
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
		const gateInfo: { [key: string]: string } = {
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
		let circuitInfo = circuitDefinitions.get(word);
		if (circuitInfo) {
			const inputsStr = circuitInfo.inputs.join(', ');
			const outputsStr = circuitInfo.outputs.join(', ');
			const hoverText = `**Circuit: ${circuitInfo.name}**\n\n` +
				`**Inputs:** ${inputsStr || 'none'}\n\n` +
				`**Outputs:** ${outputsStr || 'none'}\n\n` +
				`*Defined in: ${path.basename(circuitInfo.filePath)}*`;
			return {
				contents: {
					kind: 'markdown',
					value: hoverText
				}
			};
		}

		// If not found, try to get circuit info by calling C# program
		try {
			const documentPath = url.fileURLToPath(textDocumentPosition.textDocument.uri);
			const documentDir = path.dirname(documentPath);
			
			// Path to the bundled CircuitSimulator.exe
			const simulatorPath = path.join(__dirname, '..', '..', 'bin', 'CircuitSimulator.exe');
			
			// Create a temporary file with the circuit content
			const tempDir = path.join(__dirname, '..', '..', 'temp');
			if (!fs.existsSync(tempDir)) {
				fs.mkdirSync(tempDir, { recursive: true });
			}
			
			const tempFile = path.join(tempDir, `hover_${Date.now()}.circuit`);
			fs.writeFileSync(tempFile, text);

			// Call C# program with --info mode synchronously
			const { execFileSync } = require('child_process');
			const stdout = execFileSync(simulatorPath, [tempFile, '--info', `--base-path=${documentDir}`], { encoding: 'utf8' });
			
			if (stdout) {
				const circuitInfos = JSON.parse(stdout);
				const foundCircuit = circuitInfos.find((info: any) => info.Name === word);
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
						filePath: foundCircuit.FilePath
					});
					
					// Clean up temp file
					try {
						fs.unlinkSync(tempFile);
					} catch (cleanupError) {
						// Ignore cleanup errors
					}
					
					return {
						contents: {
							kind: 'markdown',
							value: hoverText
						}
					};
				}
			}
			
			// Clean up temp file
			try {
				fs.unlinkSync(tempFile);
			} catch (cleanupError) {
				// Ignore cleanup errors
			}
		} catch (error) {
			// Silently ignore errors for hover functionality
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

// Make the text document manager listen on the connection
// for open, change and close text document events
documents.listen(connection);

// Listen on the connection
connection.listen();