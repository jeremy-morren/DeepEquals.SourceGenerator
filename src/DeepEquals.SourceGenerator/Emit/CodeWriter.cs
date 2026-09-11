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

            if (newline < 0) return;

            start = newline + 1;
        }
    }

    public void Return(string expression) => Line($"return {expression};");

    /// <summary>An expression-bodied member: the signature, the arrow, the body.</summary>
    public void Arrow(string signature, string body) => Line($"{signature} => {body};");

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
}
