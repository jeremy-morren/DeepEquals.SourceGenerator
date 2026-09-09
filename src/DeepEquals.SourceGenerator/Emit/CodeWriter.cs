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

    public void Line(string text)
    {
        Write(text);
        Line();
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
