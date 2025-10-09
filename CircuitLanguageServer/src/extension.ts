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

let extensionContext: ExtensionContext;

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

async function runCircuit() {
	const activeEditor = vscode.window.activeTextEditor;
	if (!activeEditor || activeEditor.document.languageId !== 'circuit') {
		vscode.window.showErrorMessage('Please open a circuit file to run.');
		return;
	}

	const circuitFile = activeEditor.document.uri.fsPath;
	const circuitDir = path.dirname(circuitFile);
	const simulatorPath = path.join(extensionContext.extensionPath, 'bin', 'CircuitSimulator.exe');

	// Get circuit info
	const infoResult = await runSimulatorCommand(simulatorPath, [circuitFile, '--info'], circuitDir);
	if (!infoResult.success) {
		vscode.window.showErrorMessage(`Failed to get circuit info: ${infoResult.error}`);
		return;
	}

	let circuitInfo;
	try {
		const circuits = JSON.parse(infoResult.output);
		circuitInfo = circuits[circuits.length - 1]; // Take the last (main) circuit
	} catch (e) {
		vscode.window.showErrorMessage('Failed to parse circuit info.');
		return;
	}

	// Collect inputs from user
	const inputs: string[] = [];
	for (const input of circuitInfo.Inputs) {
		const value = await vscode.window.showInputBox({
			prompt: `Enter value for input '${input.Name}' (${input.BitWidth} bit${input.BitWidth > 1 ? 's' : ''})`,
			placeHolder: 'Leave empty to use default, or enter value (e.g., 5, b101, hFF)',
			validateInput: (value) => {
				if (!value) return null; // Empty is allowed
				// Basic validation - could be improved
				return null;
			}
		});

		if (value !== undefined) { // User didn't cancel
			if (value) {
				inputs.push(`--${input.Name}=${value}`);
			}
		} else {
			return; // User cancelled
		}
	}

	// Run simulation
	const simArgs = [circuitFile, '--ticks=1'].concat(inputs);
	const simResult = await runSimulatorCommand(simulatorPath, simArgs, circuitDir);
	
	if (simResult.success) {
		// Show results in output channel
		const outputChannel = vscode.window.createOutputChannel('Circuit Simulation');
		outputChannel.clear();
		outputChannel.appendLine(`Running circuit: ${path.basename(circuitFile)}`);
		outputChannel.appendLine('Inputs:');
		for (const input of circuitInfo.Inputs) {
			const inputArg = inputs.find(arg => arg.startsWith(`--${input.Name}=`));
			const value = inputArg ? inputArg.split('=')[1] : 'default';
			outputChannel.appendLine(`  ${input.Name}: ${value}`);
		}
		outputChannel.appendLine('');
		outputChannel.appendLine('Results:');
		outputChannel.appendLine(simResult.output);
		outputChannel.show();
	} else {
		vscode.window.showErrorMessage(`Simulation failed: ${simResult.error}`);
	}
}

async function runSimulatorCommand(simulatorPath: string, args: string[], cwd: string): Promise<{success: boolean, output: string, error: string}> {
	return new Promise((resolve) => {
		const { spawn } = require('child_process');
		const child = spawn(simulatorPath, args, { cwd });

		let stdout = '';
		let stderr = '';

		child.stdout.on('data', (data: Buffer) => {
			stdout += data.toString();
		});

		child.stderr.on('data', (data: Buffer) => {
			stderr += data.toString();
		});

		child.on('close', (code: number) => {
			resolve({
				success: code === 0,
				output: stdout,
				error: stderr
			});
		});

		child.on('error', (err: Error) => {
			resolve({
				success: false,
				output: stdout,
				error: err.message
			});
		});
	});
}

export function activate(context: ExtensionContext) {
	extensionContext = context;
	loadBuiltinDocs(context);

	const provider = {
		provideTextDocumentContent(uri: vscode.Uri): string {
			return builtinDocs[uri.path.toLowerCase()] || '// Unknown builtin';
		},
	};

	context.subscriptions.push(
		vscode.workspace.registerTextDocumentContentProvider('circuit-builtin', provider)
	);

	// Register the run circuit command
	context.subscriptions.push(
		vscode.commands.registerCommand('circuit.run', runCircuit)
	);

	// Register the create file command
	context.subscriptions.push(
		vscode.commands.registerCommand('circuit.createFile', async (filePath: string) => {
			try {
				const uri = vscode.Uri.file(filePath);
				await vscode.workspace.fs.writeFile(uri, new Uint8Array());
				const doc = await vscode.workspace.openTextDocument(uri);
				await vscode.window.showTextDocument(doc);
			} catch (error) {
				vscode.window.showErrorMessage(`Failed to create file: ${error}`);
			}
		})
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