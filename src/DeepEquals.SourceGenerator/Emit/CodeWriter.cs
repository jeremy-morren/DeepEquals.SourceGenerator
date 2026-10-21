// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
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

    /// <summary>Appends text that already carries its own indentation and line breaks, such as another writer's output.</summary>
    public void AppendRaw(string text)
    {
        NoteAliases(text);
        _builder.Append(text);
        _atLineStart = text.Length == 0 || text[^1] == '\n';
    }

    /// <summary>Which of <see cref="KnownTypes.FrameworkAliases"/> the text written so far names, by index.</summary>
    public bool[] UsedAliases { get; } = new bool[KnownTypes.FrameworkAliases.Length];

    /// <summary>Every alias starts with <c>_Deq</c>, so one search per text tells whether any of them is in it.</summary>
    private void NoteAliases(string text)
    {
        if (text.IndexOf("_Deq", StringComparison.Ordinal) < 0)
            return;

        for (var i = 0; i < UsedAliases.Length; i++)
            if (!UsedAliases[i] && text.IndexOf(KnownTypes.FrameworkAliases[i].Alias, StringComparison.Ordinal) >= 0)
                UsedAliases[i] = true;
    }

    /// <summary>The current block depth, as the number of indentation units a line written now would start with.</summary>
    public int Indent => _indent;

    /// <summary>Inserts text that carries its own indentation and line breaks at <paramref name="index"/>, once every block is closed.</summary>
    public void Insert(int index, string text)
    {
        _builder.Insert(index, text);
        _lastReturn = null;
    }

    public void Line()
    {
        _builder.Append('\n');
        _atLineStart = true;
    }

    /// <summary>
    /// Writes <paramref name="text"/>: one statement that embeds none, or any line outside a method body. It may span
    /// several lines. An expression builder returns a string, so a wide one can only be broken up by putting
    /// newlines in that string; every line it contains is written at the block's indentation, and whatever
    /// relative indentation the expression carries is added on top of it.
    /// </summary>
    public void Line(string text)
    {
        Count(Statement.Simple);
        WriteLines(text);
    }

    /// <summary>A local declaration, which C# does not accept as the whole body of a control statement (CS1023).</summary>
    public void Local(string declaration)
    {
        Count(Statement.Declaration);
        WriteLines(declaration);
    }

    private void WriteLines(string text)
    {
        var start = 0;
        while (true)
        {
            var newline = text.IndexOf('\n', start);
            Write(newline < 0 
                ? text[start..] 
                : text.Substring(start, newline - start));
            Line();

            if (newline < 0) 
                return;

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
    public void Arrow(string signature, string body) =>
        Line($"{signature} =>{Spaced(body)};");

    /// <summary>The expression after a keyword or arrow: one space, unless it starts on the next line.</summary>
    private static string Spaced(string expression) => 
        expression.Length > 0 && expression[0] == '\n' ? expression : " " + expression;

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
        Open(signature, Kind.None);
        var bodyStart = _builder.Length;
        return new Scope(() => CloseMember(signature, start, indent, bodyStart));
    }

    private void CloseMember(string signature, int start, int indent, int bodyStart)
    {
        if (_lastReturn is { } last && last.Start == bodyStart && last.End == _builder.Length)
        {
            _frames.RemoveAt(_frames.Count - 1);
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
        if (items.Count == 1) 
            return open + items[0] + close;

        var text = new StringBuilder(open);
        for (var i = 0; i < items.Count; i++) 
            text.Append('\n')
                .Append(IndentLines(items[i]))
                .Append(i == items.Count - 1 ? close : separator);

        return text.ToString();
    }

    /// <summary>One level in on every line, so a nested list sits inside the list containing it.</summary>
    private static string IndentLines(string text) => IndentUnit + text.Replace("\n", "\n" + IndentUnit);

    /// <summary>
    /// A braced block that keeps its braces, closed when the returned scope is disposed: a type or member, or a
    /// <c>try</c>, <c>finally</c>, <c>switch</c> or <c>using</c>. The emitter's own nesting then mirrors the
    /// generated code's, and the compiler rather than convention keeps the braces balanced.
    /// </summary>
    public IDisposable Block(string header)
    {
        Open(header, Kind.None);
        return new Scope(Close);
    }

    /// <summary>A <c>try</c> whose <c>finally</c> holds <paramref name="cleanup"/>, written when the returned scope is disposed.</summary>
    public IDisposable TryFinally(string cleanup)
    {
        var body = Block("try");
        return new Scope(() =>
        {
            body.Dispose();
            using (Block("finally"))
                Line(cleanup);
        });
    }

    /// <summary>A scope that writes nothing, for a caller whose block turns out not to be needed.</summary>
    public static IDisposable NoScope { get; } = new Scope(null);

    /// <summary>A bare braced block, for a switch section that declares locals.</summary>
    public IDisposable Block()
    {
        Count(Statement.Compound);
        OpenBrace(Kind.None);
        return new Scope(Close);
    }

    /// <summary>An <c>if</c> whose block loses its braces when it holds a single statement that can stand alone.</summary>
    public IDisposable If(string condition)
    {
        Open($"if ({condition})", Kind.If);
        return new Scope(Close);
    }

    /// <summary><c>if (condition)</c>, then <paramref name="statement"/> on the next line one level in.</summary>
    public void If(string condition, string statement)
    {
        using (If(condition))
            Line(statement);
    }

    /// <summary>The <c>else</c> of the <see cref="If(string)"/> closed just before it.</summary>
    public IDisposable Else()
    {
        Open("else", Kind.Else);
        return new Scope(Close);
    }

    /// <summary>A counted loop over <c>i</c> from zero to <paramref name="limit"/>, the shape every element walk takes.</summary>
    public IDisposable For(string limit)
    {
        Open($"for (int i = 0; i < {limit}; i++)", Kind.Loop);
        return new Scope(Close);
    }

    /// <summary><c>foreach (iteration)</c>, where <paramref name="iteration"/> is the <c>T e in source</c> part.</summary>
    public IDisposable Foreach(string iteration)
    {
        Open($"foreach ({iteration})", Kind.Loop);
        return new Scope(Close);
    }

    /// <summary><c>foreach (iteration)</c>, then <paramref name="statement"/> on the next line one level in.</summary>
    public void Foreach(string iteration, string statement)
    {
        using (Foreach(iteration))
            Line(statement);
    }

    public IDisposable While(string condition)
    {
        Open($"while ({condition})", Kind.Loop);
        return new Scope(Close);
    }

    /// <summary>
    /// Opens a block that a later <see cref="Close"/> ends. For the file skeleton only, whose namespace and
    /// containing types are a run of blocks whose number is not known until the model is read.
    /// </summary>
    public void Open(string header) => Open(header, Kind.None);

    private void Open(string header, Kind kind)
    {
        Count(kind switch
        {
            Kind.None => Statement.Compound,
            Kind.If => Statement.If,
            _ => Statement.Simple,
        });
        WriteLines(header);
        OpenBrace(kind);
    }

    private void OpenBrace(Kind kind)
    {
        var braceStart = _builder.Length;
        WriteLines("{");
        _frames.Add(new Frame(kind, braceStart, _builder.Length - braceStart));
        _indent++;
    }

    /// <summary>
    /// Ends the innermost block. A control statement's block that holds a single statement loses its braces: the
    /// statement is already written one level in, where an embedded statement sits.
    /// </summary>
    public void Close()
    {
        var frame = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        _indent--;

        var embedded = Embeddable(frame);
        if (embedded)
        {
            _builder.Remove(frame.BraceStart, frame.BraceLength);
            _lastReturn = null;
        }
        else
        {
            WriteLines("}");
        }

        // What the parent's statement now ends with: an if without an else would take an else written after it.
        if (_frames.Count > 0)
        {
            _frames[^1].EndsWithOpenIf = frame.Kind switch
            {
                Kind.If => true,
                Kind.Loop or Kind.Else => embedded && frame.EndsWithOpenIf,
                _ => false,
            };
        }
    }

    // ----- embedded statements ----------------------------------------------------------------------------------------

    /// <summary>The blocks open now, innermost last, each counting the statements written directly inside it.</summary>
    private readonly List<Frame> _frames = [];

    private sealed class Frame(Kind kind, int braceStart, int braceLength)
    {
        public Kind Kind { get; } = kind;
        public int BraceStart { get; } = braceStart;
        public int BraceLength { get; } = braceLength;
        public int Statements { get; set; }

        /// <summary>What the first statement is.</summary>
        public Statement? First { get; set; }

        /// <summary>The last statement ends with an <c>if</c> that has no <c>else</c>.</summary>
        public bool EndsWithOpenIf { get; set; }
    }

    /// <summary>What a block's header makes it: a control statement's block can drop its braces, any other cannot.</summary>
    private enum Kind { None, If, Else, Loop }

    /// <summary>What a statement is, as far as the control statement holding it is concerned.</summary>
    private enum Statement
    {
        /// <summary>Accepted as an embedded statement: an expression, jump, loop or <c>else</c>.</summary>
        Simple,

        /// <summary>An <c>if</c>, which an <c>else</c> holding it alone would read as an <c>else if</c>.</summary>
        If,

        /// <summary>A local declaration, which C# does not accept as an embedded statement (CS1023).</summary>
        Declaration,

        /// <summary>A statement with a block of its own that is not a control statement's, kept braced for readability.</summary>
        Compound,
    }

    /// <summary>Counts a statement written directly in the innermost block, when that block could drop its braces.</summary>
    private void Count(Statement statement)
    {
        if (_frames.Count == 0 || _frames[^1].Kind == Kind.None)
            return;

        var frame = _frames[^1];
        frame.Statements++;
        frame.First ??= statement;

        // An if ends open once its block closes; Close records that.
        frame.EndsWithOpenIf = false;
    }

    /// <summary>
    /// Whether a block can drop its braces: it belongs to an <c>if</c>, <c>else</c>, <c>for</c>, <c>foreach</c> or
    /// <c>while</c> and holds one statement that C# accepts as an embedded statement. Under an <c>if</c> or
    /// <c>else</c> the statement must not end with an <c>if</c> lacking an <c>else</c>, which would take the outer
    /// <c>else</c>; nor may an <c>else</c> hold a lone <c>if</c>, which reads as an <c>else if</c>.
    /// </summary>
    private static bool Embeddable(Frame frame)
    {
        if (frame.Kind == Kind.None || frame.Statements != 1 || frame.First is Statement.Declaration or Statement.Compound)
            return false;

        return frame.Kind == Kind.Loop || 
               !frame.EndsWithOpenIf && !(frame.Kind == Kind.Else && frame.First == Statement.If);
    }

    /// <summary>The number of characters written so far; a caller compares two readings to learn whether anything was written between them.</summary>
    public int Length => _builder.Length;

    /// <summary>The text so far ends with an empty line, so a separator would make two.</summary>
    public bool EndsWithBlankLine =>
        _builder.Length >= 2 && 
        _builder[^1] == '\n' && 
        _builder[^2] == '\n';

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
        NoteAliases(text);
        if (_atLineStart && text.Length > 0)
        {
            for (var i = 0; i < _indent; i++) 
                _builder.Append(IndentUnit);
            _atLineStart = false;
        }

        _builder.Append(text);
    }

    private sealed class Scope(Action? dispose) : IDisposable
    {
        public void Dispose() => dispose?.Invoke();
    }
}
