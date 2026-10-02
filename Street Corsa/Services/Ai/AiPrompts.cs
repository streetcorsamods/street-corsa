using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Ai
{
    /// <summary>One thing the AI is asked to write: its prompt (with the game's {placeholders}) and how long it may be</summary>
    public sealed class AiPrompt
    {
        /// <summary>What is asked, filled like the game's text (<see cref="Text.GameText.Fill"/>)</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>The most characters kept of the answer; it is cut at a sentence or a word before that</summary>
        public int MaxLength { get; set; } = 200;

        /// <summary>The most tokens the model may answer with</summary>
        public int MaxTokens { get; set; } = 120;

        /// <summary>How free the model is (0 to about 1.5); providers that take none are not sent it</summary>
        public double Temperature { get; set; } = 0.9;

        /// <summary>
        /// Lines of the answer kept, the first ones (a small model likes to offer a second try: "Or, edgier: ..."); 0 keeps all
        /// </summary>
        public int Lines { get; set; } = 1;
    }

    /// <summary>
    /// A ComfyUI workflow of a mod (<c>ai/comfy_&lt;use&gt;.json</c>): the graph in ComfyUI's API format ("Save (API
    /// Format)"), and which node inputs the game fills in. The last mod in load order that has one for a use gives it
    /// whole: a graph is not merged.
    /// </summary>
    public sealed class ComfyWorkflow
    {
        /// <summary>The checkpoint (model file) the graph loads, as ComfyUI lists it in models/checkpoints</summary>
        public string Checkpoint { get; set; } = string.Empty;

        public int Width { get; set; } = 512;

        public int Height { get; set; } = 512;

        /// <summary>The picture asked for, with the game's {placeholders}</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>What the picture must not be</summary>
        public string Negative { get; set; } = string.Empty;

        private Dictionary<string, ComfyInput> _fill = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Where each value goes: "positive", "negative", "seed", "width", "height", "checkpoint", "prefix", in any case
        /// (the JSON reader sets a dictionary of its own: it is copied into one that ignores case)
        /// </summary>
        public Dictionary<string, ComfyInput> Fill
        {
            get => _fill;
            set
            {
                // The same name twice in other cases: the last one written
                _fill = new Dictionary<string, ComfyInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, input) in value ?? []) _fill[name] = input;
            }
        }

        /// <summary>The graph, node id to node, in ComfyUI's API format</summary>
        public JsonObject Workflow { get; set; } = new();

        /// <summary>Where it came from, for the log ("streetcorsa/ai/comfy_portrait.json")</summary>
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// How a person talks and deals (<c>ai/people.json</c>'s <c>temperaments</c>): the words the AI is given for it, how far
    /// they come down on a price (times the rules' most), who it is for, and how often it is drawn
    /// </summary>
    public sealed class Temperament
    {
        /// <summary>Its key in the file</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Who they are, for the AI: "a fast-talking salesman who calls everybody pal"</summary>
        public string Words { get; set; } = string.Empty;

        /// <summary>How far they come down, times the rules' most for their kind (0: never, 1: all the way)</summary>
        public double Give { get; set; } = 1;

        /// <summary>The kinds of person it is drawn for: "seller", "salesman", "rival"; none is every kind</summary>
        public List<string> For { get; set; } = [];

        /// <summary>How often it is drawn against the others</summary>
        public double Weight { get; set; } = 1;

        /// <summary>For rivals: drawn only for an aggression in this range (0-100)</summary>
        public int MinAggression { get; set; }

        public int MaxAggression { get; set; } = 100;

        public bool Fits(string kind, int aggression) =>
            (For.Count == 0 || For.Contains(kind, StringComparer.OrdinalIgnoreCase)) && aggression >= MinAggression && aggression <= MaxAggression;
    }

    /// <summary>
    /// The game's own model in Ollama (<c>ai/ollama.json</c>): made from a base model the player has, with a system prompt,
    /// parameters and a few example exchanges in the game's voice. No training: what a Modelfile does.
    /// </summary>
    public sealed class OllamaModel
    {
        /// <summary>What it is called in Ollama</summary>
        public string Name { get; set; } = "streetcorsa";

        /// <summary>The model it is made from, when the player names none</summary>
        public string From { get; set; } = "llama3.1:8b";

        /// <summary>Its own system prompt (a list of lines is joined), under the persona sent with every request</summary>
        public JsonNode? System { get; set; }

        /// <summary>Its parameters, as a Modelfile's (temperature, repeat_penalty, num_ctx, stop...)</summary>
        public JsonObject Parameters { get; set; } = new();

        /// <summary>Example exchanges: what the player says, what is answered</summary>
        public List<OllamaExample> Examples { get; set; } = [];

        public string SystemText => AiPrompts.Joined(System);
    }

    public sealed class OllamaExample
    {
        public string Player { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;
    }

    /// <summary>A node's input: <c>{ "node": "10", "input": "text" }</c></summary>
    public sealed class ComfyInput
    {
        public string Node { get; set; } = string.Empty;

        public string Input { get; set; } = string.Empty;
    }

    /// <summary>
    /// What the AI is asked, as mod data (<c>Content\&lt;mod&gt;\ai</c>): <c>prompts.json</c>, every mod's merged key by key
    /// over the mods' before it (a mod changes one prompt, or the persona, without repeating the rest), and
    /// <c>comfy_&lt;use&gt;.json</c>, a ComfyUI workflow per use, the last mod's whole. So a game mod has its own voice
    /// and its own pictures. Read once; what does not read is told among the mods' problems.
    /// </summary>
    public sealed class AiPrompts
    {
        public const string PromptsFile = "prompts.json";
        public const string PeopleFile = "people.json";
        public const string OllamaFile = "ollama.json";
        public const string ComfyPrefix = "comfy_";

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Ai");
        private static readonly Lazy<AiPrompts> InstalledPrompts = new(() => Load(ModSet.Installed));

        private static readonly JsonSerializerOptions Options = new(ModJson.Options);

        /// <summary>The prompts of the mods next to the game, read once</summary>
        public static AiPrompts Installed => InstalledPrompts.Value;

        /// <summary>
        /// Who the AI is: the setting, the voice, the language and units ({language}, {units}, {title}); sent as the
        /// system prompt with every request
        /// </summary>
        public string Persona { get; private init; } = string.Empty;

        public IReadOnlyDictionary<string, AiPrompt> Prompts { get; private init; } = new Dictionary<string, AiPrompt>();

        public IReadOnlyDictionary<string, ComfyWorkflow> Workflows { get; private init; } = new Dictionary<string, ComfyWorkflow>();

        public IReadOnlyList<string> Problems { get; private init; } = [];

        /// <summary>The temperaments people are drawn with (<c>ai/people.json</c>)</summary>
        public IReadOnlyList<Temperament> Temperaments { get; private init; } = [];

        /// <summary>The game's model in Ollama (<c>ai/ollama.json</c>); null when no mod has one</summary>
        public OllamaModel? Ollama { get; private init; }

        public Temperament? TemperamentOf(string? id) =>
            Temperaments.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>A text, or a list of lines joined with spaces</summary>
        public static string Joined(JsonNode? node) => node switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonArray lines => string.Join(" ", lines.Select(l => l?.ToString()).Where(l => !string.IsNullOrEmpty(l))),
            _ => string.Empty
        };

        public AiPrompt? Prompt(string use) => Prompts.TryGetValue(use, out var prompt) ? prompt : null;

        public ComfyWorkflow? Workflow(string use) => Workflows.TryGetValue(use, out var workflow) ? workflow : null;

        public static AiPrompts Load(ModSet mods)
        {
            var problems = new List<string>();
            var merged = ModMerge.NewObject();
            var people = ModMerge.NewObject();
            var ollama = ModMerge.NewObject();
            var workflows = new Dictionary<string, ComfyWorkflow>(StringComparer.OrdinalIgnoreCase);

            foreach (var (mod, file) in mods.JsonFiles(ContentTypes.Ai))
            {
                var name = Path.GetFileName(file);
                var source = $"{mod.Id}/{ContentTypes.Ai}/{name}";
                if (name.Equals(PromptsFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (ModMerge.ReadFile(file, source, problems) is { } read) ModMerge.Into(merged, read);
                }
                else if (name.Equals(PeopleFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (ModMerge.ReadFile(file, source, problems) is { } read) ModMerge.Into(people, read);
                }
                else if (name.Equals(OllamaFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (ModMerge.ReadFile(file, source, problems) is { } read) ModMerge.Into(ollama, read);
                }
                else if (name.StartsWith(ComfyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var use = Path.GetFileNameWithoutExtension(name)[ComfyPrefix.Length..];
                    if (ReadWorkflow(file, source, problems) is { } workflow) workflows[use] = workflow;
                }
                else
                {
                    problems.Add($"{source}: not {PromptsFile}, {PeopleFile}, {OllamaFile} nor a {ComfyPrefix}<use>.json workflow; left out");
                }
            }

            var prompts = new Dictionary<string, AiPrompt>(StringComparer.OrdinalIgnoreCase);
            if (merged["prompts"] is JsonObject uses)
            {
                foreach (var (use, node) in uses)
                {
                    if (use.StartsWith('_')) continue;
                    try
                    {
                        var prompt = node?.Deserialize<AiPrompt>(Options);
                        if (prompt == null || string.IsNullOrWhiteSpace(prompt.Prompt))
                            problems.Add($"ai prompt {use}: no prompt; left out");
                        else
                            prompts[use] = Checked(prompt);
                    }
                    catch (JsonException ex)
                    {
                        problems.Add($"ai prompt {use}: {ex.Message}; left out");
                    }
                }
            }

            var persona = Joined(merged["persona"]);
            var temperaments = ReadTemperaments(people, problems);
            var model = ReadOllama(ollama, problems);

            foreach (var problem in problems) Logger.Warning("AI content: {Problem}", problem);
            ModProblems.Add("AI", problems);
            return new AiPrompts
            {
                Persona = persona, Prompts = prompts, Workflows = workflows, Problems = problems, Temperaments = temperaments, Ollama = model
            };
        }

        private static List<Temperament> ReadTemperaments(JsonObject people, List<string> problems)
        {
            var temperaments = new List<Temperament>();
            if (people["temperaments"] is not JsonObject drawn) return temperaments;

            foreach (var (id, node) in drawn)
            {
                if (id.StartsWith('_') || node == null) continue;
                try
                {
                    var temperament = node.Deserialize<Temperament>(Options);
                    if (temperament == null || string.IsNullOrWhiteSpace(temperament.Words))
                    {
                        problems.Add($"temperament {id}: no words; left out");
                        continue;
                    }

                    temperament.Id = id;
                    temperament.Give = Math.Clamp(temperament.Give, 0, 2);
                    temperament.Weight = Math.Max(0, temperament.Weight);
                    temperaments.Add(temperament);
                }
                catch (JsonException ex)
                {
                    problems.Add($"temperament {id}: {ex.Message}; left out");
                }
            }

            return temperaments;
        }

        private static OllamaModel? ReadOllama(JsonObject ollama, List<string> problems)
        {
            if (ollama.Count == 0) return null;
            try
            {
                var model = ollama.Deserialize<OllamaModel>(Options);
                if (model != null && !string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.From)) return model;
                problems.Add($"{OllamaFile}: no name or no from; left out");
            }
            catch (JsonException ex)
            {
                problems.Add($"{OllamaFile}: {ex.Message}; left out");
            }

            return null;
        }

        /// <summary>Figures a prompt cannot do with, put inside what a request can take</summary>
        private static AiPrompt Checked(AiPrompt prompt)
        {
            prompt.MaxLength = Math.Clamp(prompt.MaxLength, 20, 4000);
            prompt.MaxTokens = Math.Clamp(prompt.MaxTokens, 16, 4000);
            prompt.Temperature = Math.Clamp(prompt.Temperature, 0, 2);
            prompt.Lines = Math.Max(0, prompt.Lines);
            return prompt;
        }

        private static ComfyWorkflow? ReadWorkflow(string file, string source, List<string> problems)
        {
            if (ModMerge.ReadFile(file, source, problems) is not { } read) return null;
            try
            {
                var workflow = read.Deserialize<ComfyWorkflow>(Options);
                if (workflow == null || workflow.Workflow.Count == 0)
                {
                    problems.Add($"{source}: no \"workflow\" graph; left out");
                    return null;
                }

                foreach (var (what, input) in workflow.Fill)
                {
                    if (workflow.Workflow[input.Node]?["inputs"] is not JsonObject)
                        problems.Add($"{source}: \"fill\" {what} names node {input.Node}, which the graph does not have (or has no inputs)");
                }

                workflow.Source = source;
                return workflow;
            }
            catch (JsonException ex)
            {
                problems.Add($"{source}: {ex.Message}; left out");
                return null;
            }
        }
    }
}
