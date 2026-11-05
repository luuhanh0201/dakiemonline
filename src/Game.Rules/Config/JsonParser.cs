using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Rules
{
    internal enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null,
    }

    internal sealed class JsonNode
    {
        public JsonNode(JsonKind kind, string text = "")
        {
            Kind = kind;
            Text = text;
        }

        public JsonKind Kind { get; }

        /// <summary>The string value, or a number exactly as written so decimals stay exact.</summary>
        public string Text { get; }

        public Dictionary<string, JsonNode> Fields { get; } = new Dictionary<string, JsonNode>();
        public List<JsonNode> Items { get; } = new List<JsonNode>();
    }

    /// <summary>
    /// Strict JSON reader. .NET Standard 2.1 has no built-in JSON and Game.Rules must not
    /// depend on Unity, so the library parses its config files itself.
    /// </summary>
    internal sealed class JsonParser
    {
        private const char ByteOrderMark = '﻿';

        private readonly string file;
        private readonly string text;
        private int position;

        private JsonParser(string file, string text)
        {
            this.file = file;
            this.text = text;
        }

        public static JsonNode Parse(string file, string text)
        {
            var parser = new JsonParser(file, text);
            return parser.ParseDocument();
        }

        private JsonNode ParseDocument()
        {
            if (text.Length > 0 && text[0] == ByteOrderMark)
            {
                position = 1;
            }

            JsonNode root = ParseValue();
            SkipWhitespace();

            if (position < text.Length)
            {
                throw Error("unexpected text after the end of the document");
            }

            return root;
        }

        private JsonNode ParseValue()
        {
            SkipWhitespace();

            if (position >= text.Length)
            {
                throw Error("unexpected end of file");
            }

            char next = text[position];

            switch (next)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return new JsonNode(JsonKind.String, ParseString());
                case 't':
                    return ParseLiteral("true", JsonKind.Boolean);
                case 'f':
                    return ParseLiteral("false", JsonKind.Boolean);
                case 'n':
                    return ParseLiteral("null", JsonKind.Null);
                default:
                    return ParseNumber();
            }
        }

        private JsonNode ParseObject()
        {
            var node = new JsonNode(JsonKind.Object);
            position++;
            SkipWhitespace();

            if (TryConsume('}'))
            {
                return node;
            }

            while (true)
            {
                SkipWhitespace();

                if (position >= text.Length || text[position] != '"')
                {
                    throw Error("expected a field name in double quotes");
                }

                string name = ParseString();

                if (node.Fields.ContainsKey(name))
                {
                    throw Error($"field '{name}' appears twice");
                }

                SkipWhitespace();
                Expect(':');
                node.Fields[name] = ParseValue();
                SkipWhitespace();

                if (TryConsume('}'))
                {
                    return node;
                }

                Expect(',');
            }
        }

        private JsonNode ParseArray()
        {
            var node = new JsonNode(JsonKind.Array);
            position++;
            SkipWhitespace();

            if (TryConsume(']'))
            {
                return node;
            }

            while (true)
            {
                node.Items.Add(ParseValue());
                SkipWhitespace();

                if (TryConsume(']'))
                {
                    return node;
                }

                Expect(',');
            }
        }

        private string ParseString()
        {
            var builder = new StringBuilder();
            position++;

            while (position < text.Length)
            {
                char next = text[position++];

                if (next == '"')
                {
                    return builder.ToString();
                }

                if (next < ' ')
                {
                    throw Error("control character inside a string");
                }

                builder.Append(next == '\\' ? ParseEscape() : next);
            }

            throw Error("string is not closed");
        }

        private char ParseEscape()
        {
            if (position >= text.Length)
            {
                throw Error("string is not closed");
            }

            char escape = text[position++];

            switch (escape)
            {
                case '"':
                case '\\':
                case '/':
                    return escape;
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'u':
                    return ParseUnicodeEscape();
                default:
                    throw Error($"unknown escape '\\{escape}'");
            }
        }

        private char ParseUnicodeEscape()
        {
            const int HexDigits = 4;

            if (position + HexDigits <= text.Length
                && ushort.TryParse(
                    text.Substring(position, HexDigits),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out ushort code))
            {
                position += HexDigits;
                return (char)code;
            }

            throw Error("\\u needs four hex digits");
        }

        private JsonNode ParseNumber()
        {
            int start = position;

            TryConsume('-');

            if (TryConsume('0') == false)
            {
                RequireDigits();
            }

            if (TryConsume('.'))
            {
                RequireDigits();
            }

            if (TryConsume('e') || TryConsume('E'))
            {
                if (TryConsume('+') == false)
                {
                    TryConsume('-');
                }

                RequireDigits();
            }

            return new JsonNode(JsonKind.Number, text.Substring(start, position - start));
        }

        private void RequireDigits()
        {
            int start = position;

            while (position < text.Length && text[position] >= '0' && text[position] <= '9')
            {
                position++;
            }

            if (position == start)
            {
                throw Error("expected a value");
            }
        }

        private JsonNode ParseLiteral(string literal, JsonKind kind)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
            {
                throw Error("expected a value");
            }

            position += literal.Length;
            return new JsonNode(kind, literal);
        }

        private void SkipWhitespace()
        {
            while (position < text.Length
                && (text[position] == ' ' || text[position] == '\t' || text[position] == '\n' || text[position] == '\r'))
            {
                position++;
            }
        }

        private bool TryConsume(char expected)
        {
            if (position < text.Length && text[position] == expected)
            {
                position++;
                return true;
            }

            return false;
        }

        private void Expect(char expected)
        {
            if (TryConsume(expected) == false)
            {
                throw Error($"expected '{expected}'");
            }
        }

        private InvalidConfigException Error(string problem)
        {
            int line = 1;

            for (int index = 0; index < position && index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                }
            }

            return new InvalidConfigException(file, $"line {line}", problem);
        }
    }
}
