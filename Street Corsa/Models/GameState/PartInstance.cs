namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// One physical part somebody owns, as it is saved: what it is, what shape it is in, how it is tuned and
    /// what is bolted onto it. A car's engine is one of these with the whole engine below it; a loose part
    /// on a shelf keeps whatever was on it when it came off.
    /// </summary>
    public class PartInstance
    {
        /// <summary>Slot of a car the engine block sits on</summary>
        public const int CarEngineSlot = 1;

        public Guid InstanceId { get; set; } = Guid.NewGuid();

        /// <summary>Id of the part in the parts catalog, e.g. "engines/chrysler/chrysler_340_v8_block"</summary>
        public string DefinitionId { get; set; } = string.Empty;

        /// <summary>1 = new, 0 = worn out (mileage)</summary>
        public double Wear { get; set; } = 1.0;

        /// <summary>1 = straight, 0 = wrecked (damage)</summary>
        public double Tear { get; set; } = 1.0;

        /// <summary>A <see cref="Tear"/> at this or better is no damage: nothing to mend, nothing to tell the player (the core's figure)</summary>
        public const double Undamaged = 0.995;

        /// <summary>
        /// Where the game reads the figure it plays by (the rules' <c>condition.undamagedTear</c>, wired by the parts
        /// wiring); the tools that link this file have no rules and keep the core's
        /// </summary>
        public static Func<double> UndamagedTearSource { get; set; } = () => Undamaged;

        /// <summary>A <see cref="Tear"/> at this or better is no damage, as the rules in play have it</summary>
        public static double UndamagedTear => UndamagedTearSource();

        /// <summary>Settings changed from the part's defaults, by spec ("idle_rpm", "gears[1]")</summary>
        public Dictionary<string, double> Tuning { get; set; } = new();

        /// <summary>
        /// What a machine shop has cut on the part, by the cut (<c>rebuilds.json</c>'s machine jobs: "bore", "grind",
        /// "mill", "port"): how many of the cut's sizes are gone, 1 for the first (+.030 over). Empty: as it left the
        /// factory. Pistons fitted to a bored block carry the block's bore.
        /// </summary>
        public Dictionary<string, int> Cuts { get; set; } = new();

        /// <summary>The sizes of the cut gone on the part; 0 for none</summary>
        public int CutOf(string cut) => Cuts != null && Cuts.TryGetValue(cut, out var done) && done > 0 ? done : 0;

        /// <summary>
        /// Slot of whatever this part is mounted on: a slot of the parent part, or of the car for the parts
        /// that sit on the car itself (<see cref="CarEngineSlot"/>). 0 while the part is loose.
        /// </summary>
        public int ParentSlot { get; set; }

        /// <summary>The part's own slot it is mounted by; 0 on the car itself or while loose</summary>
        public int OwnSlot { get; set; }

        public List<PartInstance> Children { get; set; } = [];

        public PartInstance() { }

        public PartInstance(string definitionId)
        {
            DefinitionId = definitionId;
        }

        /// <summary>
        /// Parts mounted one on another no deeper than this: a real engine is a dozen deep (block, head, intake, carb,
        /// cleaner). A save is anyone's file: what hangs deeper is left out. <c>PartTrees</c> keeps to the same depth.
        /// </summary>
        public const int MaxDepth = 64;

        /// <summary>
        /// This part and everything on it, each part before what is on it and in the order they are mounted. Walked
        /// without recursion, so a save of any depth cannot overflow the stack: what hangs deeper than
        /// <see cref="MaxDepth"/> is left out, as are parts a save left null.
        /// </summary>
        public IEnumerable<PartInstance> SelfAndDescendants() => SelfAndDescendantsWithDepth().Select(p => p.Part);

        /// <summary><see cref="SelfAndDescendants"/> with how deep each part hangs: 0 for this one, 1 for what is on it</summary>
        public IEnumerable<(PartInstance Part, int Depth)> SelfAndDescendantsWithDepth()
        {
            var stack = new Stack<(PartInstance Part, int Depth)>();
            stack.Push((this, 0));
            while (stack.Count > 0)
            {
                var (part, depth) = stack.Pop();
                yield return (part, depth);
                if (depth >= MaxDepth || part.Children is not { } children) continue;

                for (var i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] != null) stack.Push((children[i], depth + 1));
                }
            }
        }
    }
}
