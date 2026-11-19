using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FolderTools.Core
{
    /// <summary>Thrown when a settings file is not valid JSON.</summary>
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Minimal JSON tree. The project targets netstandard2.0 and ships as two files,
    /// so a small self-contained implementation beats pulling in a serializer plus
    /// its dependency chain for a settings file this simple.
    /// </summary>
    public abstract class JsonNode
    {
        public static JsonNode Parse(string text)
        {
            var parser = new JsonParser(text ?? string.Empty);
            JsonNode value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw new JsonException("Unexpected trailing content at position " + parser.Position + ".");
            }

            return value;
        }

        public string ToJson()
        {
            var builder = new StringBuilder();
            Write(builder, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        internal abstract void Write(StringBuilder builder, int indent);

        internal static void Indent(StringBuilder builder, int level)
        {
            builder.Append(' ', level * 2);
        }
    }

    public sealed class JsonObject : JsonNode
    {
        private readonly List<KeyValuePair<string, JsonNode>> _members =
            new List<KeyValuePair<string, JsonNode>>();

        /// <summary>Members in insertion order, so the saved file keeps a stable shape.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonNode>> Members => _members;

        public JsonObject Add(string name, JsonNode value)
        {
            _members.Add(new KeyValuePair<string, JsonNode>(name, value ?? JsonNull.Instance));
            return this;
        }

        public JsonObject Add(string name, string? value)
        {
            return Add(name, value == null ? JsonNull.Instance : new JsonString(value));
        }

        public JsonObject Add(string name, bool value)
        {
            return Add(name, new JsonBoolean(value));
        }

        public JsonObject Add(string name, int value)
        {
            return Add(name, new JsonNumber(value));
        }

        public JsonObject Add(string name, IEnumerable<string> values)
        {
            var array = new JsonArray();
            foreach (string value in values)
            {
                array.Add(new JsonString(value));
            }

            return Add(name, array);
        }

        public JsonNode? this[string name]
        {
            get
            {
                foreach (KeyValuePair<string, JsonNode> member in _members)
                {
                    if (string.Equals(member.Key, name, StringComparison.Ordinal))
                    {
                        return member.Value;
                    }
                }

                return null;
            }
        }

        public string? GetString(string name)
        {
            return (this[name] as JsonString)?.Value;
        }

        public int GetInt(string name, int fallback)
        {
            JsonNumber? number = this[name] as JsonNumber;
            return number == null ? fallback : (int)number.Value;
        }

        public JsonArray? GetArray(string name)
        {
            return this[name] as JsonArray;
        }

        internal override void Write(StringBuilder builder, int indent)
        {
            if (_members.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append("{\n");
            for (int i = 0; i < _members.Count; i++)
            {
                Indent(builder, indent + 1);
                JsonString.WriteEscaped(builder, _members[i].Key);
                builder.Append(": ");
                _members[i].Value.Write(builder, indent + 1);
                if (i < _members.Count - 1)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            Indent(builder, indent);
            builder.Append('}');
        }
    }

    public sealed class JsonArray : JsonNode
    {
        private readonly List<JsonNode> _items = new List<JsonNode>();

        public IReadOnlyList<JsonNode> Items => _items;

        public JsonArray Add(JsonNode value)
        {
            _items.Add(value ?? JsonNull.Instance);
            return this;
        }

        internal override void Write(StringBuilder builder, int indent)
        {
            if (_items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(builder, indent + 1);
                _items[i].Write(builder, indent + 1);
                if (i < _items.Count - 1)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            Indent(builder, indent);
            builder.Append(']');
        }
    }

    public sealed class JsonString : JsonNode
    {
        public JsonString(string value)
        {
            Value = value;
        }

        public string Value { get; }

        internal override void Write(StringBuilder builder, int indent)
        {
            WriteEscaped(builder, Value);
        }

        /// <summary>
        /// Escapes quotes, backslashes and control characters. Backslashes matter here:
        /// every Windows path in the settings file contains them.
        /// </summary>
        internal static void WriteEscaped(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }

    public sealed class JsonNumber : JsonNode
    {
        public JsonNumber(double value)
        {
            Value = value;
        }

        public double Value { get; }

        internal override void Write(StringBuilder builder, int indent)
        {
            if (Math.Abs(Value % 1) < double.Epsilon)
            {
                builder.Append(((long)Value).ToString(CultureInfo.InvariantCulture));
                return;
            }

            builder.Append(Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public sealed class JsonBoolean : JsonNode
    {
        public JsonBoolean(bool value)
        {
            Value = value;
        }

        public bool Value { get; }

        internal override void Write(StringBuilder builder, int indent)
        {
            builder.Append(Value ? "true" : "false");
        }
    }

    public sealed class JsonNull : JsonNode
    {
        public static readonly JsonNull Instance = new JsonNull();

        private JsonNull()
        {
        }

        internal override void Write(StringBuilder builder, int indent)
        {
            builder.Append("null");
        }
    }

    /// <summary>Recursive-descent parser for the JSON subset the settings file uses.</summary>
    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _index;

        public JsonParser(string text)
        {
            _text = text;
        }

        public bool AtEnd => _index >= _text.Length;

        public int Position => _index;

        public void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        public JsonNode ParseValue()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw new JsonException("Unexpected end of input.");
            }

            char c = _text[_index];
            switch (c)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return new JsonString(ParseString());
                case 't':
                    Expect("true");
                    return new JsonBoolean(true);
                case 'f':
                    Expect("false");
                    return new JsonBoolean(false);
                case 'n':
                    Expect("null");
                    return JsonNull.Instance;
                default:
                    return ParseNumber();
            }
        }

        private JsonObject ParseObject()
        {
            var result = new JsonObject();
            _index++; // '{'
            SkipWhitespace();
            if (!AtEnd && _text[_index] == '}')
            {
                _index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _text[_index] != '"')
                {
                    throw new JsonException("Expected a member name at position " + _index + ".");
                }

                string name = ParseString();
                SkipWhitespace();
                if (AtEnd || _text[_index] != ':')
                {
                    throw new JsonException("Expected ':' at position " + _index + ".");
                }

                _index++;
                result.Add(name, ParseValue());
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new JsonException("Unterminated object.");
                }

                if (_text[_index] == ',')
                {
                    _index++;
                    continue;
                }

                if (_text[_index] == '}')
                {
                    _index++;
                    return result;
                }

                throw new JsonException("Expected ',' or '}' at position " + _index + ".");
            }
        }

        private JsonArray ParseArray()
        {
            var result = new JsonArray();
            _index++; // '['
            SkipWhitespace();
            if (!AtEnd && _text[_index] == ']')
            {
                _index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue());
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new JsonException("Unterminated array.");
                }

                if (_text[_index] == ',')
                {
                    _index++;
                    continue;
                }

                if (_text[_index] == ']')
                {
                    _index++;
                    return result;
                }

                throw new JsonException("Expected ',' or ']' at position " + _index + ".");
            }
        }

        private string ParseString()
        {
            _index++; // opening quote
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw new JsonException("Unterminated string.");
                }

                char c = _text[_index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw new JsonException("Unterminated escape sequence.");
                }

                char escape = _text[_index++];
                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (_index + 4 > _text.Length)
                        {
                            throw new JsonException("Truncated \\u escape.");
                        }

                        string hex = _text.Substring(_index, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            throw new JsonException("Invalid \\u escape: " + hex);
                        }

                        builder.Append((char)code);
                        _index += 4;
                        break;
                    default:
                        throw new JsonException("Invalid escape '\\" + escape + "'.");
                }
            }
        }

        private JsonNumber ParseNumber()
        {
            int start = _index;
            while (_index < _text.Length &&
                   ("+-0123456789.eE".IndexOf(_text[_index]) >= 0))
            {
                _index++;
            }

            string text = _text.Substring(start, _index - start);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new JsonException("Invalid number '" + text + "' at position " + start + ".");
            }

            return new JsonNumber(value);
        }

        private void Expect(string literal)
        {
            if (_index + literal.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
            {
                throw new JsonException("Expected '" + literal + "' at position " + _index + ".");
            }

            _index += literal.Length;
        }
    }
}
