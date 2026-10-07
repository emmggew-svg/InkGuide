namespace InkGuide.Analysis.Identification;

/// <summary>Which signal decided the kind.</summary>
public enum KindIdentificationSource
{
    /// <summary>File extension via the kind map.</summary>
    Extension = 0,

    /// <summary>Generic MIME prefix table.</summary>
    Mime = 1,

    /// <summary>Magic-byte signature of the content header.</summary>
    MagicBytes = 2,

    /// <summary>Google-native MIME mapping.</summary>
    GoogleNative = 3,

    /// <summary>Drive folder MIME type.</summary>
    Folder = 4,

    /// <summary>No signal matched.</summary>
    Fallback = 5,
}

/// <summary>One kind identification outcome.</summary>
/// <param name="Kind">Stable kind id from the Appendix A taxonomy.</param>
/// <param name="Confidence">Signal confidence in 0-1.</param>
/// <param name="Source">Deciding signal.</param>
public sealed record KindIdentification(string Kind, double Confidence, KindIdentificationSource Source);
