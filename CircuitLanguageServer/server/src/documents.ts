import { TextDocuments, TextDocument } from 'vscode-languageserver/node';
import { execFileSync } from 'child_process';
import * as crypto from 'crypto';
import * as path from 'path';
import * as fs from 'fs';
import * as url from 'url';

import { CircuitInfo, CircuitInfoCacheEntry } from './types';
import { connection, documents } from './connection';
import { validateTextDocument } from './validation';

// Map to store circuit definitions for hover tooltips
export const circuitDefinitions: Map<string, any> = new Map();

// Cache for getCircuitInfo results to avoid redundant calls
const circuitInfoCache: Map<string, CircuitInfoCacheEntry> = new Map();
const MAX_CACHE_SIZE = 50; // Limit cache size to prevent memory leaks

export function initializeDocumentHandlers(): void {
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

	// Clear circuit info cache when documents are closed
	documents.onDidClose((e: any) => {
		const documentUri = e.document.uri;
		for (const [key, entry] of circuitInfoCache.entries()) {
			if (key.includes(documentUri) || key.includes(url.fileURLToPath(documentUri))) {
				circuitInfoCache.delete(key);
			}
		}
	});
}

// Helper function to get circuit information using CircuitSimulator
export function getCircuitInfo(content: string, basePath: string, originalFilePath?: string): CircuitInfo[] | null {
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