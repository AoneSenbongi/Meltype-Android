// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>The only permitted spelling change at a model-number boundary.</summary>
public sealed record ModelNumberBoundary(string Model, string RawSuffix, string KanaSuffix)
{
    public bool IsValid() => Model.Length is > 0 and <= 32 &&
        Regex.IsMatch(Model, @"\A(?:[A-Za-z]{1,2}[0-9]+){2,}[A-Za-z]*\z") &&
        RawSuffix.Length is >= 2 and <= 3 && RawSuffix.All(char.IsAsciiLetterLower) &&
        KanaSuffix.Length is > 0 and <= 2 && KanaSuffix.All(c => c is >= 'ぁ' and <= 'ゖ') &&
        new RomajiDetector().ConvertLenient(RawSuffix, true) == KanaSuffix;

    internal static ModelBoundaryProposal? Find(CompositionText text, string baseline, string left, string right)
    {
        if (text.ProtectedSpans().Count > 0) return null;
        var segments = text.Segments(false);
        ModelBoundaryProposal? proposal = null;
        var rawOffset = 0;
        for (var index = 0; index + 1 < segments.Count; index++)
        {
            var segment = segments[index];
            rawOffset += segment.Raw.Length;
            if (!segment.IsEnglish || segment.IsProtected || segments[index + 1].IsEnglish) continue;
            var unitOffset = 0;
            CompositionUnit? suffix = null;
            foreach (var unit in text.Units)
            {
                if (unitOffset == rawOffset) { suffix = unit; break; }
                unitOffset += unit.Raw.Length;
            }
            if (suffix is not { } reading) continue;
            var boundary = new ModelNumberBoundary(segment.Raw, reading.Raw, reading.Kana);
            if (!boundary.IsValid()) continue;
            var original = boundary.Model + boundary.KanaSuffix;
            var offset = baseline.IndexOf(original, StringComparison.Ordinal);
            if (offset < 0 || baseline.IndexOf(original, offset + 1, StringComparison.Ordinal) >= 0) continue;
            // A second ambiguous token needs a separate interaction; do not guess which one to change.
            if (proposal is not null) return null;
            var prefix = baseline[..offset];
            var tail = baseline[(offset + original.Length)..];
            if (left.Length + right.Length + prefix.Length + tail.Length == 0) continue;
            proposal = new(new ContextRequest("", left + prefix, tail + right,
                [original, boundary.Model + boundary.RawSuffix], ModelBoundary: boundary), prefix, tail);
        }
        return proposal;
    }
}

internal sealed record ModelBoundaryProposal(ContextRequest Request, string Prefix, string Suffix);
