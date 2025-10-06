import { initializeConnection, connection, documents } from './connection';
import { initializeDocumentHandlers } from './documents';
import { initializeCompletionHandlers } from './completion';
import { initializeHoverHandler } from './hover';
import { initializeDefinitionHandler } from './definition';
import { initializeSemanticTokensHandler } from './semanticTokens';

// Initialize all connection and LSP handlers
initializeConnection();
initializeDocumentHandlers();
initializeCompletionHandlers();
initializeHoverHandler();
initializeDefinitionHandler();
initializeSemanticTokensHandler();

// Make the text document manager listen on the connection
// for open, change and close text document events
documents.listen(connection);

// Listen on the connection
connection.listen();