using System.Numerics;
using System.Text.RegularExpressions;
using AcTools.Kn5File;
using StreetCorsa.Helpers;

namespace StreetCorsa.Services.Race.Traffic.Conversion
{
    /// <summary>One mesh of a model in the model's space: its node, where it is, its box, the names above it, its material</summary>
    public sealed record Kn5Mesh(Kn5Node Node, Matrix4x4 World, Vector3 Min, Vector3 Max, IReadOnlyList<string> Path, Kn5Material? Material)
    {
        public Vector3 Centre => (Min + Max) / 2;
        public string MaterialName => Material?.Name ?? "";
        public string Shader => Material?.ShaderName ?? "";
    }

    /// <summary>
    /// What a traffic model needs to know of a car's geometry, ported from Race Explorer's <c>kn5lod.py</c>: every mesh
    /// in the car's space (each node's transform applied: without them a car whose LOD keeps its parts in separate
    /// nodes measures 5.4 m tall), which of them are seen from outside, where the car's lowest point is, and which small
    /// meshes at either end are its lamps.
    /// </summary>
    public static class TrafficGeometry
    {
        // Names that mark a mesh as the inside of the car or a state of it even when its box would pass: a roll cage
        // reaches the roof, a damaged bumper the end
        private static readonly Regex Never = new(
            @"(cockpit|interior|steer|seat|dash|pedal|gauge|needle|carpet|shadow|damage|dmg|driver|helmet|arm_|arms|glove|" +
            @"cage|rollbar|roll_bar|floor_int|wiper_int|_int$|^int_|_int_|cinture|belt|dmg_)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lamp words in the languages modders write in. A bare "head", "tail" or "brake" is left out: those catch brake
        // discs and wheel hubs
        private static readonly Regex LampWords = new(
            @"(light|lamp|lampa|faro|fari|luce|luci|phare|feu|farol|lanterna|fanal|scheinwerfer|licht|swiatlo|fonar|led\b|led_|_led|" +
            @"blinker|indicator|headl|taill|stopl|brakel|brake_l|rearl|rear_l|frontl|front_l|nor_f|nor_r)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NotLamp = new(
            @"(interior|dash|gauge|needle|switch|plate|number|logo|badge|mirror|wheel|tyre|tire|rim|caliper|disc|disk|rotor|hub|" +
            @"susp|wiper|bumper|hood|bonnet|door|detail|bolt|grill|_int_|cockpit|steer|panel|reflect|dmg|damage|void|shadow|" +
            @"cover|housing|chrome|bezel|trim|ring|bulb_holder|lens_cover)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Glass = new(@"(glass|vetro|cristal|verre|glas|steklo)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BrakeOrStop = new(@"(brake|stop)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The model's plain meshes (not the skinned ones: a driver), each placed in the model's space</summary>
        public static List<Kn5Mesh> Meshes(IKn5 model, bool skinned = false)
        {
            var meshes = new List<Kn5Mesh>();
            var path = new List<string>();
            void Walk(Kn5Node node, Matrix4x4 parent)
            {
                path.Add(node.Name ?? "");
                if (node.NodeClass == Kn5NodeClass.Base)
                {
                    var world = Matrices.ToNumerics(node.Transform) * parent;
                    foreach (var child in node.Children) Walk(child, world);
                }
                else if ((node.NodeClass == Kn5NodeClass.Mesh || skinned) && node.Vertices is { Length: > 0 } vertices)
                {
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    foreach (var v in vertices)
                    {
                        var p = Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), parent);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    var names = path.ToArray();
                    Array.Reverse(names);
                    meshes.Add(new Kn5Mesh(node, parent, min, max, names, model.GetMaterial(node.MaterialId)));
                }
                path.RemoveAt(path.Count - 1);
            }

            if (model.RootNode != null) Walk(model.RootNode, Matrix4x4.Identity);
            return meshes;
        }

        /// <summary>The meshes drawn and seen: active, renderable, not the inside of the car or a state of it by name</summary>
        public static List<Kn5Mesh> Visible(IEnumerable<Kn5Mesh> meshes) =>
            meshes.Where(m => m.Node.Active && m.Node.IsRenderable && !m.Path.Any(n => Never.IsMatch(n)) && !Never.IsMatch(m.MaterialName)).ToList();

        /// <summary>The box round them all; null for none</summary>
        public static (Vector3 Min, Vector3 Max)? Envelope(IEnumerable<Kn5Mesh> meshes)
        {
            (Vector3 Min, Vector3 Max)? box = null;
            foreach (var m in meshes) box = box is var (a, b) ? (Vector3.Min(a, m.Min), Vector3.Max(b, m.Max)) : (m.Min, m.Max);
            return box;
        }

        /// <summary>Every vertex of the model, skinned meshes too</summary>
        public static int Vertices(IKn5 model) => model.Nodes.Where(n => n.NodeClass != Kn5NodeClass.Base).Sum(n => n.Vertices?.Length ?? 0);

        /// <summary>The model's box over every mesh (skinned ones too); null for an empty model</summary>
        public static (Vector3 Min, Vector3 Max)? Measure(IKn5 model) => Envelope(Meshes(model, skinned: true));

        /// <summary>
        /// The lowest point of the meshes seen, in the model's space, and the mesh that sets it: what CSP puts on the
        /// road, as it has no ride-height of its own. Null for a model with none.
        /// </summary>
        public static (float Y, string Mesh)? Lowest(IKn5 model)
        {
            var low = Visible(Meshes(model)).MinBy(m => m.Min.Y);
            return low == null ? null : (low.Min.Y, low.Node.Name ?? "");
        }

        /// <summary>The lamps found, each list of mesh names: front lamps, rear lamps, brake lamps, and lamp glass at either end</summary>
        public sealed record Lamps(List<string> Headlights, List<string> Rear, List<string> Brakes, List<string> GlassFront, List<string> GlassRear, List<string> Maybe);

        /// <summary>
        /// The small meshes at the very front or rear whose name or material says lamp, or that glow (an emissive shader).
        /// Front and rear are told by where the mesh is against the car's ends, never by the name alone (the Muravey's
        /// dash tell-tales are lamp_left and lamp_right). Glass is listed apart: the shipped cars light the bulb mesh, and
        /// the glass only when there is nothing else.
        /// </summary>
        public static Lamps FindLamps(IKn5 model)
        {
            var found = new Lamps([], [], [], [], [], []);
            var visible = Visible(Meshes(model));
            if (Envelope(visible) is not var (lo, hi)) return found;

            var length = hi.Z - lo.Z;
            var width = hi.X - lo.X;
            var height = hi.Y - lo.Y;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mesh in visible)
            {
                var name = mesh.Node.Name ?? "";
                if (seen.Contains(name)) continue;
                var text = name + " " + mesh.MaterialName;
                var emissive = mesh.Shader.Contains("emissive", StringComparison.OrdinalIgnoreCase);
                if (!LampWords.IsMatch(text) && !emissive) continue;
                if (NotLamp.IsMatch(text)) continue;

                var centre = mesh.Centre;
                var small = mesh.Max.X - mesh.Min.X < 0.8 * width && mesh.Max.Z - mesh.Min.Z < 0.35 * length;
                var atFront = hi.Z - mesh.Max.Z < 0.25 * length && centre.Z > lo.Z + 0.65 * length;
                var atRear = mesh.Min.Z - lo.Z < 0.25 * length && centre.Z < lo.Z + 0.35 * length;
                var lowEnough = lo.Y + 0.12 * height < centre.Y && centre.Y < lo.Y + 0.8 * height;
                seen.Add(name);
                if (!(small && lowEnough && (atFront || atRear)))
                {
                    found.Maybe.Add(name);
                    continue;
                }

                if (Glass.IsMatch(text))
                {
                    if (atFront && !atRear) found.GlassFront.Add(name);
                    else if (atRear && !atFront) found.GlassRear.Add(name);
                    else found.Maybe.Add(name);
                    continue;
                }

                if (atFront && !atRear) found.Headlights.Add(name);
                else if (atRear && !atFront) (BrakeOrStop.IsMatch(text) ? found.Brakes : found.Rear).Add(name);
                else found.Maybe.Add(name);
            }
            return found;
        }

        /// <summary>
        /// The lamps as a traffic model's <c>lights</c> lists them: the front lamps (else the front glass); then brake and
        /// rear lamps apart when a mesh says brake, else the rear lamps (else the rear glass) doing both, which on a period
        /// car is usually true, one filament for tail and stop
        /// </summary>
        public static Dictionary<string, List<string>> Lights(Lamps lamps)
        {
            var heads = (lamps.Headlights.Count > 0 ? lamps.Headlights.Take(8) : lamps.GlassFront.Take(6)).ToList();
            if (lamps.Brakes.Count > 0)
                return new() { ["headlights"] = heads, ["brakes"] = lamps.Brakes.Take(6).ToList(), ["rear"] = lamps.Rear.Take(8).ToList() };
            var rear = (lamps.Rear.Count > 0 ? lamps.Rear.Take(8) : lamps.GlassRear.Take(6)).ToList();
            return new() { ["headlights"] = heads, ["rearCombined"] = rear };
        }
    }
}
