import { Diagnostic, DiagnosticSeverity, TextDocument } from 'vscode-languageserver/node';
import { execFile } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as url from 'url';

import { connection } from './connection';

export async function validateTextDocument(textDocument: TextDocument): Promise<void> {
	const documentPath = url.fileURLToPath(textDocument.uri);
	connection.console.log(`Circuit Language Server: Validating document ${documentPath}`);

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
		const documentDir = path.dirname(documentPath);

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