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
    /// Writes <paramref name="text"/>, which may span several lines. An expression builder returns a string, so a
    /// wide one can only be broken up by putting newlines in that string; every line it contains is written at the
    /// block's indentation, and whatever relative indentation the expression carries is added on top of it.
    /// </summary>
    public void Line(string text)
    {
        text = SplitEmbedded(text);
        Count(text);
        WriteLines(text);
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
        Open(signature);
        return new MemberCloser(this, signature, start, indent, _builder.Length);
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
        Count("{");
        OpenBrace(string.Empty);
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
        OpenBrace(header);
    }

    private void OpenBrace(string header)
    {
        var braceStart = _builder.Length;
        WriteLines("{");
        _frames.Add(new Frame(header, braceStart, _builder.Length - braceStart));
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

    private sealed class Frame(string header, int braceStart, int braceLength)
    {
        public Kind Kind { get; } = HeaderKind(header);
        public int BraceStart { get; } = braceStart;
        public int BraceLength { get; } = braceLength;
        public int Statements { get; set; }

        /// <summary>The first line of the first statement.</summary>
        public string? First { get; set; }

        /// <summary>The last statement ends with an <c>if</c> that has no <c>else</c>.</summary>
        public bool EndsWithOpenIf { get; set; }
    }

    private enum Kind { None, If, Else, Loop }

    /// <summary>Counts a statement written directly in the innermost block, when that block could drop its braces.</summary>
    private void Count(string text)
    {
        if (_frames.Count == 0 || _frames[^1].Kind == Kind.None)
            return;

        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;

        if (start == text.Length ||
            text[start] == '#' || 
            string.CompareOrdinal(text, start, "//", 0, 2) == 0)
            return;

        var frame = _frames[^1];
        frame.Statements += Math.Max(1, TopLevelStatements(text));
        frame.First ??= FirstLine(text).Trim();
        frame.EndsWithOpenIf = CheckEndsWithOpenIf(text);
    }

    /// <summary>
    /// Whether a block can drop its braces: it belongs to an <c>if</c>, <c>else</c>, <c>for</c>, <c>foreach</c> or
    /// <c>while</c> and holds one statement that C# accepts as an embedded statement. A declaration is not one, and a
    /// statement with a block of its own (<c>try</c>, <c>switch</c>, <c>unchecked</c>) keeps the braces around it
    /// for readability. Under an <c>if</c> or <c>else</c> the statement must not end with an <c>if</c> lacking an
    /// <c>else</c>, which would take the outer <c>else</c>; nor may an <c>else</c> hold a lone <c>if</c>, which
    /// reads as an <c>else if</c>.
    /// </summary>
    private static bool Embeddable(Frame frame)
    {
        if (frame.Kind == Kind.None || frame.Statements != 1 || frame.First is not { } first)
            return false;

        if (first[0] == '{' || 
            StartsWithWord(first, 0, "try", "switch", "unchecked", "checked", "do", "using", "lock", "fixed") || 
            IsDeclaration(first))
            return false;

        return frame.Kind == Kind.Loop || 
               !frame.EndsWithOpenIf && !(frame.Kind == Kind.Else && StartsWithWord(first, 0, "if"));
    }

    private static Kind HeaderKind(string header)
    {
        if (StartsWithWord(header, 0, "if"))
            return Kind.If;

        if (StartsWithWord(header, 0, "else"))
            return StartsWithWord(header, SkipSpaces(header, 4), "if") ? Kind.If : Kind.Else;

        return StartsWithWord(header, 0, "for", "foreach", "while") ? Kind.Loop : Kind.None;
    }

    /// <summary>A local declaration, which C# does not accept as the whole body of a control statement (CS1023).</summary>
    private static readonly System.Text.RegularExpressions.Regex Declaration = new(
        @"^(?!(?:return|throw|yield|goto|await|break|continue|new|else)\b)(?:const\s+|ref\s+(?:readonly\s+)?|scoped\s+)?" +
        @"(?:\([^()]*\)|(?:global::)?[A-Za-z_][\w.]*(?:<[^;=()]*>)?)(?:\[[,\s]*\])*\??\s+@?[A-Za-z_]\w*\s*(?:=(?![=>])|;)");

    private static bool IsDeclaration(string line) => Declaration.IsMatch(line);

    /// <summary>
    /// <c>if (c) statement;</c> becomes the header, then the statement on the next line one level in; likewise
    /// <c>else</c>, <c>for</c>, <c>foreach</c> and <c>while</c>, and again for the statement it holds. A header
    /// followed by a block, or by more than one statement, is left as written.
    /// </summary>
    private static string SplitEmbedded(string text)
    {
        var headerEnd = EmbeddingHeaderEnd(text);
        if (headerEnd < 0)
            return text;

        var rest = text[headerEnd..].TrimStart(' ');
        if (rest.Length == 0 || rest[0] is '{' or ';' or '\n' || TopLevelStatements(rest) != 1)
            return text;

        return $"{text[..headerEnd]}\n{IndentLines(SplitEmbedded(rest))}";
    }

    /// <summary>Whether a statement written as text ends with an <c>if</c> that has no <c>else</c>.</summary>
    private static bool CheckEndsWithOpenIf(string text)
    {
        var headerEnd = EmbeddingHeaderEnd(text);
        if (headerEnd < 0)
            return false;

        return HeaderKind(text) == Kind.If || 
               CheckEndsWithOpenIf(text[headerEnd..].TrimStart(' ', '\n'));
    }

    /// <summary>
    /// The index just past the header of a statement that embeds another: past the parenthesised condition of
    /// <c>if</c>, <c>else if</c>, <c>for</c>, <c>foreach</c> or <c>while</c>, or past a plain <c>else</c>; -1 for
    /// any other statement.
    /// </summary>
    private static int EmbeddingHeaderEnd(string text)
    {
        var i = 0;
        if (StartsWithWord(text, 0, "else"))
        {
            i = SkipSpaces(text, 4);
            if (!StartsWithWord(text, i, "if"))
                return 4;
        }

        foreach (var keyword in ConditionKeywords)
        {
            if (!StartsWithWord(text, i, keyword))
                continue;

            var open = SkipSpaces(text, i + keyword.Length);
            if (open >= text.Length || text[open] != '(')
                return -1;

            var close = Scan(text, open, stopAtClose: true);
            return close < 0 ? -1 : close + 1;
        }

        return -1;
    }

    private static readonly string[] ConditionKeywords = ["if", "foreach", "for", "while"];

    /// <summary>The semicolons outside every bracket and literal: one per statement the text holds.</summary>
    private static int TopLevelStatements(string text) => Scan(text, 0, stopAtClose: false);

    /// <summary>
    /// Walks <paramref name="text"/> from <paramref name="start"/>, skipping string and character literals. With
    /// <paramref name="stopAtClose"/> it returns the index of the bracket closing the one at <paramref name="start"/>
    /// (-1 if none); otherwise the number of semicolons at depth zero.
    /// </summary>
    private static int Scan(string text, int start, bool stopAtClose)
    {
        var depth = 0;
        var semicolons = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '"' or '\'':
                    var verbatim = c == '"' && i > 0 && text[i - 1] == '@';
                    for (i++; i < text.Length; i++)
                    {
                        if (!verbatim && text[i] == '\\')
                            i++;
                        else if (text[i] == c && !(verbatim && i + 1 < text.Length && text[i + 1] == '"' && ++i > 0))
                            break;
                    }

                    break;
                
                case '(' or '[' or '{':
                    depth++;
                    break;
                
                case ')' or ']' or '}':
                    depth--;
                    if (stopAtClose && depth == 0)
                        return i;
                    break;
                
                case ';' when depth == 0:
                    semicolons++;
                    break;
            }
        }

        return stopAtClose ? -1 : semicolons;
    }

    /// <summary>Whether one of <paramref name="words"/> is at <paramref name="index"/> as a whole word. A loop, not LINQ: this runs several times per generated line.</summary>
    private static bool StartsWithWord(string text, int index, params string[] words)
    {
        foreach (var word in words)
        {
            var end = index + word.Length;
            if (end <= text.Length && 
                string.CompareOrdinal(text, index, word, 0, word.Length) == 0 &&
                (end == text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_')))
                return true;
        }

        return false;
    }

    private static int SkipSpaces(string text, int index)
    {
        while (index < text.Length && text[index] == ' ')
            index++;

        return index;
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        return newline < 0 ? text : text[..newline];
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
