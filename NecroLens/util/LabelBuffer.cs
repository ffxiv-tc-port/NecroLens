using System;

namespace NecroLens.util;

/// <summary>
/// 可重用的字元緩衝：每幀組標籤文字時不配置新字串。不是執行緒安全的，只給繪製執行緒用。
/// </summary>
/// <remarks>
/// 數值的格式化與 <c>ToString(format)</c> 相同（格式提供者為 null＝目前文化特性），
/// 所以組出來的字元與原本用字串串接的結果逐字相同。
/// </remarks>
public sealed class LabelBuffer
{
    private char[] chars = new char[128];

    public int Length { get; private set; }

    public ReadOnlySpan<char> Span => chars.AsSpan(0, Length);

    public void Clear()
    {
        Length = 0;
    }

    public void Append(char c)
    {
        EnsureFree(1);
        chars[Length++] = c;
    }

    public void Append(string? s)
    {
        if (string.IsNullOrEmpty(s)) return;

        EnsureFree(s.Length);
        s.AsSpan().CopyTo(chars.AsSpan(Length));
        Length += s.Length;
    }

    public void Append<T>(T value, string? format = null) where T : ISpanFormattable
    {
        int written;
        while (!value.TryFormat(chars.AsSpan(Length), out written, format, null))
            EnsureFree(chars.Length - Length + 1);

        Length += written;
    }

    public override string ToString()
    {
        return new string(chars, 0, Length);
    }

    private void EnsureFree(int count)
    {
        if (chars.Length - Length >= count) return;

        var newSize = Math.Max(chars.Length * 2, Length + count);
        Array.Resize(ref chars, newSize);
    }
}
