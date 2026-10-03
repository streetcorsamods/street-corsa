using StreetCorsa.Logging;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Career;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The racers the mods define: every .json in a mod's <c>racers</c> folder (a list under "opponents", a list, or
    /// one racer), their portraits next to them. A mod's racers get its namespace; one that <c>replaces</c> a racer
    /// takes its place (a mod's King replaces "king"). Read on every call: each caller gets racers of its own to keep.
    /// </summary>
    public class OpponentRepository : IOpponentRepository
    {
        private readonly ModSet _mods;
        private readonly IAppLogger _logger;

        private static readonly JsonSerializerOptions BossOptions = new() { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

        public OpponentRepository() : this(ModSet.Installed)
        {
        }

        public OpponentRepository(ModSet mods)
        {
            _mods = mods;
            _logger = AppLoggerFactory.CreateLogger("OpponentRepository");
            _victories = new(() => ReferenceEquals(mods, ModSet.Installed) ? CareerContent.Installed.Victories : CareerContent.Load(mods).Victories);
        }

        /// <summary>The victory paths of the same mods, for the path a boss names; read only when one may be his mod's own</summary>
        private readonly Lazy<IReadOnlyList<VictoryDefinition>> _victories;

        private IEnumerable<VictoryDefinition> Victories()
        {
            foreach (var victory in _victories.Value) yield return victory;
        }

        /// <summary>
        /// Load all opponent definitions from every mod
        /// </summary>
        public List<Opponent> LoadAllOpponents()
        {
            var problems = new List<string>();
            var registry = new ContentRegistry<Opponent>("racer");
            var removals = registry.Removals(_mods, ContentTypes.Racers);
            try
            {
                foreach (var entry in ModJson.Entries(_mods, ContentTypes.Racers, "opponents", problems, idName: "opponentId"))
                {
                    removals.Before(entry.Mod);
                    var localId = ModJson.Text(entry.Element, "opponentId") ?? entry.Id;
                    var opponent = ParseOpponent(entry, problems);
                    if (opponent == null) continue;

                    if (registry.Add(entry.Mod, localId, entry.Replaces, opponent, entry.Source) is { } id)
                    {
                        opponent.DefinitionId = id;
                        opponent.OpponentId = StableIdFor(id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load the racers");
            }

            removals.Rest();
            problems.AddRange(registry.Problems);
            foreach (var problem in problems) _logger.Warning("Racers: {Problem}", problem);
            ModProblems.Add("Racers", problems);
            _logger.Information("Loaded {Count} opponent definitions", registry.Count);
            return registry.All.ToList();
        }

        /// <summary>
        /// The same Guid for the same definition id on every load and every machine, so an opponent is the same
        /// opponent from one game to the next. A definition id that already is a Guid is taken as it is.
        /// </summary>
        internal static Guid StableIdFor(string definitionId)
        {
            if (Guid.TryParse(definitionId, out var guid)) return guid;

            var hash = MD5.HashData(Encoding.UTF8.GetBytes("street-rod-opponent:" + definitionId.ToLowerInvariant()));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3: name-based (MD5)
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant
            return new Guid(hash, bigEndian: true);
        }

        /// <summary>
        /// Parse a single opponent from JSON element
        /// </summary>
        private Opponent? ParseOpponent(ModEntry entry, List<string> problems)
        {
            var json = entry.Element;
            try
            {
                // Extract required fields; the id is the registry's to give
                var name = json.GetProperty("name").GetString() ?? "Unknown";
                var age = json.GetProperty("age").GetInt32();
                var genderStr = json.GetProperty("gender").GetString() ?? "Male";
                var skill = json.GetProperty("skill").GetInt32();
                var aggression = json.GetProperty("aggression").GetInt32();

                // Parse gender enum: TryParse takes any number ("7"), which is no gender
                if (!Enum.TryParse<Gender>(genderStr, out var gender) || !Enum.IsDefined(gender))
                {
                    gender = Gender.Male;
                }

                // The definition's full id is kept, and the Guid is made from it rather than rolled fresh on every
                // load (LoadAllOpponents): the same definition is the same opponent every time
                var opponent = new Opponent(name, age, gender, skill, aggression);

                // Extract optional fields
                if (json.TryGetProperty("nickname", out var nicknameElement))
                {
                    opponent.Nickname = nicknameElement.GetString() ?? string.Empty;
                }

                if (json.TryGetProperty("portraitPath", out var portraitElement))
                {
                    opponent.PortraitPath = Portraits.FromMod(_mods, entry.Mod, portraitElement.GetString());
                }

                if (json.TryGetProperty("location", out var locationElement))
                {
                    opponent.Location = locationElement.GetString();
                }

                // A boss: "boss": { "path": ..., his own figures } (true: one with neither); "isKing": true, as before
                // bosses, is the King path's
                if (json.TryGetProperty("boss", out var bossElement) && bossElement.ValueKind is not (JsonValueKind.False or JsonValueKind.Null))
                {
                    BossRecord record;
                    try
                    {
                        record = bossElement.ValueKind == JsonValueKind.True ? new BossRecord()
                            : bossElement.ValueKind == JsonValueKind.Object ? bossElement.Deserialize<BossRecord>(BossOptions) ?? new BossRecord()
                            : throw new JsonException("\"boss\" is an object (or true)");
                    }
                    catch (JsonException ex)
                    {
                        problems.Add($"{entry.Source}: racer {name}'s boss block does not read ({ex.Message}); left out");
                        return null;
                    }

                    opponent.IsBoss = true;
                    opponent.BossRecord = record;
                    opponent.BossPath = Bosses.ResolvePath(entry.Mod.Id, record.Path, Victories());
                }
                else if (json.TryGetProperty("isKing", out var kingElement) && kingElement.ValueKind == JsonValueKind.True)
                {
                    opponent.IsBoss = true;
                    opponent.BossPath = Bosses.KingPathId;
                }

                if (json.TryGetProperty("biography", out var biographyElement))
                {
                    opponent.Biography = biographyElement.GetString();
                }

                return opponent;
            }
            catch (Exception ex)
            {
                problems.Add($"{entry.Source}: a racer that does not read ({ex.Message}); left out");
                return null;
            }
        }
    }
}
