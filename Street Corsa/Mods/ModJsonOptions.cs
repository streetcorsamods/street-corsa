using System.Text.Json;

namespace StreetCorsa.Mods;

/// <summary>
/// How every mod file is read: names whatever their case, comments and trailing commas allowed. Kept on its own so the
/// tools that link the part code (the converter, the bench) read a mod's files the game's way without the rest of the
/// mods' code.
/// </summary>
public static partial class ModJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>A mod's file read as a document or as nodes: comments and trailing commas allowed</summary>
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
