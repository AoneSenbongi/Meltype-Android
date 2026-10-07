// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Meltype.Composition;

/// <summary>Installed Google IME with a private session. Learning is explicit and verifies every selected clause.</summary>
public sealed class GoogleImeConverter : IKanjiConverter, ILearningConverter
{
    private readonly Func<byte[], byte[]> _transport;
    private readonly bool _learning;
    public Func<bool> LearningEnabled { get; init; } = () => true;
    private bool CanLearn => _learning && LearningEnabled();
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);

    public GoogleImeConverter(bool learning = false) : this(CallPipe, learning) { }
    internal GoogleImeConverter(Func<byte[], byte[]> transport, bool learning = false) { _transport = transport; _learning = learning; }

    public string? Convert(string hiragana) => ConvertClauses(hiragana) is { Count: > 0 } clauses
        ? string.Concat(clauses.Select(c => c.Text)) : null;

    public IReadOnlyList<string> Candidates(string reading)
    {
        lock (_gate)
        {
            if (_candidates.TryGetValue(reading, out var cached)) return cached;
            ConvertClauses(reading);
            return _candidates.GetValueOrDefault(reading) ?? [];
        }
    }

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        lock (_gate) return ConvertCore(hiragana, context);
    }

    private IReadOnlyList<ConversionClause>? ConvertCore(string hiragana, string? context)
    {
        if (hiragana.Length is 0 or > 100 || hiragana.Any(char.IsControl)) return null;
        ulong session = 0;
        _candidates.Clear();
        var deadline = Stopwatch.StartNew();
        Proto Call(int kind, byte[]? payload = null)
        {
            // No config, submit, shutdown, reset-history, or dictionary commands.
            if (kind is not (1 or 2 or 3 or 5 or 17)) throw new InvalidOperationException("Unsupported probe command");
            if (deadline.ElapsedMilliseconds > 2000 && kind != 2) throw new TimeoutException("Google conversion deadline");
            var request = Proto.Number(1, (ulong)kind)
                .Concat(session == 0 ? [] : Proto.Number(2, session)).Concat(payload ?? []).ToArray();
            var output = Proto.Parse(_transport(request));
            if (output.Int(11) != 0 || output.Has(4)) throw new InvalidOperationException("Google returned an error or unexpected commit");
            return output;
        }
        Proto Command(int kind, byte[]? extra = null) =>
            Call(5, Proto.Blob(4, Proto.Number(1, (ulong)kind).Concat(extra ?? []).ToArray()));
        try
        {
            session = Call(1, Proto.Blob(7, [])).Int(1);
            if (session == 0) return null;
            Call(17, Proto.Blob(9, Proto.Number(23, CanLearn ? 0UL : 1UL))); // session-local incognito setting
            Command(22, Proto.Number(3, 1)); // private TURN_ON_IME, HIRAGANA
            var surrounding = string.IsNullOrEmpty(context) ? [] : Proto.Blob(6, Proto.String(1, context));
            var update = Proto.Number(1, 26).Concat(Proto.Blob(11, Proto.String(1, hiragana))).ToArray();
            Call(5, Proto.Blob(4, update).Concat(surrounding).ToArray());
            var output = Call(3, Proto.Blob(3, Proto.Number(3, 4)).Concat(surrounding).ToArray()); // SPACE
            var clauses = output.Message(5).Groups(2)
                .Select(p => new ConversionClause(p.Text(6), p.Text(4))).ToList();
            if (clauses.Count == 0 || !SameReading(string.Concat(clauses.Select(c => c.Reading)), hiragana)) return null;
            var offset = 0;
            for (var i = 0; i < clauses.Count; i++)
            {
                var original = hiragana.Substring(offset, clauses[i].Reading.Length);
                clauses[i] = new ConversionClause(original, RestoreSymbolWidth(clauses[i].Text, original));
                offset += original.Length;
            }
            for (var index = 0; index < clauses.Count; index++)
            {
                if (index > 0) output = Call(3, Proto.Blob(3, Proto.Number(3, 7))); // RIGHT: focus next clause
                var words = output.Message(14).Messages(2).Select(p => p.Text(4)).Where(t => t.Length > 0).ToList();
                if (words.Count == 0) words = output.Message(6).Groups(3).Select(p => p.Text(5)).Where(t => t.Length > 0).ToList();
                _candidates[clauses[index].Reading] = words.Select(w => RestoreSymbolWidth(w, clauses[index].Reading)).Prepend(clauses[index].Text).Distinct().ToArray();
            }
            return clauses;
        }
        catch (Exception ex)
        {
            _candidates.Clear();
            Diagnostics.Log.Warn($"Google日本語入力との試験的な連携に失敗しました: {ex.Message}");
            return null;
        }
        finally
        {
            if (session != 0)
            {
                // DELETE_SESSION does not submit the pending composition.
                try { Call(2); } catch (Exception ex) { Diagnostics.Log.Warn($"Google試験セッションを破棄できませんでした: {ex.Message}"); }
            }
        }
    }

    private static char FoldSymbol(char c) => c is >= '！' and <= '～' && !char.IsLetterOrDigit(c) ? (char)(c - 0xFEE0) : c;
    private static bool SameReading(string returned, string requested) => returned.Length == requested.Length &&
        returned.Zip(requested).All(pair => FoldSymbol(pair.First) == FoldSymbol(pair.Second));
    private static string RestoreSymbolWidth(string text, string reading) =>
        reading.Length > 0 && reading.All(c => FoldSymbol(c) is >= '!' and <= '~' && !char.IsLetterOrDigit(c)) && SameReading(text, reading)
            ? reading : text;

    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses) => TryLearn(context, clauses);

    internal bool TryLearn(string? context, IReadOnlyList<ConversionClause> clauses)
    {
        if (!CanLearn || clauses.Count == 0) return false;
        var reading = string.Concat(clauses.Select(c => c.Reading));
        if (reading.Length is 0 or > 100 || reading.Any(char.IsControl) || clauses.Any(c => c.Text.Length == 0 || c.Text.Any(char.IsControl))) return false;
        lock (_gate)
        {
            if (!CanLearn) return false;
            ulong session = 0;
            var deadline = Stopwatch.StartNew();
            Proto Call(int kind, byte[]? payload = null, bool allowResult = false)
            {
                if (kind is not (1 or 2 or 3 or 5 or 6 or 8 or 17)) throw new InvalidOperationException("Unsupported Google command");
                if (deadline.ElapsedMilliseconds > 2000 && kind != 2) throw new TimeoutException("Google learning deadline");
                var request = Proto.Number(1, (ulong)kind).Concat(session == 0 ? [] : Proto.Number(2, session)).Concat(payload ?? []).ToArray();
                var output = Proto.Parse(_transport(request));
                if (output.Int(11) != 0 || (!allowResult && output.Has(4))) throw new InvalidOperationException("Unexpected Google result");
                return output;
            }
            try
            {
                var config = Call(6).Message(9);
                if (config.Int(20) != 0 || config.Int(50) != 0)
                {
                    Diagnostics.Log.Info("Google日本語入力の設定により、Google側の学習は停止しています。");
                    return false;
                }
                session = Call(1, Proto.Blob(7, [])).Int(1);
                if (session == 0) return false;
                Call(17, Proto.Blob(9, Proto.Number(23, 0)));
                Call(5, Proto.Blob(4, Proto.Number(1, 22).Concat(Proto.Number(3, 1)).ToArray()));
                var surrounding = string.IsNullOrEmpty(context) ? [] : Proto.Blob(6, Proto.String(1, context));
                var update = Proto.Number(1, 26).Concat(Proto.Blob(11, Proto.String(1, reading))).ToArray();
                Call(5, Proto.Blob(4, update).Concat(surrounding).ToArray());
                var output = Call(3, Proto.Blob(3, Proto.Number(3, 4)).Concat(surrounding).ToArray());
                var actual = output.Message(5).Groups(2).Select(p => p.Text(6)).ToArray();
                if (!actual.SequenceEqual(clauses.Select(c => c.Reading))) return false;
                for (var index = 0; index < clauses.Count; index++)
                {
                    if (index > 0) output = Call(3, Proto.Blob(3, Proto.Number(3, 7)));
                    var matches = output.Message(14).Messages(2).Where(p => p.Has(1) && p.Text(4) == clauses[index].Text).ToArray();
                    if (matches.Length == 0) return false; // Do not invent or partially commit a candidate.
                    output = Call(5, Proto.Blob(4, Proto.Number(1, 4).Concat(Proto.Number(2, matches[0].Int(1))).ToArray())); // HIGHLIGHT only
                }
                var selected = string.Concat(output.Message(5).Groups(2).Select(p => p.Text(4)));
                var expected = string.Concat(clauses.Select(c => c.Text));
                if (selected != expected) return false;
                if (!CanLearn) return false;
                var result = Call(5, Proto.Blob(4, Proto.Number(1, 2)), allowResult: true); // SUBMIT only after exact match
                if (result.Message(4).Text(2) != expected) throw new InvalidOperationException("Google committed an unexpected candidate");
                Call(8); // persist through Google's own server
                _candidates.Clear();
                Diagnostics.Log.Info("Google日本語入力に確定した変換を学習させました。");
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Google日本語入力への学習を完了できませんでした: {ex.Message}");
                return false;
            }
            finally
            {
                if (session != 0)
                    try { Call(2); } catch (Exception ex) { Diagnostics.Log.Warn($"Google学習セッションを破棄できませんでした: {ex.Message}"); }
            }
        }
    }

    private static byte[] CallPipe(byte[] request)
    {
        var names = Directory.GetFiles(@"\\.\pipe\")
            .Select(Path.GetFileName).Where(n => n is not null && n.StartsWith("googlejapaneseinput.", StringComparison.Ordinal)
                && n.EndsWith(".session", StringComparison.Ordinal)).ToArray();
        if (names.Length != 1) throw new IOException("Googleの変換サーバーが一つだけ起動している必要があります。");
        using var pipe = new NamedPipeClientStream(".", names[0]!, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(500);
        pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) throw new IOException("Google接続先を確認できません。");
        using var process = Process.GetProcessById((int)pid);
        if (!string.Equals(Path.GetFileName(process.MainModule?.FileName), "GoogleIMEJaConverter.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Googleの変換プロセスではありません。");
        pipe.ReadMode = PipeTransmissionMode.Message;
        pipe.WriteAsync(request, timeout.Token).AsTask().GetAwaiter().GetResult();
        using var response = new MemoryStream();
        var buffer = new byte[8192];
        do
        {
            var length = pipe.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
            if (length == 0 || response.Length + length > 1024 * 1024) throw new IOException("Invalid Google response size");
            response.Write(buffer, 0, length);
        } while (!pipe.IsMessageComplete);
        return response.ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

    // Small bounded reader for the subset of Mozc's public proto2 schema used here.
    internal sealed class Proto
    {
        private readonly Dictionary<int, List<object>> _fields = new();
        public bool Has(int field) => _fields.ContainsKey(field);
        public ulong Int(int field) => _fields.TryGetValue(field, out var v) && v[0] is ulong n ? n : 0;
        public string Text(int field) => Encoding.UTF8.GetString(Bytes(field));
        private byte[] Bytes(int field) => _fields.TryGetValue(field, out var v) && v[0] is byte[] b ? b : [];
        public Proto Message(int field) => Parse(Bytes(field));
        public IEnumerable<Proto> Messages(int field) => _fields.GetValueOrDefault(field, []).OfType<byte[]>().Select(Parse);
        public IEnumerable<Proto> Groups(int field) => _fields.GetValueOrDefault(field, []).OfType<Proto>();
        public static byte[] Number(int field, ulong value) => Varint((ulong)(field << 3)).Concat(Varint(value)).ToArray();
        public static byte[] Blob(int field, byte[] value) => Varint((ulong)((field << 3) | 2)).Concat(Varint((ulong)value.Length)).Concat(value).ToArray();
        public static byte[] String(int field, string value) => Blob(field, Encoding.UTF8.GetBytes(value));
        private static IEnumerable<byte> Varint(ulong value)
        {
            while (value > 127) { yield return (byte)((value & 127) | 128); value >>= 7; }
            yield return (byte)value;
        }
        public static Proto Parse(byte[] data)
        {
            var offset = 0;
            ulong ReadInt()
            {
                ulong value = 0;
                for (var shift = 0; shift < 70; shift += 7)
                {
                    if (offset >= data.Length) throw new InvalidDataException("Truncated varint");
                    var b = data[offset++];
                    if (shift == 63 && b > 1) throw new InvalidDataException("Varint overflow");
                    value |= (ulong)(b & 127) << shift;
                    if (b < 128) return value;
                }
                throw new InvalidDataException("Varint overflow");
            }
            Proto Read(int endGroup = 0, int depth = 0)
            {
                if (depth > 32) throw new InvalidDataException("Excessive nesting");
                var result = new Proto();
                while (offset < data.Length)
                {
                    var tag = ReadInt();
                    var field = checked((int)(tag >> 3));
                    var wire = (int)(tag & 7);
                    if (field == 0) throw new InvalidDataException("Invalid field");
                    if (wire == 4)
                    {
                        if (field != endGroup) throw new InvalidDataException("Unexpected end group");
                        return result;
                    }
                    object value;
                    if (wire == 0) value = ReadInt();
                    else if (wire == 3) value = Read(field, depth + 1);
                    else if (wire is 1 or 2 or 5)
                    {
                        var size = wire == 2 ? checked((int)ReadInt()) : wire == 1 ? 8 : 4;
                        if (size > data.Length - offset) throw new InvalidDataException("Truncated field");
                        value = data[offset..(offset + size)]; offset += size;
                    }
                    else throw new InvalidDataException("Invalid wire type");
                    if (!result._fields.TryGetValue(field, out var values)) result._fields[field] = values = [];
                    values.Add(value);
                }
                if (endGroup != 0) throw new InvalidDataException("Unterminated group");
                return result;
            }
            return Read();
        }
    }
}
