import { SemanticTokensParams, SemanticTokensBuilder } from 'vscode-languageserver/node';

import { connection, documents } from './connection';

export function initializeSemanticTokensHandler(): void {
	// Semantic tokens support for syntax highlighting
	connection.languages.semanticTokens.on((params: SemanticTokensParams) => {
		const document = documents.get(params.textDocument.uri);
		if (!document) return { data: [] };

		const text = document.getText();
		const builder = new SemanticTokensBuilder();

		// Define token type indices based on the legend
		const TOKEN_TYPES = {
			circuitKeyword: 0,
			circuitOperator: 1,
			circuitFunction: 2,
			comment: 3,
			string: 4,
			identifier: 5
		};

		// Known tokens that are not identifiers
		const knownTokens = new Set([
			'circuit', 'import', 'inputs', 'outputs', 'lookup_tables', 'gates', 'connections',
			'AND', 'OR', 'NOT', 'NAND', 'NOR', 'XOR', 'XNOR', 'DFF', 'Circuit', 'LookupTable'
		]);

		const lines = text.split('\n');
		let lineIndex = 0;

		for (const line of lines) {
			// Comments (parse first to avoid conflicts)
			const commentRegex = /\/\/.*$/g;
			let match;
			while ((match = commentRegex.exec(line)) !== null) {
				builder.push(lineIndex, match.index, match[0].length, TOKEN_TYPES.comment, 0);
			}

			// Strings (parse before keywords/operators to avoid highlighting inside strings)
			const stringRegex = /"[^"]*"/g;
			while ((match = stringRegex.exec(line)) !== null) {
				builder.push(lineIndex, match.index, match[0].length, TOKEN_TYPES.string, 0);
			}

			// Keywords
			const keywordRegex = /\b(circuit|import|inputs|outputs|lookup_tables|gates|connections)\b/g;
			while ((match = keywordRegex.exec(line)) !== null) {
				builder.push(lineIndex, match.index, match[0].length, TOKEN_TYPES.circuitKeyword, 0);
			}

			// Operators (handle -> first as it's multi-character)
			const operatorPatterns = [
				{ regex: /->/g, type: TOKEN_TYPES.circuitOperator },
				{ regex: /[=\.(){}[\]]/g, type: TOKEN_TYPES.circuitOperator }
			];

			for (const pattern of operatorPatterns) {
				pattern.regex.lastIndex = 0; // Reset regex state
				while ((match = pattern.regex.exec(line)) !== null) {
					builder.push(lineIndex, match.index, match[0].length, pattern.type, 0);
				}
			}

			// Functions: gate names and built-in functions followed by (
			const functionRegex = /\b(AND|OR|NOT|NAND|NOR|XOR|XNOR|DFF|Circuit|LookupTable)\b(?=\s*\()/g;
			while ((match = functionRegex.exec(line)) !== null) {
				builder.push(lineIndex, match.index, match[0].length, TOKEN_TYPES.circuitFunction, 0);
			}

			// Identifiers: words that are not known tokens
			const identifierRegex = /\b[a-zA-Z_][a-zA-Z0-9_]*\b/g;
			while ((match = identifierRegex.exec(line)) !== null) {
				const word = match[0];
				if (!knownTokens.has(word)) {
					console.log(`Identifier found: ${word} at line ${lineIndex}, char ${match.index}`);
					builder.push(lineIndex, match.index, match[0].length, TOKEN_TYPES.identifier, 0);
				}
			}

			lineIndex++;
		}

		return builder.build();
	});
}