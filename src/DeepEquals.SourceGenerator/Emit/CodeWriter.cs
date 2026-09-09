using System;
using System.Text;

namespace DeepEquals.SourceGenerator.Emit;

/// <summary>A StringBuilder with indentation, sized up front; the only place generated text is produced.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder;
    private int _indent;
    private bool _atLineStart = true;

    public CodeWriter(int capacity)
    {
        _builder = new StringBuilder(capacity);
    }

    public void Line()
    {
        _builder.Append('\n');
        _atLineStart = true;
    }

    /// <summary>
    /// Writes <paramref name="text"/>, which may span several lines. An expression builder returns a string, so a
    /// wide one can only be broken up by putting newlines in that string; every line it contains is written at the
    /// block's indentation, and whatever relative indentation the expression carries is added on top of it.
    /// </summary>
    public void Line(string text)
    {
        int start = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', start);
            Write(newline < 0 ? text.Substring(start) : text.Substring(start, newline - start));
            Line();

            if (newline < 0)
            {
                return;
            }

            start = newline + 1;
        }
    }

    public void Write(string text)
    {
        if (_atLineStart && text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _atLineStart = false;
        }

        _builder.Append(text);
    }

    /// <summary>Indents without opening a brace, for the continuation lines of one statement.</summary>
    public void Indent() => _indent++;

    public void Unindent() => _indent--;

    public void Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
    }

    public void Open()
    {
        Line("{");
        _indent++;
    }

    public void Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
    }

    public IDisposable Block(string header)
    {
        Open(header);
        return new Closer(this);
    }

    public override string ToString() => _builder.ToString();

    private sealed class Closer : IDisposable
    {
        private readonly CodeWriter _writer;

        public Closer(CodeWriter writer)
        {
            _writer = writer;
        }

        public void Dispose() => _writer.Close();
    }
}
