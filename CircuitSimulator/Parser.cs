using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CircuitSimulator
{
    public class Parser
    {
        private readonly List<Token> _tokens;
        private int _current;
        private readonly string _basePath;

        public Parser(List<Token> tokens, string basePath)
        {
            _tokens = tokens;
            _current = 0;
            _basePath = basePath;
        }

        public Dictionary<string, Circuit> ParseCircuits()
        {
            var circuits = new Dictionary<string, Circuit>();

            while (!IsAtEnd())
            {
                if (Match(TokenType.IMPORT))
                {
                    ParseImport(circuits);
                }
                else if (Match(TokenType.CIRCUIT))
                {
                    var circuit = ParseCircuit();
                    circuits[circuit.Name] = circuit;
                }
                else
                {
                    throw new DSLInvalidSyntaxException($"Unexpected token: {Peek()}", $"Expected 'import' or 'circuit' at line {Peek().Line}, column {Peek().Column}");
                }
            }

            return circuits;
        }

        private void ParseImport(Dictionary<string, Circuit> circuits)
        {
            Consume(TokenType.STRING, "Expected string after 'import'");
            string filename = Previous().Value;

            try
            {
                string importPath = Path.Combine(_basePath, filename);
                string importedDsl = File.ReadAllText(importPath);

                var lexer = new Lexer(importedDsl);
                var tokens = lexer.Tokenize().ToList();
                var parser = new Parser(tokens, _basePath);
                var importedCircuits = parser.ParseCircuits();

                foreach (var kvp in importedCircuits)
                {
                    circuits[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                throw new DSLImportException(filename, ex);
            }
        }

        private Circuit ParseCircuit()
        {
            Consume(TokenType.IDENTIFIER, "Expected circuit name");
            string circuitName = Previous().Value;

            var circuit = new Circuit { Name = circuitName };

            Consume(TokenType.LBRACE, "Expected '{' after circuit name");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                if (Match(TokenType.INPUTS))
                {
                    ParseInputs(circuit);
                }
                else if (Match(TokenType.OUTPUTS))
                {
                    ParseOutputs(circuit);
                }
                else if (Match(TokenType.GATES))
                {
                    ParseGates(circuit);
                }
                else if (Match(TokenType.LOOKUP_TABLES))
                {
                    ParseLookupTables();
                }
                else if (Match(TokenType.CONNECTIONS))
                {
                    ParseConnections(circuit);
                }
                else
                {
                    throw new DSLInvalidSyntaxException($"Unexpected token in circuit: {Peek()}", $"Expected block keyword at line {Peek().Line}, column {Peek().Column}");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after circuit body");

            return circuit;
        }

        private void ParseInputs(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'inputs'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                if (Match(TokenType.IDENTIFIER))
                {
                    string name = Previous().Value;

                    if (Match(TokenType.LBRACKET))
                    {
                        Consume(TokenType.NUMBER, "Expected number in array size");
                        int size = int.Parse(Previous().Value);
                        Consume(TokenType.RBRACKET, "Expected ']' after array size");

                        for (int i = 0; i < size; i++)
                        {
                            circuit.ExternalInputs[$"{name}[{i}]"] = false;
                        }
                    }
                    else
                    {
                        circuit.ExternalInputs[name] = false;
                    }
                }

                if (!Check(TokenType.RBRACE))
                {
                    Consume(TokenType.COMMA, "Expected ',' between inputs");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after inputs");
        }

        private void ParseOutputs(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'outputs'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected output name");
                string name = Previous().Value;
                circuit.ExternalOutputs[name] = null;

                if (!Check(TokenType.RBRACE))
                {
                    Consume(TokenType.COMMA, "Expected ',' between outputs");
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after outputs");
        }

        private void ParseGates(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'gates'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected gate name");
                string gateName = Previous().Value;

                Consume(TokenType.EQUALS, "Expected '=' after gate name");

                Consume(TokenType.IDENTIFIER, "Expected gate type");
                string gateType = Previous().Value;

                Gate gate;
                if (gateType == "Circuit")
                {
                    Consume(TokenType.LPAREN, "Expected '(' after 'Circuit'");
                    Consume(TokenType.STRING, "Expected circuit name");
                    string circuitName = Previous().Value;
                    Consume(TokenType.RPAREN, "Expected ')' after circuit name");

                    // For now, we'll assume circuits are already parsed
                    // This is a limitation of the current architecture
                    throw new NotImplementedException("Circuit references in new parser not yet implemented");
                }
                else if (gateType == "LookupTable")
                {
                    Consume(TokenType.LPAREN, "Expected '(' after 'LookupTable'");
                    Consume(TokenType.STRING, "Expected table name");
                    string tableName = Previous().Value;
                    Consume(TokenType.RPAREN, "Expected ')' after table name");

                    gate = RegexParser.CreateLookupTableGate(tableName);
                }
                else
                {
                    // Regular gates like AND(), OR(), etc.
                    Consume(TokenType.LPAREN, "Expected '(' after gate type");
                    Consume(TokenType.RPAREN, "Expected ')' after gate type");

                    if (!RegexParser.GateFactory.TryGetValue(gateType, out var factory))
                    {
                        throw new DSLInvalidGateException(gateName, $"Unknown gate type '{gateType}'");
                    }
                    gate = factory();
                }

                circuit.AddGate(gateName, gate);

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after gates");
        }

        private void ParseLookupTables()
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'lookup_tables'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                Consume(TokenType.IDENTIFIER, "Expected table name");
                string tableName = Previous().Value;

                Consume(TokenType.EQUALS, "Expected '=' after table name");
                Consume(TokenType.LBRACE, "Expected '{' after '='");

                var table = new Dictionary<string, bool[]>();

                while (!Check(TokenType.RBRACE) && !IsAtEnd())
                {
                    Consume(TokenType.NUMBER, "Expected input pattern");
                    string input = Previous().Value;

                    Consume(TokenType.ARROW, "Expected '->' after input");

                    Consume(TokenType.NUMBER, "Expected output pattern");
                    string outputStr = Previous().Value;

                    var output = new bool[outputStr.Length];
                    for (int i = 0; i < outputStr.Length; i++)
                    {
                        if (outputStr[i] == '1')
                            output[i] = true;
                        else if (outputStr[i] == '0')
                            output[i] = false;
                        else
                            throw new DSLInvalidSyntaxException($"Invalid output bit '{outputStr[i]}'", $"Expected '0' or '1' in output pattern");
                    }

                    table[input] = output;

                    if (!Check(TokenType.RBRACE))
                    {
                        // Optional comma
                        Match(TokenType.COMMA);
                    }
                }

                RegexParser.LookupTables[tableName] = table;

                Consume(TokenType.RBRACE, "Expected '}' after table entries");

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after lookup_tables");
        }

        private void ParseConnections(Circuit circuit)
        {
            Consume(TokenType.LBRACE, "Expected '{' after 'connections'");

            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                // Parse source
                string source = ParseSource();

                Consume(TokenType.ARROW, "Expected '->' in connection");

                // Parse target
                string target = ParseTarget();

                // Parse the connection: source -> target
                // For now, implement basic connection parsing
                ParseConnection(circuit, source, target);

                if (!Check(TokenType.RBRACE))
                {
                    // Optional comma
                    Match(TokenType.COMMA);
                }
            }

            Consume(TokenType.RBRACE, "Expected '}' after connections");
        }

        private void ParseConnection(Circuit circuit, string source, string target)
        {
            object sourceObj;
            Gate? targetGate = null;
            int targetInputIndex;

            // Parse source
            if (circuit.ExternalInputs.ContainsKey(source))
            {
                sourceObj = source; // external input name
            }
            else if (source.Contains('.'))
            {
                // gate.output format
                var parts = source.Split('.');
                if (parts.Length == 2 && parts[1] == "out")
                {
                    if (circuit.NamedGates.TryGetValue(parts[0], out var gate))
                    {
                        sourceObj = gate;
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Source gate '{parts[0]}' not found");
                    }
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid source format: {source}");
                }
            }
            else
            {
                throw new DSLInvalidConnectionException($"{source} -> {target}", $"Unknown source: {source}");
            }

            // Parse target
            if (target.Contains('.'))
            {
                var parts = target.Split('.');
                if (parts.Length == 2 && parts[1].StartsWith("in[") && parts[1].EndsWith("]"))
                {
                    var indexStr = parts[1].Substring(3, parts[1].Length - 4);
                    if (int.TryParse(indexStr, out targetInputIndex))
                    {
                        if (circuit.NamedGates.TryGetValue(parts[0], out targetGate))
                        {
                            // Connect
                            circuit.Connect(sourceObj, targetGate, targetInputIndex);
                        }
                        else
                        {
                            throw new DSLInvalidConnectionException($"{source} -> {target}", $"Target gate '{parts[0]}' not found");
                        }
                    }
                    else
                    {
                        throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid input index: {indexStr}");
                    }
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", $"Invalid target format: {target}");
                }
            }
            else if (circuit.ExternalOutputs.ContainsKey(target))
            {
                // Connecting to external output
                if (sourceObj is Gate sourceGate)
                {
                    circuit.ExternalOutputs[target] = sourceGate;
                }
                else
                {
                    throw new DSLInvalidConnectionException($"{source} -> {target}", "Cannot connect external input directly to external output");
                }
            }
            else
            {
                throw new DSLInvalidConnectionException($"{source} -> {target}", $"Unknown target: {target}");
            }
        }

        private string ParseSource()
        {
            Consume(TokenType.IDENTIFIER, "Expected source identifier");
            string source = Previous().Value;

            // Handle array notation like value[0]
            if (Match(TokenType.LBRACKET))
            {
                Consume(TokenType.NUMBER, "Expected array index");
                string index = Previous().Value;
                Consume(TokenType.RBRACKET, "Expected ']' after array index");
                source += $"[{index}]";
            }

            if (Match(TokenType.DOT))
            {
                Consume(TokenType.IDENTIFIER, "Expected 'out' after dot");
                if (Previous().Value != "out")
                {
                    throw new DSLInvalidSyntaxException("Expected 'out' after dot in source", $"Found '{Previous().Value}' at line {Previous().Line}, column {Previous().Column}");
                }

                if (Match(TokenType.LBRACKET))
                {
                    Consume(TokenType.NUMBER, "Expected output index");
                    string index = Previous().Value;
                    Consume(TokenType.RBRACKET, "Expected ']' after output index");
                    source += $".out[{index}]";
                }
                else
                {
                    source += ".out";
                }
            }

            return source;
        }

        private string ParseTarget()
        {
            Consume(TokenType.IDENTIFIER, "Expected target identifier");
            string target = Previous().Value;

            if (Match(TokenType.DOT))
            {
                Consume(TokenType.IDENTIFIER, "Expected 'in' after dot");
                if (Previous().Value != "in")
                {
                    throw new DSLInvalidSyntaxException("Expected 'in' after dot in target", $"Found '{Previous().Value}' at line {Previous().Line}, column {Previous().Column}");
                }

                Consume(TokenType.LBRACKET, "Expected '[' after 'in'");
                Consume(TokenType.NUMBER, "Expected input index");
                string index = Previous().Value;
                Consume(TokenType.RBRACKET, "Expected ']' after input index");
                target += $".in[{index}]";
            }

            return target;
        }

        private bool Match(TokenType type)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
            return false;
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
            {
                return Advance();
            }

            throw new DSLInvalidSyntaxException(message, $"Expected {type} at line {Peek().Line}, column {Peek().Column}");
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.EOF;
        }

        private Token Peek()
        {
            return _tokens[_current];
        }

        private Token Previous()
        {
            return _tokens[_current - 1];
        }
    }
}