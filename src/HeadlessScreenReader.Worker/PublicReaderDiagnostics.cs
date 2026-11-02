using System.Text;
using System.Text.RegularExpressions;

namespace HeadlessScreenReader.Worker;

/// <summary>Bounded diagnostics classified only after native Closed, zero exit and exact stdout EOF.</summary>
public static class PublicReaderDiagnostics
{
    public static int ClassifyAfterConfirmedClose(byte[] bytes, string runtime)
    {
        if (bytes.Length == 0) return 0;
        if (bytes.Length > 16384 || runtime != "podman") throw new InvalidDataException("PUBLIC_READER_DIAGNOSTICS_UNCONFIRMED");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidDataException("PUBLIC_READER_DIAGNOSTICS_UNCONFIRMED"); }
        // A confirmed native Closed frame, zero exit and exact stdout EOF precede
        // this classification. Retain the one known Podman EOF warning explicitly;
        // any other diagnostic still fails acceptance.
        if (!Regex.IsMatch(text, "\\Atime=\"[0-9T:.+\\-]+\" level=warning msg=\"Failed to close STDIN for writing: EOF\"\\r?\\n\\z", RegexOptions.NonBacktracking))
            throw new InvalidDataException("PUBLIC_READER_DIAGNOSTICS_UNCONFIRMED");
        return 1;
    }
}
