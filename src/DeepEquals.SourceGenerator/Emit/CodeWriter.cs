// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Text;

namespace DeepEquals.SourceGenerator.Emit;

/// <summary>A StringBuilder with indentation, sized up front; the only place generated text is produced.</summary>
internal sealed class CodeWriter
{
    private const string IndentUnit = "    ";

    private readonly StringBuilder _builder;
    private int _indent;
    private bool _atLineStart = true;

    /// <summary>The span and expression of the last <see cref="Return"/> written.</summary>
    private (int Start, int End, string Expression)? _lastReturn;

    public CodeWriter(int capacity)
    {
        _builder = new StringBuilder(capacity);
    }

    /// <summary>A writer that starts <paramref name="indent"/> levels in, for a body written before the blocks around it.</summary>
    public CodeWriter(int capacity, int indent)
        : this(capacity)
    {
        _indent = indent;
    }

    /// <summary>Appends text that already carries its own indentation and line breaks, such as another writer's output.</summary>
    public void AppendRaw(string text)
    {
        _builder.Append(text);
        _atLineStart = text.Length == 0 || text[text.Length - 1] == '\n';
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

            if (newline < 0) return;

            start = newline + 1;
        }
    }

    /// <summary>
    /// A return statement. An expression that starts on its own line, such as a multi-line <see cref="ItemList"/>
    /// with an empty opening, leaves <c>return</c> alone on the first. Where it starts and ends is kept, so a
    /// <see cref="Member"/> closing right after it can tell that it was the whole body.
    /// </summary>
    public void Return(string expression)
    {
        var start = _builder.Length;
        Line($"return{Spaced(expression)};");
        _lastReturn = (start, _builder.Length, expression);
    }

    /// <summary>An expression-bodied member: the signature, the arrow, the body, the arrow on the signature's line.</summary>
    public void Arrow(string signature, string body) => Line($"{signature} =>{Spaced(body)};");

    /// <summary>The expression after a keyword or arrow: one space, unless it starts on the next line.</summary>
    private static string Spaced(string expression) => expression.Length > 0 && expression[0] == '\n' ? expression : " " + expression;

    /// <summary>
    /// A method or accessor body, closed when the returned scope is disposed. A body that turns out to be a single
    /// <see cref="Return"/> is rewritten as an expression body, <c>signature => expression;</c>: whether it is only
    /// that often depends on guards and preludes that write nothing for most types, so the choice is made from what
    /// was written rather than predicted at each call site.
    /// </summary>
    public IDisposable Member(string signature)
    {
        var start = _builder.Length;
        var indent = _indent;
        Open(signature);
        return new MemberCloser(this, signature, start, indent, _builder.Length);
    }

    private void CloseMember(string signature, int start, int indent, int bodyStart)
    {
        if (_lastReturn is { } last && last.Start == bodyStart && last.End == _builder.Length)
        {
            Truncate(start);
            _indent = indent;
            Arrow(signature, last.Expression);
            return;
        }

        Close();
    }

    /// <summary>
    /// <paramref name="open"/>, then one item per line indented a further level, each but the last followed by
    /// <paramref name="separator"/> and the last by <paramref name="close"/>. A member is the unit a reader scans
    /// for, so a list of them reads as a column rather than as one line that wraps wherever the editor happens to
    /// be wide, and a business object with thirty properties is the case that matters. Indenting rather than
    /// padding to the opening text keeps the column in the same place whatever opened it, and keeps a long prefix
    /// from pushing every item to the right. A single item stays on the line it started on.
    ///
    /// The result carries relative indentation only. It is equally usable as a whole statement and as one item of
    /// an enclosing list, because <see cref="Line(string)"/> adds the block indentation to every line it is given.
    /// </summary>
    public static string ItemList(string open, List<string> items, string separator, string close)
    {
        if (items.Count == 1) return open + items[0] + close;

        var text = new StringBuilder(open);
        for (var i = 0; i < items.Count; i++) text.Append('\n').Append(IndentLines(items[i])).Append(i == items.Count - 1 ? close : separator);

        return text.ToString();
    }

    /// <summary>One level in on every line, so a nested list sits inside the list containing it.</summary>
    private static string IndentLines(string text) => IndentUnit + text.Replace("\n", "\n" + IndentUnit);

    /// <summary>
    /// A braced block, closed when the returned scope is disposed. The emitter's own nesting then mirrors the
    /// generated code's, and the compiler rather than convention keeps the braces balanced.
    /// </summary>
    public IDisposable Block(string header)
    {
        Open(header);
        return new Closer(this);
    }

    /// <summary>A bare braced block, for a switch section that declares locals.</summary>
    public IDisposable Block()
    {
        Line("{");
        _indent++;
        return new Closer(this);
    }

    /// <summary>A counted loop over <c>i</c> from zero to <paramref name="limit"/>, the shape every element walk takes.</summary>
    public IDisposable For(string limit) => Block($"for (int i = 0; i < {limit}; i++)");

    /// <summary>
    /// Opens a block that a later <see cref="Close"/> ends. For the file skeleton only, whose namespace and
    /// containing types are a run of blocks whose number is not known until the model is read.
    /// </summary>
    public void Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
    }

    public void Close()
    {
        _indent--;
        Line("}");
    }

    /// <summary>The number of characters written so far; a caller compares two readings to learn whether anything was written between them.</summary>
    public int Length => _builder.Length;

    /// <summary>Drops everything written after <paramref name="length"/>.</summary>
    public void Truncate(int length)
    {
        _builder.Length = length;
        _atLineStart = length == 0 || _builder[length - 1] == '\n';
        _lastReturn = null;
    }

    public override string ToString() => _builder.ToString();

    private void Write(string text)
    {
        if (_atLineStart && text.Length > 0)
        {
            for (var i = 0; i < _indent; i++) _builder.Append(IndentUnit);
            _atLineStart = false;
        }

        _builder.Append(text);
    }

    private sealed class Closer : IDisposable
    {
        private readonly CodeWriter _writer;

        public Closer(CodeWriter writer)
        {
            _writer = writer;
        }

        public void Dispose() => _writer.Close();
    }

    private sealed class MemberCloser : IDisposable
    {
        private readonly CodeWriter _writer;
        private readonly string _signature;
        private readonly int _start;
        private readonly int _indent;
        private readonly int _bodyStart;

        public MemberCloser(CodeWriter writer, string signature, int start, int indent, int bodyStart)
        {
            _writer = writer;
            _signature = signature;
            _start = start;
            _indent = indent;
            _bodyStart = bodyStart;
        }

        public void Dispose() => _writer.CloseMember(_signature, _start, _indent, _bodyStart);
    }
}
