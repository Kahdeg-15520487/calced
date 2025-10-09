import {
	createConnection,
	TextDocuments,
	ProposedFeatures,
	InitializeParams,
	DidChangeConfigurationNotification,
	TextDocumentSyncKind,
	InitializeResult,
	CodeAction,
	CodeActionKind,
	WorkspaceEdit,
	CreateFile,
	TextDocumentEdit,
	TextEdit,
	Range
} from 'vscode-languageserver/node';

import {
	TextDocument
} from 'vscode-languageserver-textdocument';

import * as path from 'path';
import { pathToFileURL } from 'url';

import { ExampleSettings } from './types';
import { json } from 'stream/consumers';

// Create a connection for the server, using Node's IPC as a transport.
// Also include all preview / proposed LSP features.
export const connection = createConnection(ProposedFeatures.all);

// Create a simple text document manager.
export const documents: TextDocuments<TextDocument> = new TextDocuments(TextDocument);

let hasConfigurationCapability = false;
let hasWorkspaceFolderCapability = false;
let hasDiagnosticRelatedInformationCapability = false;

// The global settings, used when the `workspace/configuration` request is not supported by the client.
// Please note that this is not the case when using this server with the client provided in this example
// but could happen with other clients.
const defaultSettings: ExampleSettings = { maxNumberOfProblems: 1000 };
let globalSettings: ExampleSettings = defaultSettings;

// Cache the settings of all open documents
const documentSettings: Map<string, Thenable<ExampleSettings>> = new Map();

// Cache diagnostics for code actions
export const documentDiagnostics: Map<string, any[]> = new Map();

export function initializeConnection(): void {
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
					triggerCharacters: ['.', '-', '>', '='],
				},
				// Enable hover support
				hoverProvider: true,
				// Enable definition support
				definitionProvider: true,
				// Enable semantic tokens
				semanticTokensProvider: {
					legend: {
						tokenTypes: ['circuitKeyword', 'circuitOperator', 'circuitFunction', 'comment', 'string', 'identifier'],
						tokenModifiers: []
					},
					full: true
				},
				// Enable code actions
				codeActionProvider: true,
				// Enable execute command
				executeCommandProvider: {
					commands: ['createMissingFile']
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
		connection.console.log('Circuit Language Server: Initialized successfully!');
		if (hasConfigurationCapability) {
			// Register for all configuration changes.
			connection.client.register(DidChangeConfigurationNotification.type, undefined);
		}
	});

	// Validate documents on change
	documents.onDidChangeContent((change) => {
		validateTextDocument(change.document);
	});

	// Handle code actions
	connection.onCodeAction((params) => {
		const actions: CodeAction[] = [];
		const diagnostics = documentDiagnostics.get(params.textDocument.uri) || [];
		for (const diag of diagnostics) {
			if (diag.message.includes("Failed to import file")) {
				const match = diag.message.match(/Could not find file '([^']+)'/);
				if (match) {
					const filePath = match[1];
					const fileUri = 'file://' + filePath.replace(/\\/g, '/');
					actions.push({
						title: `Create missing file '${path.basename(filePath)}'`,
						kind: CodeActionKind.QuickFix,
						diagnostics: [diag],
						command: {
							title: `Create missing file '${path.basename(filePath)}'`,
							command: 'createMissingFile',
							arguments: [filePath]
						}
					});
				}
			}
		}
		return actions;
	});

	// Handle execute command
	connection.onExecuteCommand(async (params) => {
		if (params.command === 'createMissingFile') {
			const filePath = params.arguments![0] as string;
			const fileUri = pathToFileURL(filePath).toString();
			const content = 'circuit MyCircuit{\n    inputs {}\n    outputs {}\n    gates{}\n    connections{}\n}';
			console.log(`Creating missing file at: ${filePath}`);
			const createFile: CreateFile = { kind: 'create', uri: fileUri, options: { overwrite: false } };
			const textEditRange: Range = { start: { line: 0, character: 0 }, end: { line: 0, character: 0 } };
			const textEdit: TextEdit = { range: textEditRange, newText: content };
			const textDocumentEdit: TextDocumentEdit = { textDocument: { uri: fileUri, version: 0 }, edits: [textEdit] };
			const edit: WorkspaceEdit = { documentChanges: [createFile, textDocumentEdit] };
			try {
				// First, create the file
				const createEdit: WorkspaceEdit = { documentChanges: [createFile] };
				const createSuccess = await connection.workspace.applyEdit(createEdit);
				if (createSuccess.applied) {
					// Then, add the content
					const textEditWorkspace: WorkspaceEdit = { documentChanges: [textDocumentEdit] };
					const textSuccess = await connection.workspace.applyEdit(textEditWorkspace);
					if (textSuccess.applied) {
						console.log(`Successfully created missing file at: ${filePath}`);
					} else {
						console.log('Failed to add content to the missing file.');
						connection.window.showErrorMessage('Failed to add content to the created file.');
					}
				} else {
					console.log('Failed to create the missing file. The client may have rejected the workspace edit.');
					connection.window.showErrorMessage('Failed to create the missing file. The client may have rejected the workspace edit.');
				}
			} catch (err: any) {
				console.log(`Error creating file: ${err.message}`);
				connection.window.showErrorMessage(`Error creating file: ${err.message}`);
			}
		}
	});

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
}

export function getDocumentSettings(resource: string): Thenable<ExampleSettings> {
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
	documentDiagnostics.delete(e.document.uri);
});

// Placeholder for validation function - will be implemented in validation module
function validateTextDocument(textDocument: TextDocument): Promise<void> {
	const text = textDocument.getText();
	const diagnostics: any[] = [];
	const lines = text.split('\n');

	for (let i = 0; i < lines.length; i++) {
		const line = lines[i];
		const importMatch = line.match(/import\s+"([^"]+\.circuit)"/);
		if (importMatch) {
			const importedFile = importMatch[1];
			// Assume circuits are in the same directory as the document
			const docDir = path.dirname(textDocument.uri.replace('file://', ''));
			const fullPath = path.join(docDir, importedFile);
			const fs = require('fs');
			if (!fs.existsSync(fullPath)) {
				diagnostics.push({
					severity: 1, // Error
					range: {
						start: { line: i, character: line.indexOf('import') },
						end: { line: i, character: line.length }
					},
					message: `Failed to import file '${fullPath}': Could not find file '${fullPath}'.`,
					source: 'circuit'
				});
			}
		}
	}

	// Send the diagnostics
	connection.sendDiagnostics({ uri: textDocument.uri, diagnostics });
	documentDiagnostics.set(textDocument.uri, diagnostics);

	return Promise.resolve();
}