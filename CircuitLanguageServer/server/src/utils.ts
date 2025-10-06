import { TextDocument } from 'vscode-languageserver-textdocument';
import { CircuitInfo, BlockInfo, LookupTableInfo, ConnectionReference } from './types';
import { getCircuitInfo } from './documents';
import * as url from 'url';
import * as path from 'path';

export function getWordRangeAtPosition(text: string, offset: number): { start: number; end: number } | null {
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

// Helper function to parse connection references like gateName.in.portName
export function parseConnectionReference(word: string, text: string, offset: number): ConnectionReference | null {
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
	let bestMatch: ConnectionReference | null = null;

	for (const m of allMatches) {

		const portName = m.hasPort ? m.match[2] : undefined;
		const gateName = m.match[1];
		const direction = m.direction;
		const matchStr = m.match[0];
		const matchStart = m.match.index;

		// Determine which part the cursor is on based on word match first
		let target: 'gate' | 'port' | 'direction' = 'port'; // default to port
		if (word === gateName) {
			target = 'gate';
		} else if (portName && word === portName) {
			target = 'port';
		} else if (word === 'in' || word === 'out') {
			target = 'direction';
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

// Helper function to get block info at a given position
export function getBlockInfoAtPosition(document: TextDocument, offset: number): { blockName: string, blockInfo: BlockInfo } | null {

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
		if (circuitInfo.FilePath === documentPath && circuitInfo.Blocks) {
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

export function findLookupTableInfo(tableName: string, circuitInfos: CircuitInfo[]): LookupTableInfo | undefined {
	for (const circuit of circuitInfos) {
		if (circuit.LookupTables && circuit.LookupTables[tableName]) {
			return circuit.LookupTables[tableName];
		}
	}
	return undefined;
}

export function findCircuitInfoForGateType(gateType: string, circuitInfos: CircuitInfo[]): CircuitInfo | undefined {
	return circuitInfos.find(circuit => circuit.Name === gateType);
}