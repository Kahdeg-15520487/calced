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

// Create a connection for the server, using Node's IPC as a transport.
// Also include all preview / proposed LSP features.
const connection = createConnection(ProposedFeatures.all);

// Create a simple text document manager.
const documents: TextDocuments<TextDocument> = new TextDocuments(TextDocument);

let hasConfigurationCapability = false;
let hasWorkspaceFolderCapability = false;
let hasDiagnosticRelatedInformationCapability = false;

connection.onInitialize((params: InitializeParams) => {
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
				resolveProvider: true
			}
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

connection.onDidChangeConfiguration(change => {
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
documents.onDidClose(e => {
	documentSettings.delete(e.document.uri);
});

// The content of a text document has changed. This event is emitted
// when the text document first opened or when its content has changed.
documents.onDidChangeContent(change => {
	validateTextDocument(change.document);
});

async function validateTextDocument(textDocument: TextDocument): Promise<void> {
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
		
		await new Promise<void>((resolve, reject) => {
			execFile(simulatorPath, [tempFile, '--verify'], (error, stdout, stderr) => {
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

connection.onDidChangeWatchedFiles(_change => {
	// Monitored files have change in VS Code
	connection.console.log('We received a file change event');
});

// This handler provides the initial list of the completion items.
connection.onCompletion(
	(_textDocumentPosition: TextDocumentPositionParams): CompletionItem[] => {
		// The pass parameter contains the position of the text document in
		// which code complete got requested. For the example we ignore this
		// info and always provide the same completion items.
		return [
			{
				label: 'circuit',
				kind: CompletionItemKind.Keyword,
				data: 1
			},
			{
				label: 'inputs',
				kind: CompletionItemKind.Keyword,
				data: 2
			},
			{
				label: 'outputs',
				kind: CompletionItemKind.Keyword,
				data: 3
			},
			{
				label: 'gates',
				kind: CompletionItemKind.Keyword,
				data: 4
			},
			{
				label: 'connections',
				kind: CompletionItemKind.Keyword,
				data: 5
			},
			{
				label: 'AND',
				kind: CompletionItemKind.Function,
				data: 6
			},
			{
				label: 'OR',
				kind: CompletionItemKind.Function,
				data: 7
			},
			{
				label: 'NOT',
				kind: CompletionItemKind.Function,
				data: 8
			}
		];
	}
);

// This handler resolves additional information for the item selected in
// the completion list.
connection.onCompletionResolve(
	(item: CompletionItem): CompletionItem => {
		if (item.data === 1) {
			item.detail = 'Circuit declaration';
			item.documentation = 'Declare a new circuit';
		} else if (item.data === 2) {
			item.detail = 'Inputs block';
			item.documentation = 'Define circuit inputs';
		} else if (item.data === 3) {
			item.detail = 'Outputs block';
			item.documentation = 'Define circuit outputs';
		} else if (item.data === 4) {
			item.detail = 'Gates block';
			item.documentation = 'Define gates and subcircuits';
		} else if (item.data === 5) {
			item.detail = 'Connections block';
			item.documentation = 'Define signal connections';
		} else if (item.data === 6) {
			item.detail = 'AND gate';
			item.documentation = 'Logical AND gate';
		} else if (item.data === 7) {
			item.detail = 'OR gate';
			item.documentation = 'Logical OR gate';
		} else if (item.data === 8) {
			item.detail = 'NOT gate';
			item.documentation = 'Inverter gate';
		}
		return item;
	}
);

// Make the text document manager listen on the connection
// for open, change and close text document events
documents.listen(connection);

// Listen on the connection
connection.listen();