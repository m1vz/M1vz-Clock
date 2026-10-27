using System.Text;

namespace M1vClock.Games.Library;

public sealed class VdfNode
{
    private readonly List<VdfNode> _children = [];

    public VdfNode(string name, string? value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string? Value { get; }

    public bool IsObject => Value is null;

    public IReadOnlyList<VdfNode> Children => _children;

    public VdfNode? Child(string name) =>
        _children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? Text(string name) => Child(name)?.Value;

    internal void Add(VdfNode child) => _children.Add(child);

    internal void Set(string name, string value)
    {
        var index = _children.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        var node = new VdfNode(index >= 0 ? _children[index].Name : name, value);
        if (index >= 0)
        {
            _children[index] = node;
        }
        else
        {
            _children.Add(node);
        }
    }

    internal bool Remove(string name) =>
        _children.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    internal VdfNode GetOrAddObject(string name)
    {
        if (Child(name) is { IsObject: true } existing)
        {
            return existing;
        }

        if (Child(name) is not null)
        {
            throw new FormatException($"\"{name}\" deveria ser um grupo.");
        }

        var node = new VdfNode(name, null);
        _children.Add(node);
        return node;
    }
}

public static class Vdf
{
    private const int MaxDepth = 64;

    public static VdfNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var root = new VdfNode(string.Empty, null);
        ReadMembers(reader, root, 0, topLevel: true);
        return root.Children.Count == 1 ? root.Children[0] : root;
    }

    public static string Serialize(VdfNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var text = new StringBuilder();
        if (root.Name.Length == 0)
        {
            foreach (var child in root.Children)
            {
                Write(text, child, 0);
            }
        }
        else
        {
            Write(text, root, 0);
        }

        return text.ToString();
    }

    private static void Write(StringBuilder text, VdfNode node, int depth)
    {
        var indent = new string('\t', depth);
        if (!node.IsObject)
        {
            text.Append(indent).Append('"').Append(Escape(node.Name)).Append("\"\t\t\"").Append(Escape(node.Value!)).Append("\"\n");
            return;
        }

        text.Append(indent).Append('"').Append(Escape(node.Name)).Append("\"\n").Append(indent).Append("{\n");
        foreach (var child in node.Children)
        {
            Write(text, child, depth + 1);
        }

        text.Append(indent).Append("}\n");
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static void ReadMembers(Reader reader, VdfNode parent, int depth, bool topLevel)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("Arquivo VDF aninhado demais.");
        }

        while (true)
        {
            var token = reader.Next();
            if (token.Kind == TokenKind.End)
            {
                if (!topLevel)
                {
                    throw new FormatException("Arquivo VDF terminou antes de fechar uma chave.");
                }

                return;
            }

            if (token.Kind == TokenKind.Close)
            {
                if (topLevel)
                {
                    throw new FormatException("Chave fechada sem ter sido aberta.");
                }

                return;
            }

            if (token.Kind != TokenKind.Text)
            {
                throw new FormatException("Esperava um nome entre aspas.");
            }

            var name = token.Text;
            var next = reader.Next();
            switch (next.Kind)
            {
                case TokenKind.Text:
                    parent.Add(new VdfNode(name, next.Text));
                    break;
                case TokenKind.Open:
                    var node = new VdfNode(name, null);
                    ReadMembers(reader, node, depth + 1, topLevel: false);
                    parent.Add(node);
                    break;
                default:
                    throw new FormatException($"O nome \"{name}\" ficou sem valor.");
            }
        }
    }

    private enum TokenKind
    {
        Text,
        Open,
        Close,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Reader(string source)
    {
        private int _position;

        public Token Next()
        {
            SkipBlanksAndComments();
            if (_position >= source.Length)
            {
                return new Token(TokenKind.End, string.Empty);
            }

            var c = source[_position];
            if (c == '{')
            {
                _position++;
                return new Token(TokenKind.Open, string.Empty);
            }

            if (c == '}')
            {
                _position++;
                return new Token(TokenKind.Close, string.Empty);
            }

            return c == '"' ? new Token(TokenKind.Text, Quoted()) : new Token(TokenKind.Text, Bare());
        }

        private void SkipBlanksAndComments()
        {
            while (_position < source.Length)
            {
                var c = source[_position];
                if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < source.Length && source[_position + 1] == '/')
                {
                    while (_position < source.Length && source[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string Quoted()
        {
            _position++;
            var text = new StringBuilder();
            while (_position < source.Length)
            {
                var c = source[_position++];
                if (c == '"')
                {
                    return text.ToString();
                }

                if (c == '\\' && _position < source.Length)
                {
                    var escaped = source[_position++];
                    text.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        _ => escaped,
                    });
                    continue;
                }

                text.Append(c);
            }

            throw new FormatException("Texto entre aspas sem fechar.");
        }

        private string Bare()
        {
            var start = _position;
            while (_position < source.Length && !char.IsWhiteSpace(source[_position]) && source[_position] is not ('{' or '}' or '"'))
            {
                _position++;
            }

            return source[start.._position];
        }
    }
}
