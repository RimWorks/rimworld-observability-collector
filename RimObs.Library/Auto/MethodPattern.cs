using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Auto;

/// <summary>
/// One filter line in <c>Assembly!Type.Full.Name::Method</c> form. <c>*</c> and <c>?</c> glob.
/// A missing assembly or method part means "any". A leading <c>!</c> turns the line into an
/// exclusion, which beats every include no matter where it sits in the list.
/// </summary>
internal sealed class MethodPattern {
    private const string Any = "*";

    private readonly string _assembly;
    private readonly string _type;
    private readonly string _method;
    private readonly bool _negated;

    private MethodPattern(string assembly, string type, string method, bool negated) {
        _assembly = assembly;
        _type = type;
        _method = method;
        _negated = negated;
    }

    public string Assembly => _assembly;

    public string Type => _type;

    public string Method => _method;

    public bool Negated => _negated;

    public static MethodPattern? Parse(string? line, bool negate = false) {
        if (line is null)
            return null;

        string text = line.Trim();
        if (text.Length == 0 || text[0] == '#')
            return null;

        if (text[0] == '!') {
            negate = true;
            text = text.Substring(1).Trim();
            if (text.Length == 0)
                return null;
        }

        string assembly = Any;
        int bang = text.IndexOf('!');
        if (bang >= 0) {
            assembly = Part(text.Substring(0, bang));
            text = text.Substring(bang + 1);
        }

        string method = Any;
        int colons = text.IndexOf("::", StringComparison.Ordinal);
        if (colons >= 0) {
            method = Part(text.Substring(colons + 2));
            text = text.Substring(0, colons);
        }

        string type = Part(text);
        if (assembly == Any && type == Any && method == Any)
            return null;

        return new MethodPattern(assembly, type, method, negate);
    }

    /// <summary>
    /// Parses a whole settings box. Blank lines and <c>#</c> comments are dropped.
    /// <paramref name="negate"/> makes every line an exclusion, which is how the ignore box
    /// works without asking the user to type <c>!</c> on each line.
    /// </summary>
    public static MethodPattern[] ParseAll(string? text, bool negate = false) {
        if (text is null || text.Length == 0)
            return [];

        string[] lines = text.Split('\n');
        List<MethodPattern> parsed = new List<MethodPattern>(lines.Length);
        for (int i = 0; i < lines.Length; i++) {
            MethodPattern? pattern = Parse(lines[i], negate);
            if (pattern is not null)
                parsed.Add(pattern);
        }
        return parsed.ToArray();
    }

    public bool MatchesAssembly(string assemblyName) => Glob(_assembly, assemblyName);

    public bool MatchesType(string typeFullName) => Glob(_type, typeFullName);

    public bool MatchesMethod(string methodName) => Glob(_method, methodName);

    public static bool AnyAssembly(MethodPattern[] patterns, string assemblyName) {
        for (int i = 0; i < patterns.Length; i++) {
            if (patterns[i].MatchesAssembly(assemblyName))
                return true;
        }
        return false;
    }

    /// <summary>Splits a mixed list into the lines that include and the lines that exclude.</summary>
    public static void Split(MethodPattern[] patterns, out MethodPattern[] includes, out MethodPattern[] excludes) {
        List<MethodPattern> inc = new List<MethodPattern>(patterns.Length);
        List<MethodPattern> exc = new List<MethodPattern>(patterns.Length);
        for (int i = 0; i < patterns.Length; i++) {
            if (patterns[i].Negated)
                exc.Add(patterns[i]);
            else
                inc.Add(patterns[i]);
        }
        includes = inc.ToArray();
        excludes = exc.ToArray();
    }

    /// <summary>Case-insensitive glob over <c>*</c> and <c>?</c>, iterative with one backtrack point.</summary>
    public static bool Glob(string pattern, string? value) {
        if (value is null)
            return false;

        int p = 0;
        int v = 0;
        int star = -1;
        int mark = 0;

        while (v < value.Length) {
            if (p < pattern.Length && (pattern[p] == '?' || SameChar(pattern[p], value[v]))) {
                p++;
                v++;
            }
            else if (p < pattern.Length && pattern[p] == '*') {
                star = p++;
                mark = v;
            }
            else if (star >= 0) {
                p = star + 1;
                v = ++mark;
            }
            else {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }

    private static bool SameChar(char a, char b) =>
        a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    private static string Part(string raw) {
        string trimmed = raw.Trim();
        return trimmed.Length == 0 ? Any : trimmed;
    }
}
