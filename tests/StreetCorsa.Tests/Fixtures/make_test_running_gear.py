# Writes the tests' own running gear (step 31's logic, held without any mod's parts) into Fixtures/test_running_gear: rear
# axles with their carriers, gears and shafts, drum brakes on their backing plates with shoes, wheel cylinders and a front
# hub, disc brakes, leaf springs with traction bars, torsion bars, sway bars and shocks, as the parts of a period would
# be; and the plain parts a car that no part's came_in names gets (stock discs, coil springs, shocks). Made-up names,
# round figures where the logic does not care; every running_gear part is drawn with the one stand-in beside it (an
# empty KN5), the plain coils with a coil's stand-in by models.json and the other plain parts with none. Owned by the
# tests: change a figure here and run it again (python -I make_test_running_gear.py), never copy a mod's parts in.
#
# The packs:
# - test_axles: four housings, each with its carriers (open, posi, welded: an open carrier's spiders weld into the
#   welded one), its ring and pinion sets and its shafts. "light" came in the gm family's cars up to 460 Nm, its carriers
#   in two series of gears (low and high); "heavy" the gm family's from 460 Nm and 1965; "ford" the ford family's from
#   380 Nm and 1957, on two shafts by torque; "any" in any car up to 700 Nm (a make no axle names gets it);
# - test_axle_speed: the light axle's spools, which no car came with;
# - test_drums: drums sized by the car's weight (front and rear, American cars to 1972), the backing plate each size goes
#   on, organic shoes (the factory's) and two aftermarket linings, the stock and a big-bore wheel cylinder, the front hub;
# - test_discs: a single-piston front disc for American cars from 1969 and three dealer's options (one each for the
#   gm, ford and mopar families), none of them a factory part but by cars.json's pick;
# - test_leaves: rear leaf springs of American cars' live axles, named by their rate in lb/in (force = rate * 175.1
#   N/m), a mono-leaf that winds up more, traction bars;
# - test_torsion: the mopar family's front torsion bars by the car's weight, and a heavy-duty one;
# - test_sway: front and rear sway bars named by their diameter (in ten-thousandths), the heavy-duty ones aftermarket;
# - test_shocks: drag shocks for each axle and an adjustable one, all aftermarket;
# - brakes/test_stock and suspension/test_stock: the plain discs, coil springs and shocks, no came_in.
import json, os, shutil, struct

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "test_running_gear")
PARTS = {}
STAND_IN = "stand_in.kn5"
COIL = "running_gear/test_coils/coil.kn5"


