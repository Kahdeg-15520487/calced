import * as path from 'path';
import * as vscode from 'vscode';
import { workspace, ExtensionContext } from 'vscode';
import * as fs from 'fs';

import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

const builtinDocs: Record<string, string> = {};

function loadBuiltinDocs(context: ExtensionContext) {
	const builtinDir = path.join(context.extensionPath, 'builtin');
	try {
		const files = fs.readdirSync(builtinDir);
		for (const file of files) {
			if (file.endsWith('.circuit')) {
				const gateName = path.basename(file, '.circuit');
				const filePath = path.join(builtinDir, file);
				const content = fs.readFileSync(filePath, 'utf8');
				builtinDocs[`/${gateName}.circuit`.toLowerCase()] = content;
			}
		}
	} catch (error) {
		console.error('Failed to load builtin docs:', error);
	}
}

export function activate(context: ExtensionContext) {
	loadBuiltinDocs(context);

	const provider = {
		provideTextDocumentContent(uri: vscode.Uri): string {
			return builtinDocs[uri.path.toLowerCase()] || '// Unknown builtin';
		},
	};

	context.subscriptions.push(
		vscode.workspace.registerTextDocumentContentProvider('circuit-builtin', provider)
	);

	// The server is implemented in node
	const serverModule = context.asAbsolutePath(
		path.join('out', 'server', 'server.js')
	);

	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	const serverOptions: ServerOptions = {
		run: { module: serverModule, transport: TransportKind.ipc },
		debug: {
			module: serverModule,
			transport: TransportKind.ipc,
		}
	};

	// Options to control the language client
	const clientOptions: LanguageClientOptions = {
		// Register the server for circuit documents
		documentSelector: [{ scheme: 'file', language: 'circuit' }],
		synchronize: {
			// Notify the server about file changes to '.circuit' files contained in the workspace
			fileEvents: workspace.createFileSystemWatcher('**/*.circuit')
		}
	};

	// Create the language client and start the client.
	client = new LanguageClient(
		'circuitLanguageServer',
		'Circuit Language Server',
		serverOptions,
		clientOptions
	);

	// Start the client. This will also launch the server
	client.start();
}

export function deactivate(): Thenable<void> | undefined {
	if (!client) {
		return undefined;
	}
	return client.stop();
}