using System;
using System.Text;

namespace InkGuide.App.Bridge.Backend;

/// <summary>BOM/encoding/EOL detection with round-trip preservation for text editing.</summary>
internal static class TextCodec
{
    public sealed record DecodedText(string Text, string Encoding, string Eol, bool HasBom);

    public static DecodedText Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new DecodedText(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8", DetectEol(bytes), true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new DecodedText(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le", DetectEol(bytes), true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be", DetectEol(bytes), true);
        }

        return new DecodedText(Encoding.UTF8.GetString(bytes), "utf-8", DetectEol(bytes), false);
    }

    public static byte[] Encode(string text, byte[] currentBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(currentBytes);
        DecodedText current = Decode(currentBytes);
        Encoding encoding = current.Encoding switch
        {
            "utf-16le" => Encoding.Unicode,
            "utf-16be" => Encoding.BigEndianUnicode,
            _ => Encoding.UTF8,
        };
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        string eol = current.Eol == "\r\n" ? "\r\n" : "\n";
        string withEol = normalized.Replace("\n", eol, StringComparison.Ordinal);
        byte[] body = encoding.GetBytes(withEol);
        if (!current.HasBom)
        {
            return body;
        }

        byte[] preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string DetectEol(byte[] bytes)
    {
        // Sample-based: CRLF wins ties so Windows-edited files keep their endings.
        int crlf = 0;
        int lf = 0;
        int sample = Math.Min(bytes.Length, 65536);
        for (int i = 0; i < sample; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                if (i > 0 && bytes[i - 1] == (byte)'\r')
                {
                    crlf++;
                }
                else
                {
                    lf++;
                }
            }
        }

        return crlf >= lf ? "\r\n" : "\n";
    }
}