def stand_in():
    """the tests' stand-in model: a KN5 of one 10 cm box (sc6969, version 5, no texture, one plain material, a root node
    and the box's mesh under it), as AcTools writes one; what it looks like does not matter, only that it has a size"""
    def text(t):
        return struct.pack("<i", len(t)) + t.encode("ascii")
    s = 0.05
    corners = [(-s, -s, -s), (s, -s, -s), (s, s, -s), (-s, s, -s), (-s, -s, s), (s, -s, s), (s, s, s), (-s, s, s)]
    faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5]
    out = b"sc6969" + struct.pack("<iii", 5, 0, 1)
    out += text("stand_in") + text("ksPerPixel") + b"\x00\x00" + struct.pack("<iii", 0, 0, 0)
    out += struct.pack("<i", 1) + text("stand_in") + struct.pack("<i", 1) + b"\x01" + struct.pack("<16f", 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
    out += struct.pack("<i", 2) + text("box") + struct.pack("<i", 0) + b"\x01" + b"\x00\x01\x00" + struct.pack("<i", len(corners))
    for x, y, z in corners:
        out += struct.pack("<11f", x, y, z, 0, 1, 0, 0, 0, 1, 0, 0)
    out += struct.pack("<i", len(faces)) + struct.pack(f"<{len(faces)}H", *faces)
    out += struct.pack("<ii", 0, 0) + struct.pack("<6f", 0, 0, 0, 0, 0, s * 3 ** 0.5) + b"\x01"
    return out


def part(pid, display, cls, specs=None, came_in=None, slots=None, needs=None, aftermarket=False, mass=0, **extra):
    p = {"id": pid, "display_name": display, "class": cls}
    if aftermarket: p["aftermarket"] = True
    p["mass"] = mass
    if specs: p["specs"] = specs
    if needs: p["needs"] = [{"slot": s, "message": m} for s, m in needs]
    if came_in is not None: p["came_in"] = came_in
    if pid.startswith("running_gear/"): p["model"] = STAND_IN
    p.update(extra)
    p["slots"] = slots or [mount()]
    PARTS[pid] = p
    return p


def mount(fits=None, position=(0, 0, 0)):
    s = {"id": 1, "name": "mount", "position": list(position)}
    if fits: s["fits"] = [fits]
    return s


def slot(sid, name, takes=None, position=(0, 0, 0), rotation=None):
    s = {"id": sid, "name": name, "position": list(position)}
    if rotation: s["rotation"] = list(rotation)
    if takes: s["takes"] = [takes]
    return s


# ----- rear axles -----------------------------------------------------------------------------------------------------

AXLES = "running_gear/test_axles/"
FLIP = (3.14159265, 0, 0)


def housing(name, display, mass, strength, drag, came_in):
    part(AXLES + name, display, "rear_axle", {"strength": strength, "drag": drag}, came_in, mass=mass,
         needs=[(2, "The rear axle needs its differential carrier."), (3, "The rear axle needs its left axle shaft."),
                (4, "The rear axle needs its right axle shaft.")],
         slots=[slot(1, "mount"), slot(2, "carrier", f"axle:{name}"),
                slot(3, "left axle shaft", f"shaft:{name}", (0.38, 0, 0)),
                slot(4, "right axle shaft", f"shaft:{name}", (-0.38, 0, 0), FLIP),
                slot(5, "left rear brake", position=(0.62, 0, 0)), slot(6, "right rear brake", position=(-0.62, 0, 0), rotation=FLIP),
                slot(7, "left rear spring", position=(0.47, -0.1, 0)), slot(8, "right rear spring", position=(-0.47, -0.1, 0)),
                slot(9, "left rear shock", position=(0.51, -0.05, 0)), slot(10, "right rear shock", position=(-0.51, -0.05, 0), rotation=FLIP),
                slot(11, "rear sway bar", position=(0, 0, -0.15))])


def carrier(pid, display, axle, gears, power, coast, preload, strength, mass, came_in=None, becomes=None, aftermarket=False, price_lock=None):
    specs = {"diff_power": power, "diff_coast": coast, "diff_preload": preload, "strength": strength}
    if price_lock: specs["price_lock"] = price_lock
    extra = {"becomes": {"weld_spiders": becomes}} if becomes else {}
    part(pid, display, "differential", specs, came_in, mass=mass, aftermarket=aftermarket,
         needs=[(2, "The differential needs its ring and pinion.")],
         slots=[mount(f"axle:{axle}"), slot(2, "ring and pinion", f"gears:{gears}")], **extra)


def axle(name, label, mass, strength, drag, came_in, series, carrier_strength, gear_strength, shafts):
    """a housing with its carriers (open, posi, welded) for each series of gears, the gears and the shafts"""
    housing(name, f"{label} rear axle housing", mass, strength, drag, came_in)
    for suffix, ratios in series.items():
        gears = name + (f"_{suffix}" if suffix else "")
        tail = f"_{suffix}" if suffix else ""
        which = f"{suffix} gears, " if suffix else ""
        carrier(f"{AXLES}{name}_open{tail}", f"Open carrier, {which}({label.lower()} axle)", name, gears, 0, 0, 0,
                carrier_strength, 11, came_in={}, becomes=f"{AXLES}{name}_welded{tail}")
        carrier(f"{AXLES}{name}_posi{tail}", f"Posi carrier, {which}({label.lower()} axle)", name, gears, 0.25, 0.15, 50,
                round(carrier_strength * 1.06), 13, came_in={})
        carrier(f"{AXLES}{name}_welded{tail}", f"Welded carrier, {which}({label.lower()} axle)", name, gears, 1.0, 1.0, 0,
                round(carrier_strength * 0.85), 11, aftermarket=True, price_lock=0.45)
        for ratio in ratios:
            part(f"{AXLES}{name}_gears_{round(ratio * 100)}", f"{ratio:.2f} ring and pinion ({label.lower()} axle)", "ring_pinion",
                 {"ratio": ratio, "strength": gear_strength(ratio)}, {}, mass=9, slots=[mount(f"gears:{gears}")])
    for shaft, (strength_of, splines, shaft_came_in) in shafts.items():
        part(f"{AXLES}{name}_shaft_{splines}", f"{splines}-spline axle shaft ({label.lower()} axle)", "axle_shaft",
             {"strength": strength_of, "splines": splines}, shaft_came_in, mass=8, slots=[mount(f"shaft:{name}")])


def steady(strength, from_ratio=99.0):
    """a set's strength: the same up to a ratio, less on the shorter sets past it (a smaller pinion)"""
    return lambda ratio: strength if ratio <= from_ratio else round(strength * from_ratio / ratio)


axle("light", "Light", 66, 4600, 0.01, {"families": ["gm"], "torque": [None, 460]},
     {"low": [2.56, 2.73, 3.08], "high": [3.36, 3.55, 3.73, 4.11]}, 3910, steady(4830), {"": (4800, 28, {})})
axle("heavy", "Heavy", 78, 8000, 0.01, {"families": ["gm"], "torque": [460, None], "years": [1965, None]},
     {"low": [2.73, 3.08, 3.31, 3.55], "high": [3.73, 4.10, 4.56, 4.88]}, 6800, steady(8400, 4.1), {"": (7600, 30, {})})
axle("ford", "Ford", 90, 10500, 0.035, {"families": ["ford"], "torque": [380, None], "years": [1957, None]},
     {"": [2.75, 3.00, 3.25, 3.50, 3.70, 4.11, 4.30, 4.57, 4.86, 5.14]}, 8925, steady(11025, 4.11),
     {"28": (7600, 28, {"torque": [None, 550]}), "31": (10000, 31, {"torque": [550, None]})})
axle("any", "Plain", 72, 6400, 0.012, {"torque": [None, 700]},
     {"": [2.87, 3.07, 3.31, 3.54, 3.73, 4.09, 4.56]}, 5440, steady(6720, 4.09), {"": (6600, 30, {})})

SPEED = "running_gear/test_axle_speed/"
for series in ("low", "high"):
    carrier(f"{SPEED}light_spool_{series}", f"Spool carrier, {series} gears (light axle)", "light", f"light_{series}",
            1.0, 1.0, 0, 6900, 9, aftermarket=True)

# ----- drum and disc brakes ---------------------------------------------------------------------------------------------

DRUMS = "running_gear/test_drums/"
SIZES = {"9": 0.1143, "9_5": 0.1206, "10": 0.127, "11": 0.1397}


def drum(name, display, size, force, width, torque_k, mass_range, front):
    came_in = {"countries": ["USA"], "axle": "front" if front else "rear", "mass": list(mass_range)}
    if front: came_in["years"] = [None, 1972]
    specs = {"force": force, "friction": 1.0, "radius": SIZES[size], "calipers": 1, "width_in": width, "torque_k": torque_k,
             "cool_transfer": 0.003 if front else 0.0025, "cool_speed_factor": 0.005 if front else 0.004}
    slots = [mount(f"drum:{size}", (-0.08, 0, 0))]
    if front: slots.append(slot(5, "hub", "hub:front"))
    part(DRUMS + name, display, "drum_brake", specs, came_in, slots=slots,
         needs=[(5, "The front drum needs its hub.")] if front else None)


drum("front_9x2", "9 x 2-inch front drum brake", "9", 16623, 2.0, 1.05, (None, 1250), True)
drum("front_9_5x2_5", "9.5 x 2.5-inch front drum brake", "9_5", 18242, 2.5, 1.0, (1150, 1550), True)
drum("front_10x2_5", "10 x 2.5-inch front drum brake", "10", 18898, 2.5, 0.95, (1300, 1700), True)
drum("front_11x2_75", "11 x 2.75-inch front drum brake", "11", 18969, 2.75, 0.9, (1600, 2100), True)
drum("front_11x3", "Heavy-duty 11 x 3-inch front drum brake", "11", 20401, 3.0, 0.85, (1900, None), True)
drum("rear_9x1_75", "9 x 1.75-inch rear drum brake", "9", 13123, 1.75, 1.15, (None, 1250), False)
drum("rear_9_5x2", "9.5 x 2-inch rear drum brake", "9_5", 14096, 2.0, 1.1, (1150, 1550), False)
drum("rear_10x2", "10 x 2-inch rear drum brake", "10", 14567, 2.0, 1.05, (1300, 1700), False)
drum("rear_11x2", "11 x 2-inch rear drum brake", "11", 14316, 2.0, 1.0, (1600, 2100), False)
drum("rear_11x2_5", "Heavy-duty 11 x 2.5-inch rear drum brake", "11", 15748, 2.5, 0.95, (1900, None), False)

for axle_name in ("front", "rear"):
    for size in SIZES:
        inches = size.replace("_", ".")
        part(f"{DRUMS}plate_{axle_name}_{size}", f"{inches}-inch {axle_name} brake backing plate", "backing_plate",
             {"fits_axle": axle_name}, {},
             needs=[(2, "The drum brake needs its shoes."), (3, "The drum brake needs its wheel cylinder."), (4, "The backing plate needs its drum.")],
             slots=[slot(1, "mount"), slot(2, "shoes", "shoes:drum", (0.08, 0, 0)), slot(3, "wheel cylinder", "wcyl:drum", (0.08, 0, 0)),
                    slot(4, "drum", f"drum:{size}")])

part(DRUMS + "shoes_organic", "Organic brake shoes", "brake_shoes",
     {"friction": 1.0, "perf_curve": "(|0=0.90|120=1.0|230=1.0|300=0.80|380=0.55|480=0.35|650=0.25|)"}, {}, slots=[mount("shoes:drum")])
part(DRUMS + "shoes_semi_metallic", "Semi-metallic brake shoes", "brake_shoes",
     {"friction": 0.95, "perf_curve": "(|0=0.85|150=1.0|320=1.0|420=0.80|520=0.55|700=0.35|)"}, aftermarket=True, slots=[mount("shoes:drum")])
part(DRUMS + "shoes_sintered", "Sintered metallic brake shoes", "brake_shoes",
     {"friction": 0.9, "perf_curve": "(|0=0.70|150=0.85|300=1.0|480=1.0|600=0.85|750=0.60|)"}, aftermarket=True, slots=[mount("shoes:drum")])
part(DRUMS + "wheel_cylinder_stock", "Stock wheel cylinder", "wheel_cylinder", {"force_factor": 1.0}, {}, slots=[mount("wcyl:drum")])
part(DRUMS + "wheel_cylinder_big_bore", "Big-bore wheel cylinder", "wheel_cylinder", {"force_factor": 1.1}, aftermarket=True,
     slots=[mount("wcyl:drum")])
part(DRUMS + "front_hub", "Front wheel hub", "hub", came_in={}, slots=[mount("hub:front")])

DISCS = "running_gear/test_discs/"
DISC_CURVE = "(|0=0.80|150=0.95|250=1.0|500=1.0|650=0.85|800=0.60|1000=0.35|)"


def disc(name, display, force, radius, calipers, came_in):
    part(DISCS + name, display, "disc_brake",
         {"force": force, "friction": 1.0, "radius": radius, "calipers": calipers, "torque_k": 0.55, "cool_transfer": 0.008,
          "cool_speed_factor": 0.014, "perf_curve": DISC_CURVE}, came_in,
         slots=[mount(position=(-0.08, 0, 0)), slot(5, "hub", "hub:front")])


disc("single_piston", "Single-piston front disc brake", 24843, 0.1187, 1, {"countries": ["USA"], "axle": "front", "years": [1969, None]})
disc("option_gm", "Four-piston front disc brake (gm option)", 19585, 0.1187, 2, {"families": ["gm"], "axle": "front", "years": [1965, 1968], "option": True})
disc("option_ford", "Four-piston front disc brake (ford option)", 18759, 0.122, 2, {"families": ["ford"], "axle": "front", "years": [1965, 1967], "option": True})
disc("option_mopar", "Four-piston front disc brake (mopar option)", 18885, 0.1192, 2, {"families": ["mopar"], "axle": "front", "years": [1966, 1968], "option": True})

# ----- springs, bars and shocks ---------------------------------------------------------------------------------------

LEAVES = "running_gear/test_leaves/"
LEAF_CAME_IN = {"countries": ["USA"], "axle": "rear", "suspension": ["AXLE"]}


def leaf(name, display, rate, wrap, leaves, came_in=None, aftermarket=False):
    part(LEAVES + name, display, "leaf_spring",
         {"force": round(rate * 175.1), "wrap": wrap, "leaves": leaves, "width_in": 2.5, "fits_axle": "rear", "fits_suspension": "AXLE"},
         came_in, aftermarket=aftermarket, slots=[slot(1, "mount"), slot(2, "traction bar", "traction:leaf")])


for rate, leaves in ((90, 3), (105, 3), (120, 4), (135, 4), (150, 5), (170, 5), (190, 6), (215, 6), (240, 6)):
    leaf(f"rear_{rate}", f"{rate} lb/in {leaves}-leaf rear spring", rate, 1.0, leaves, LEAF_CAME_IN)
leaf("mono_110", "110 lb/in mono-leaf rear spring", 110, 1.3, 1, aftermarket=True)
for name, display, factor in (("ladder", "Ladder bars", 0.25), ("slapper", "Slapper bars", 0.5), ("traction_bars", "Traction bars", 0.6)):
    part(LEAVES + name, display, "traction_bar", {"wrap_factor": factor}, aftermarket=True, slots=[mount("traction:leaf")])

TORSION = "running_gear/test_torsion/"
for name, diameter, force, mass_range in (("bar_85", 0.85, 16316, (None, 1350)), ("bar_87", 0.87, 17907, (1250, 1550)),
                                          ("bar_89", 0.89, 19611, (1450, 1700)), ("bar_92", 0.92, 22392, (1650, None))):
    part(TORSION + name, f"{diameter:.2f}-inch torsion bar", "torsion_bar",
         {"force": force, "diameter_in": diameter, "fits_axle": "front", "fits_suspension": "DWB"},
         {"families": ["mopar"], "axle": "front", "suspension": ["DWB"], "mass": list(mass_range), "years": [1957, None]})
part(TORSION + "bar_100", "Heavy-duty 1.00-inch torsion bar", "torsion_bar",
     {"force": 31257, "diameter_in": 1.0, "fits_axle": "front", "fits_suspension": "DWB"}, aftermarket=True)

SWAY = "running_gear/test_sway/"
for axle_name, bars in (("front", ((6875, 3049), (7500, 4318), (8125, 5948), (8750, 8000), (9375, 10542), (10000, 13648))),
                        ("rear", ((5625, 551), (6250, 840), (6875, 1230), (7500, 1742), (8750, 3227)))):
    for size, rate in bars:
        part(f"{SWAY}{axle_name}_{size}", f"{size / 10000:.4f}-inch {axle_name} sway bar", "sway_bar",
             {"arb_rate": rate, "diameter_in": size / 10000, "fits_axle": axle_name}, {})
for axle_name, size, rate in (("front", 11250, 21861), ("front", 12500, 33319), ("rear", 11250, 8819)):
    part(f"{SWAY}{axle_name}_{size}", f"Heavy-duty {size / 10000:.4f}-inch {axle_name} sway bar", "sway_bar",
         {"arb_rate": rate, "diameter_in": size / 10000, "fits_axle": axle_name}, aftermarket=True)

SHOCKS = "running_gear/test_shocks/"
part(SHOCKS + "drag_90_10", "90/10 front drag shock absorber", "shock", {"damping": 4200, "rebound_factor": 0.15, "fits_axle": "front"}, aftermarket=True)
part(SHOCKS + "drag_50_50", "50/50 rear drag shock absorber", "shock", {"damping": 3600, "rebound_factor": 1.0, "fits_axle": "rear"}, aftermarket=True)
part(SHOCKS + "adjustable", "Adjustable shock absorber", "shock", {"damping": 3700, "rebound_factor": 1.5}, aftermarket=True)

# ----- the plain parts ------------------------------------------------------------------------------------------------

STOCK_BRAKES = "brakes/test_stock/"
for name, force, radius, calipers in (("disc_9_in", 20000, 0.118, 2), ("disc_10_in", 18639, 0.1275, 2), ("disc_11_in", 20000, 0.142, 2),
                                      ("disc_12_in", 22000, 0.155, 3)):
    part(STOCK_BRAKES + name, f"{name[5:].replace('_', '-')} disc brake", "disc_brake",
         {"force": force, "friction": 1.0, "radius": radius, "calipers": calipers})

STOCK = "suspension/test_stock/"
for rate, load in ((150, 330), (200, 400), (250, 480), (300, 560)):
    part(f"{STOCK}coil_{rate}", f"{rate} lb/in coil spring", "spring", {"force": round(rate * 175.1), "design_load": load},
         slots=[{"id": 1, "name": "mount", "position": [0, -0.4, 0]}])
for damping, rebound in ((2500, 1.3), (3000, 1.33), (3500, 1.3), (4500, 1.25)):
    part(f"{STOCK}shock_{damping}", f"Shock absorber ({damping} N s/m)", "shock", {"damping": damping, "rebound_factor": rebound})


if __name__ == "__main__":
    shutil.rmtree(OUT, ignore_errors=True)
    for pid, p in PARTS.items():
        path = os.path.join(OUT, *pid.split("/")) + ".json"
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(p, f, indent=2)
            f.write("\n")
    for folder in sorted({os.path.dirname(pid) for pid, p in PARTS.items() if p.get("model")}):
        with open(os.path.join(OUT, *folder.split("/"), STAND_IN), "wb") as f:
            f.write(stand_in())
    # The plain coil springs, which have no model of their own, are drawn with a coil by models.json, as a mod gives its
    # parts models of its own
    os.makedirs(os.path.join(OUT, *os.path.dirname(COIL).split("/")), exist_ok=True)
    with open(os.path.join(OUT, *COIL.split("/")), "wb") as f:
        f.write(stand_in())
    with open(os.path.join(OUT, "models.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"models": {STOCK + "coil_*": COIL}}, f, indent=2)
        f.write("\n")
    print(len(PARTS), "parts")
