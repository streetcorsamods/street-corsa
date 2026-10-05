namespace StreetCorsa.Services.Settings
{
    /// <summary>
    /// The AI the player connects, if any: a text provider for what people say and write, and ComfyUI for pictures. Off
    /// by default. The key is never kept in plain text: <see cref="ApiKeyProtected"/> is the key encrypted for this
    /// Windows user (<see cref="Ai.AiKeys"/>). A settings.json from before the AI loads these defaults.
    /// </summary>
    public class AiSettings
    {
        /// <summary>The game asks the text provider for words</summary>
        public bool Enabled { get; set; }

        /// <summary>Which kind of API: <see cref="Ai.AiProviders.Ollama"/>, <see cref="Ai.AiProviders.OpenAi"/> or <see cref="Ai.AiProviders.Anthropic"/></summary>
        public string TextProvider { get; set; } = Ai.AiProviders.Ollama;

        /// <summary>The provider's address; empty for its usual one (<see cref="Ai.AiProviders.DefaultEndpoint"/>)</summary>
        public string? Endpoint { get; set; }

        /// <summary>The model to ask; empty for the provider's first (Ollama, OpenAI-compatible) or the usual one (Anthropic)</summary>
        public string? Model { get; set; }

        /// <summary>The API key, encrypted with Windows DPAPI for the current user, base64; null for none</summary>
        public string? ApiKeyProtected { get; set; }

        /// <summary>
        /// The provider the key was typed for: it is sent only while that provider is the one asked
        /// (<see cref="Ai.AiKeys.KeyFor"/>). Null in a settings.json from before keys were tied to one (the key is then the
        /// saved provider's, never Ollama's)
        /// </summary>
        public string? KeyProvider { get; set; }

        /// <summary>The address the key was typed for (empty for the provider's usual one): sent only while that is the one asked</summary>
        public string? KeyEndpoint { get; set; }

        /// <summary>The game asks ComfyUI for pictures (new rivals' portraits)</summary>
        public bool ComfyEnabled { get; set; }

        /// <summary>ComfyUI's address; empty for its usual one</summary>
        public string? ComfyEndpoint { get; set; }

        /// <summary>
        /// How much a talk sends with each line (<see cref="Talk.TalkDetail"/>: light, normal, full): less costs a paid
        /// provider less and reads faster; full is all the rules give
        /// </summary>
        public string TalkDetail { get; set; } = Talk.TalkDetail.Full;

        public AiSettings Clone() => (AiSettings)MemberwiseClone();
    }
}
