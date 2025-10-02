using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CircuitSimulator
{
    // Token types for the DSL lexer
    public enum TokenType
    {
        // Keywords
        CIRCUIT, INPUTS, OUTPUTS, GATES, CONNECTIONS, LOOKUP_TABLES, IMPORT,
        
        // Literals
        IDENTIFIER, STRING, NUMBER,
        
        // Symbols
        LBRACE, RBRACE, LBRACKET, RBRACKET, LPAREN, RPAREN, ARROW, COMMA, EQUALS, DOT,
        
        // Special
        EOF, COMMENT
    }

    public class Token
    {
        public TokenType Type { get; }
        public string Value { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenType type, string value, int line, int column)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"{Type}({Value})";
        }
    }

    public class Lexer
    {
        private readonly string _input;
        private int _position;
        private int _line;
        private int _column;

        private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
        {
            ["circuit"] = TokenType.CIRCUIT,
            ["inputs"] = TokenType.INPUTS,
            ["outputs"] = TokenType.OUTPUTS,
            ["gates"] = TokenType.GATES,
            ["connections"] = TokenType.CONNECTIONS,
            ["lookup_tables"] = TokenType.LOOKUP_TABLES,
            ["import"] = TokenType.IMPORT
        };

        public Lexer(string input)
        {
            _input = input;
            _position = 0;
            _line = 1;
            _column = 1;
        }

        public IEnumerable<Token> Tokenize()
        {
            while (_position < _input.Length)
            {
                char current = _input[_position];

                if (char.IsWhiteSpace(current))
                {
                    SkipWhitespace();
                    continue;
                }

                if (current == '/' && Peek() == '/')
                {
                    SkipComment();
                    continue;
                }

                switch (current)
                {
                    case '{': yield return ConsumeToken(TokenType.LBRACE, "{"); break;
                    case '}': yield return ConsumeToken(TokenType.RBRACE, "}"); break;
                    case '[': yield return ConsumeToken(TokenType.LBRACKET, "["); break;
                    case ']': yield return ConsumeToken(TokenType.RBRACKET, "]"); break;
                    case '(': yield return ConsumeToken(TokenType.LPAREN, "("); break;
                    case ')': yield return ConsumeToken(TokenType.RPAREN, ")"); break;
                    case ',': yield return ConsumeToken(TokenType.COMMA, ","); break;
                    case '=': yield return ConsumeToken(TokenType.EQUALS, "="); break;
                    case '.': yield return ConsumeToken(TokenType.DOT, "."); break;
                    case '"': yield return ReadString(); break;
                    case '-':
                        if (Peek() == '>')
                        {
                            _position += 2; // consume both '-' and '>'
                            _column += 2;
                            yield return new Token(TokenType.ARROW, "->", _line, _column - 2);
                        }
                        else
                        {
                            throw new DSLInvalidSyntaxException(_line, _column, $"Unexpected character: {current}");
                        }
                        break;
                    default:
                        if (char.IsLetter(current) || current == '_')
                        {
                            yield return ReadIdentifier();
                        }
                        else if (char.IsDigit(current))
                        {
                            yield return ReadNumber();
                        }
                        else
                        {
                            throw new DSLInvalidSyntaxException(_line, _column, $"Unexpected character: {current}");
                        }
                        break;
                }
            }

            yield return new Token(TokenType.EOF, "", _line, _column);
        }

        private void SkipWhitespace()
        {
            while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
            {
                if (_input[_position] == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
        }

        private void SkipComment()
        {
            while (_position < _input.Length && _input[_position] != '\n')
            {
                _position++;
                _column++;
            }
        }

        private Token ConsumeToken(TokenType type, string value)
        {
            var token = new Token(type, value, _line, _column);
            _position++;
            _column++;
            return token;
        }

        private char Peek()
        {
            return _position + 1 < _input.Length ? _input[_position + 1] : '\0';
        }

        private void Consume()
        {
            _position++;
            _column++;
        }

        private Token ReadString()
        {
            int startColumn = _column;
            _position++; // skip opening quote
            _column++;
            int start = _position;

            while (_position < _input.Length && _input[_position] != '"')
            {
                if (_input[_position] == '\n')
                {
                    throw new DSLInvalidSyntaxException(_line, _column, "Unterminated string literal");
                }
                _position++;
                _column++;
            }

            if (_position >= _input.Length)
            {
                throw new DSLInvalidSyntaxException(_line, _column, "Unterminated string literal");
            }

            string value = _input.Substring(start, _position - start);
            _position++; // skip closing quote
            _column++;

            return new Token(TokenType.STRING, value, _line, startColumn);
        }

        private Token ReadIdentifier()
        {
            int startColumn = _column;
            int start = _position;

            while (_position < _input.Length && (char.IsLetterOrDigit(_input[_position]) || _input[_position] == '_'))
            {
                _position++;
                _column++;
            }

            string value = _input.Substring(start, _position - start);

            // Check if it's a keyword
            if (Keywords.TryGetValue(value, out TokenType keywordType))
            {
                return new Token(keywordType, value, _line, startColumn);
            }

            return new Token(TokenType.IDENTIFIER, value, _line, startColumn);
        }

        private Token ReadNumber()
        {
            int startColumn = _column;
            int start = _position;

            while (_position < _input.Length && char.IsDigit(_input[_position]))
            {
                _position++;
                _column++;
            }

            string value = _input.Substring(start, _position - start);
            return new Token(TokenType.NUMBER, value, _line, startColumn);
        }
    }
}