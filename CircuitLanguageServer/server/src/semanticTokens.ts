import { SemanticTokensParams, SemanticTokensBuilder } from 'vscode-languageserver/node';
import { spawn } from 'child_process';
import * as path from 'path';

import { connection, documents } from './connection';

export function initializeSemanticTokensHandler(): void {
	// Semantic tokens support for syntax highlighting
	connection.languages.semanticTokens.on((params: SemanticTokensParams) => {
		const document = documents.get(params.textDocument.uri);
		if (!document) return { data: [] };

		const text = document.getText();

		// Path to CircuitSimulator.dll
		const dllPath = path.join(__dirname, '..', '..', 'bin', 'CircuitSimulator.dll');

		return new Promise((resolve) => {
			const child = spawn('dotnet', [dllPath, '--tokens'], {
				stdio: ['pipe', 'pipe', 'pipe']
			});

			let output = '';
			let errorOutput = '';

			child.stdout.on('data', (data) => {
				output += data.toString();
			});

			child.stderr.on('data', (data) => {
				errorOutput += data.toString();
			});

			child.on('close', (code) => {
				if (code !== 0) {
					console.error('CircuitSimulator --tokens failed:', errorOutput);
					resolve({ data: [] });
					return;
				}

				try {
					const tokens: number[][] = JSON.parse(output.trim());
					const builder = new SemanticTokensBuilder();
					for (const token of tokens) {
						builder.push(token[0], token[1], token[2], token[3], token[4]);
					}
					resolve(builder.build());
				} catch (e) {
					console.error('Failed to parse tokens JSON:', e);
					resolve({ data: [] });
				}
			});

			child.on('error', (err) => {
				console.error('Failed to spawn CircuitSimulator:', err);
				resolve({ data: [] });
			});

			// Send the document text to stdin
			child.stdin.write(text);
			child.stdin.end();
		});
	});
}