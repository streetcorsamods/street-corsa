namespace StreetCorsa.Rules;

/// <summary>
/// The AI the player may connect (<c>"ai"</c> in the rules): how long the game waits for it, how often it is asked, and
/// what it is asked to write. The game never needs it: what it says without one is always there, and a line the AI has
/// not written in time is the game's own.
/// </summary>
public sealed class AiRules
{
    /// <summary>Seconds a rival's line waits for the AI when none was written ahead; the game's own line after that</summary>
    public double TalkWaitSeconds { get; set; } = 1.5;

    /// <summary>Seconds one request may take before it is given up (a local model loading its weights is slow the first time)</summary>
    public double RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Seconds a ComfyUI picture may take, from the queue to the file</summary>
    public double ImageTimeoutSeconds { get; set; } = 240;

    /// <summary>Requests to the text provider at once (1 or 2: a local model does one at a time anyway)</summary>
    public int MaxConcurrent { get; set; } = 1;

    /// <summary>Seconds at least between two requests, so a busy day never floods a paid API</summary>
    public double MinSecondsBetween { get; set; } = 0.5;

    /// <summary>Requests waiting at most; past that a new background one is dropped (the game's own words stand)</summary>
    public int MaxQueued { get; set; } = 24;

    /// <summary>Seconds the AI is left alone after it could not be reached, before it is tried again</summary>
    public double RetryAfterFailureSeconds { get; set; } = 60;

    /// <summary>Lines a rival keeps written ahead per moment (greeting, track picked, pink slips...)</summary>
    public int TalkLinesAhead { get; set; } = 1;

    /// <summary>The chance a new line of the diner's street talk is told again in the AI's words</summary>
    public double StreetTalkChance { get; set; } = 1.0;

    /// <summary>The chance a new piece in the paper is written again by the AI</summary>
    public double NewsChance { get; set; } = 1.0;

    /// <summary>A new face on the street gets a portrait painted by ComfyUI, when it is on</summary>
    public bool PaintNewFaces { get; set; } = true;

    /// <summary>The AI's words kept in a save at most (dealers' takes, ads); the oldest go</summary>
    public int MaxKept { get; set; } = 400;
}
