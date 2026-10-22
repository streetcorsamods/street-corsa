# Writes the tests' own engines (roadmap step 38) into Fixtures/test_engines: one small pack per kind of engine the game
# supports, each with only the parts the logic needs, round figures, no models. Owned by the tests: change a figure here
# and run it again (python -I make_test_engines.py), never copy a mod's parts in.
#
# The kinds:
# - test_v8: a V8 with two heads, left and right valve covers and headers, a belt-driven water pump and fan, a steering
#   pump and its belt, a radiator with an electric fan as an alternative, the base game's battery that no slot takes; a
#   factory build, a rated build, a big block's factory build (a bigger engine of the family, on a block of its own), a
#   stroker kit on the small block with the speed parts' top end, an aftermarket (speed-shop) build, a blown build (a
#   roots blower with the stock carburettor on it) and a nitrous build. The speed parts make power on the dyno (a stroker
#   crank only with its pistons) and so cost more than the parts they replace; nitrous makes none, as the dyno has it.
#   An engine short of a part (its water pump, its fan) is made in the test that needs it, never a build here: every
#   build is one the game may hand out;
# - test_six: an inline six with one head carrying the intake, its distributor on the side of the block;
# - test_flathead: a flathead V8 with row intakes whose pads are all enforced (one, two and four carburettors on carb:97
#   pads), a Stromberg-like carburettor, an oil bath cleaner and an open velocity stack that never clogs;
# - test_four: the smallest engine, an inline four with a front-drive gearbox, of no make (its family is its block's
#   name's).
#
# Names: the parts are named as real packs name them ("timing chain cover", "harmonic balancer", "power steering pump
# belt"), so the work order's kinds are found by its names in their order, not by a name that is a kind's own. Makes: a
# pack's parts carry its make, but those that mount on a pad any engine has (carburettors, air cleaners, a blower), as
# the base game's universal parts do.
#
# Figures: every bottom end puts the piston flush with the deck (deck = stroke / 2 + rod + pin height), so the chamber
# alone is the clearance volume and the compression reads straight off it.
import json, os, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "test_engines")
BUILDS = []


class Pack:
    def __init__(self, ns, make, prefix):
        self.ns, self.make, self.prefix, self.parts = ns, make, prefix, {}

    def id(self, name):
        return f"engines/{self.ns}/{name}"

    def fit(self, part, what):
        """the pack's own fitting: taken by `part`'s slot for `what`"""
        return f"{self.ns}:{part}/{what}"

    def add(self, name, display, cls=None, mass=1.0, specs=None, roles=None, slots=(), needs=(), **extra):
        """a part of the pack, of its make as a real pack's are; but one that mounts on a pad any engine has (a carburettor,
        an air cleaner, a blower) is of none, as the base game's universal parts are"""
        p = {"id": self.id(name), "display_name": display if display[0].isupper() else f"{self.prefix} {display}"}
        shared = any(not f.startswith(self.ns + ":") for s in slots for f in s.get("fits", []))
        if self.make and not shared: p["make"] = self.make
        if cls: p["class"] = cls
        p["mass"] = mass
        if specs: p["specs"] = specs
        if roles: p["slot_roles"] = roles
        if needs: p["needs"] = [{"slot": s, "message": f"the engine is missing the {m}."} for s, m in needs]
        p["slots"] = list(slots)
        p.update(extra)
        self.parts[name] = p
        return name

    def build(self, bid, name, origin, parts, rated=None, aftermarket=False):
        b = {"id": bid, "name": name, "origin": origin}
        if rated: b["rated_power"] = rated
        if aftermarket: b["aftermarket"] = True
        b["parts"] = [self.id(p) if "/" not in p else p for p in parts if p]
        BUILDS.append(b)


def slot(sid, name, fits=(), takes=(), at=(0.0, 0.0, 0.0)):
    s = {"id": sid, "name": name, "position": list(at), "rotation": [0.0, 0.0, 0.0]}
    if fits: s["fits"] = list(fits)
    if takes: s["takes"] = list(takes)
    return s


def bottom_end(k, stroke, rod, pin, crank_max=8000, extra_crank_slots=()):
    """crankshaft, rods and pistons on block slot 8; the crank's timing gear (32) and pulley (33)"""
    k.add("crankshaft", f"crankshaft ({stroke} mm stroke)", "crankshaft", 20, {"stroke": stroke, "max_rpm": crank_max, "inertia": 0.03},
          {"connecting_rods": 40, "flywheel": 6},
          [slot(7, "mount", fits=[k.fit("block", "crankshaft")]), slot(40, "rods", takes=[k.fit("crankshaft", "connecting_rods")]),
           slot(6, "flywheel", takes=[k.fit("crankshaft", "flywheel")]), slot(32, "timing gear", takes=[k.fit("crankshaft", "timing_gear")]),
           slot(33, "pulley", takes=[k.fit("crankshaft", "pulley")]), slot(34, "damper", takes=[k.fit("crankshaft", "damper")]), *extra_crank_slots])
    k.add("harmonic_damper", "harmonic balancer", "engine_part", 3, slots=[slot(34, "mount", fits=[k.fit("crankshaft", "damper")])])
    k.add("connecting_rods", f"connecting rods ({rod} mm)", "connecting_rods", 5, {"rod_length": rod, "max_rpm": 9000, "inertia": 0.015},
          {"pistons": 42}, [slot(41, "mount", fits=[k.fit("crankshaft", "connecting_rods")]), slot(42, "pistons", takes=[k.fit("connecting_rods", "pistons")])])
    k.add("pistons", "pistons", "pistons", 4, {"pin_height": pin, "dome": 0, "max_rpm": 8000, "inertia": 0.015},
          slots=[slot(42, "mount", fits=[k.fit("connecting_rods", "pistons")])])
    k.add("crankshaft_timing_gear", "crankshaft timing gear", "engine_part", 0.5, slots=[slot(32, "mount", fits=[k.fit("crankshaft", "timing_gear")])])
    k.add("crankshaft_pulley", "crankshaft pulley", "pulley", 1, {"max_rpm": 100000, "inertia": 0.0003},
          slots=[slot(33, "mount", fits=[k.fit("crankshaft", "pulley")])])


def camshaft(k, name="camshaft", display="camshaft", on="block", lift=(12.0, 16.0), duration=(230, 290), aftermarket=False):
    extra = {"aftermarket": True} if aftermarket else {}
    k.add(name, display, "camshaft", 8, {"lobe_separation": 212, "intake_lift": lift[0], "exhaust_lift": lift[1], "intake_duration": duration[0],
                                         "exhaust_duration": duration[1], "max_rpm": 12000, "inertia": 0.008},
          slots=[slot(10, "mount", fits=[k.fit(on, "camshaft")]), slot(5, "timing gear", takes=[k.fit("camshaft", "timing_gear")])], **extra)


def timing(k):
    k.add("camshaft_timing_gear", "camshaft timing gear", "engine_part", 0.5,
          slots=[slot(5, "mount", fits=[k.fit("camshaft", "timing_gear")]), slot(22, "chain", takes=[k.fit("camshaft_timing_gear", "timing_chain")])])
    k.add("timing_chain", "timing chain", "engine_part", 0.5, slots=[slot(22, "mount", fits=[k.fit("camshaft_timing_gear", "timing_chain")])])
    k.add("timing_cover", "timing chain cover", "engine_part", 2, slots=[slot(56, "mount", fits=[k.fit("block", "timing_cover")])])


def driveline(k, gears=(2.5, 1.5, 1.0), drive="rear", final=3.3):
    k.add("oil_pan", "oil pan", "oil_pan", 4, {"friction": 0.0002, "capacity": 5}, slots=[slot(10, "mount", fits=[k.fit("block", "oil_pan")])])
    k.add("oil_filter", "oil filter", "engine_part", 0.5, slots=[slot(55, "mount", fits=[k.fit("block", "oil_filter")])])
    k.add("flywheel", "flywheel", "flywheel", 12, {"max_rpm": 9000, "inertia": 0.036}, {"clutch": 4},
          [slot(2, "mount", fits=[k.fit("crankshaft", "flywheel")]), slot(4, "clutch", takes=[k.fit("flywheel", "clutch")])])
    k.add("clutch", "clutch", "clutch", 6, {"torque": 1300, "max_rpm": 13000, "inertia": 0.008}, slots=[slot(6, "mount", fits=[k.fit("flywheel", "clutch")])])
    k.add("transmission", f"{len(gears)}-speed manual transmission", "transmission", 40,
          {"gears": list(gears), "reverse": 2.5, "final_drive": final, "drive": drive, "diff_lock": 0.2, "max_rpm": 9000, "inertia": 0.023},
          slots=[slot(5, "mount", fits=[k.fit("block", "transmission")])])


def cooling(k, alternator=True, steering=False):
    """a belt-driven water pump with its fan, the alternator's belt on it, a steering pump and its belt when asked; a
    radiator, and a radiator with an electric fan in its place"""
    slots = [slot(57, "mount", fits=[k.fit("block", "water_pump")]), slot(1, "fan", takes=[k.fit("water_pump", "fan")]),
             slot(11, "belt", takes=[k.fit("water_pump", "belt")])]
    if steering: slots.append(slot(69, "steering pump", takes=[k.fit("water_pump", "steering_pump")]))
    k.add("water_pump", "water pump", "engine_part", 3, slots=slots)
    k.add("fan", "4-blade fan", "engine_part", 1, slots=[slot(1, "mount", fits=[k.fit("water_pump", "fan")])])
    k.add("drive_belt", "alternator drive belt", "engine_part", 0.2, slots=[slot(1, "mount", fits=[k.fit("water_pump", "belt")])])
    if alternator: k.add("alternator", "alternator", "engine_part", 5, slots=[slot(20, "mount", fits=[k.fit("block", "alternator")])])
    if steering:
        k.add("steering_pump", "power steering pump", "engine_part", 3,
              slots=[slot(55, "mount", fits=[k.fit("water_pump", "steering_pump")]), slot(115, "belt", takes=[k.fit("steering_pump", "belt")])])
        k.add("steering_belt", "power steering pump belt", "engine_part", 0.2, slots=[slot(115, "mount", fits=[k.fit("steering_pump", "belt")])])
    k.add("radiator", "radiator", "engine_part", 8, slots=[slot(58, "mount", fits=[k.fit("block", "radiator")])])
    k.add("radiator_electric_fan", "aluminium radiator with electric fan", "engine_part", 9, slots=[slot(58, "mount", fits=[k.fit("block", "radiator")])])


def carb_and_cleaner(k, pad="carb:4bbl", air="air:single", name="4-bbl carburetor", flow=(40, 600), cleaner_flow=240, clogging=0.25, prefix="", fits=()):
    k.add(f"{prefix}carburetor", name, "fuel_system", 4, {"mixture_ratio": 12.5, "fuel_flow": flow[0], "air_flow": flow[1]},
          slots=[slot(10, "mount", fits=[pad, *fits]), slot(11, "air cleaner", takes=[air])])
    k.add(f"{prefix}air_cleaner", "air cleaner", "air_cleaner", 1, {"air_flow": cleaner_flow, "clogging": clogging},
          slots=[slot(11, "mount", fits=[air])])


# ---- test_v8 ------------------------------------------------------------------------------------------------------------
def v8():
    k = Pack("test_v8", "mopar", "Test Mopar V8")
    # Two blocks of one family taking the same parts: the small block (100 mm bore) and a big block (110 mm, revving to
    # 6500), the bigger engine a tuner swaps in
    for name, display, bore, mass, rev in (("block", "Test Mopar V8 block", 100.0, 80, 6000), ("big_block", "Test Mopar V8 big block", 110.0, 95, 6500)):
        k.add(name, display, "engine_block", mass,
              {"cylinders": 8, "bore": bore, "deck_height": 270.0, "idle_rpm": 800, "rev_limit": rev, "spark_advance": 30.0,
               "spark_advance_high": 25.0, "spark_rpm_low": 1500, "spark_rpm_high": 4000},
              {"crankshaft": 8, "transmission": 5, "oil_pan": 9, "intake_manifold": 14, "left_head": 45, "right_head": 46, "camshaft": 10},
              [slot(17, "mount"), slot(9, "oil pan", takes=[k.fit("block", "oil_pan")]), slot(5, "transmission", takes=[k.fit("block", "transmission")]),
               slot(45, "left head", takes=[k.fit("block", "left_head")], at=(0.2, 0.15, 0)),
               slot(46, "right head", takes=[k.fit("block", "right_head")], at=(-0.2, 0.15, 0)),
               slot(8, "crankshaft", takes=[k.fit("block", "crankshaft")]), slot(20, "alternator", takes=[k.fit("block", "alternator")], at=(0.15, 0.1, 0.3)),
               slot(14, "intake", takes=[k.fit("block", "intake_manifold")], at=(0, 0.25, 0)), slot(10, "camshaft", takes=[k.fit("block", "camshaft")]),
               slot(55, "oil filter", takes=[k.fit("block", "oil_filter")]), slot(56, "timing cover", takes=[k.fit("block", "timing_cover")], at=(0, 0, 0.35)),
               slot(57, "water pump", takes=[k.fit("block", "water_pump")], at=(0, 0.1, 0.4)), slot(58, "radiator", takes=[k.fit("block", "radiator")], at=(0, 0.1, 0.7)),
               slot(59, "distributor", takes=[k.fit("block", "distributor")], at=(0, 0.3, 0.3)), slot(60, "block cover", takes=[k.fit("block", "block_cover")]),
               slot(61, "starter", takes=[k.fit("block", "starter")], at=(-0.15, -0.1, -0.3)),
               slot(62, "fuel pump", takes=[k.fit("block", "fuel_pump")], at=(0.15, -0.05, 0.3)),
               slot(63, "ignition coil", takes=[k.fit("block", "coil")], at=(0.1, 0.3, 0.2))],
              [(20, "alternator"), (56, "timing cover"), (55, "oil filter"), (58, "radiator"), (57, "water pump"), (59, "ignition distributor"),
               (45, "left cylinder head"), (46, "right cylinder head"), (14, "intake"), (8, "crankshaft"), (9, "oil pan"), (5, "transmission")])
    # The bottom end: stock (80 mm stroke, 9.4:1 with the 75 cc chamber) and a stroker's crank and pistons (90 mm, 10.4:1)
    bottom_end(k, 80, 150, 80)
    k.add("stroker_crankshaft", "stroker crankshaft (90 mm stroke)", "crankshaft", 22, {"stroke": 90, "max_rpm": 8000, "inertia": 0.032},
          {"connecting_rods": 40, "flywheel": 6},
          [slot(7, "mount", fits=[k.fit("block", "crankshaft")]), slot(40, "rods", takes=[k.fit("crankshaft", "connecting_rods")]),
           slot(6, "flywheel", takes=[k.fit("crankshaft", "flywheel")]), slot(32, "timing gear", takes=[k.fit("crankshaft", "timing_gear")]),
           slot(33, "pulley", takes=[k.fit("crankshaft", "pulley")]), slot(34, "damper", takes=[k.fit("crankshaft", "damper")])], aftermarket=True)
    k.add("stroker_pistons", "stroker pistons", "pistons", 4, {"pin_height": 75, "dome": 0, "max_rpm": 8000, "inertia": 0.016},
          slots=[slot(42, "mount", fits=[k.fit("connecting_rods", "pistons")])], aftermarket=True)
    camshaft(k)
    camshaft(k, "speed_camshaft", "Racer speed camshaft (Test Mopar V8)", lift=(13.0, 17.5), duration=(250, 300), aftermarket=True)
    timing(k)
    k.add("block_cover", "block cover", "engine_part", 2, slots=[slot(60, "mount", fits=[k.fit("block", "block_cover")])])
    driveline(k)
    k.add("four_speed", "Test Mopar V8 4-speed manual transmission (close ratio)", "transmission", 45,
          {"gears": [2.5, 1.9, 1.5, 1.0], "reverse": 2.5, "final_drive": 3.5, "drive": "rear", "diff_lock": 0.3, "max_rpm": 9000, "inertia": 0.025},
          slots=[slot(5, "mount", fits=[k.fit("block", "transmission")])])
    # The heads, a bank each: 75 cc, a 50 mm intake and a 40 mm exhaust valve
    for side, sid, x in (("left", 45, 0.2), ("right", 46, -0.2)):
        k.add(f"{side}_head", f"{side} cylinder head", "cylinder_head", 20,
              {"chamber": 75, "heat_loss": 0, "intake_valve": 50, "intake_valves": 1, "exhaust_valve": 40, "exhaust_valves": 1, "inertia": 0.004},
              {"exhaust_header": 37, "cover": 12, "engine_block": sid},
              [slot(sid, "mount", fits=[k.fit("block", f"{side}_head")], at=(x, 0.15, 0)), slot(12, "cover", takes=[k.fit(f"{side}_head", "cover")]),
               slot(37, "exhaust", takes=[k.fit(f"{side}_head", "exhaust_header")], at=(x * 1.4, 0.1, 0))])
        k.add(f"{side}_header", f"{side} exhaust manifold", "exhaust_header", 8, {"flow": 0.45},
              slots=[slot(15, "mount", fits=[k.fit(f"{side}_head", "exhaust_header")])])
        k.add(f"{side}_tube_header", f"Racer {side} tube exhaust header (Test Mopar V8)", "exhaust_header", 6, {"flow": 0.6},
              slots=[slot(15, "mount", fits=[k.fit(f"{side}_head", "exhaust_header")])], aftermarket=True)
        k.add(f"{side}_valve_cover", f"{side} valve cover", "engine_part", 1, slots=[slot(12, "mount", fits=[k.fit(f"{side}_head", "cover")])])
    # The intakes: the stock 4-bbl, a speed shop's high-rise, one with a blower's pad on top
    intake_slots = lambda: [slot(14, "mount", fits=[k.fit("block", "intake_manifold")]), slot(4, "nitrous", takes=[k.fit("intake_manifold", "nitrous")])]
    k.add("intake_manifold", "4-bbl intake manifold", "intake_manifold", 12, {"flow": 0.9}, {"fuel_rail": 7},
          [*intake_slots(), slot(7, "carburettor", takes=["carb:4bbl"], at=(0, 0.3, 0))])
    k.add("high_rise_intake_manifold", "Racer high-rise 4-bbl intake manifold (Test Mopar V8)", "intake_manifold", 9, {"flow": 1.0}, {"fuel_rail": 7},
          [*intake_slots(), slot(7, "carburettor", takes=["carb:4bbl"], at=(0, 0.32, 0))], aftermarket=True)
    k.add("blower_intake_manifold", "blower intake manifold", "intake_manifold", 10, {"flow": 0.9},
          slots=[slot(14, "mount", fits=[k.fit("block", "intake_manifold")]), slot(7, "blower", takes=["blower:roots"], at=(0, 0.3, 0))])
    k.add("roots_blower", "Racer roots supercharger (Test Mopar V8)", "supercharger", 15,
          {"boost": 1.15, "drive_ratio": 2.5, "normal_drive_ratio": 2.5, "boost_rpm": 4000, "boost_rpm_range": 3000},
          slots=[slot(7, "mount", fits=["blower:roots"]), slot(9, "carburettor", takes=["carb:4bbl"]),
                 slot(15, "belt", takes=[k.fit("roots_blower", "belt")])], aftermarket=True)
    k.add("blower_belt", "Racer supercharger drive belt (Test Mopar V8)", "engine_part", 0.5,
          slots=[slot(14, "mount", fits=[k.fit("roots_blower", "belt")])], aftermarket=True)
    k.add("nitrous", "Racer nitrous oxide system (Test Mopar V8)", "nitrous", 4, slots=[slot(22, "mount", fits=[k.fit("intake_manifold", "nitrous")])],
          aftermarket=True)
    # The stock carburettor passes fuel enough for the engine as it comes and little more: what takes in more air (a blower,
    # a speed shop's top end) runs it short, so a bigger carburettor makes power. The big block comes with a bigger one.
    carb_and_cleaner(k, flow=(17, 600))
    carb_and_cleaner(k, name="4-bbl carburetor (750 cfm)", flow=(25, 750), cleaner_flow=320, prefix="big_")
    k.parts["big_air_cleaner"]["display_name"] = "Test Mopar V8 big-block air cleaner"
    carb_and_cleaner(k, name="Racer 4-bbl carburetor (750 cfm)", flow=(50, 750), cleaner_flow=400, clogging=0.1, prefix="racer_")
    k.parts["racer_air_cleaner"]["display_name"] = "Racer open air cleaner"
    k.add("distributor", "ignition distributor", "engine_part", 2, slots=[slot(59, "mount", fits=[k.fit("block", "distributor")])])
    # What the base game's rebuilds are for besides: the starter, the fuel pump, the ignition coil
    k.add("starter", "starter", "engine_part", 7, slots=[slot(61, "mount", fits=[k.fit("block", "starter")])])
    k.add("fuel_pump", "fuel pump", "engine_part", 1, slots=[slot(62, "mount", fits=[k.fit("block", "fuel_pump")])])
    k.add("ignition_coil", "ignition coil", "engine_part", 1, slots=[slot(63, "mount", fits=[k.fit("block", "coil")])])
    cooling(k, steering=True)
    # What a tuner swaps in for the heat and the oil: a racing radiator, a high-flow pump, a flex fan, a deep pan (a race sump)
    # and a drag pan big enough to be a big sump; and what only sounds like a cooling part
    k.add("racing_radiator", "Racer aluminium racing radiator (Test Mopar V8)", "engine_part", 7,
          slots=[slot(58, "mount", fits=[k.fit("block", "radiator")])], aftermarket=True)
    k.add("high_flow_water_pump", "Racer high-flow water pump (Test Mopar V8)", "engine_part", 3,
          slots=[slot(57, "mount", fits=[k.fit("block", "water_pump")]), slot(1, "fan", takes=[k.fit("water_pump", "fan")]),
                 slot(11, "belt", takes=[k.fit("water_pump", "belt")]), slot(69, "steering pump", takes=[k.fit("water_pump", "steering_pump")])],
          aftermarket=True)
    k.add("flex_fan", "Racer flex fan (Test Mopar V8)", "engine_part", 1, slots=[slot(1, "mount", fits=[k.fit("water_pump", "fan")])],
          aftermarket=True)
    k.add("deep_oil_pan", "Racer deep oil pan (Test Mopar V8)", "oil_pan", 5, {"friction": 0.0002, "capacity": 6},
          slots=[slot(10, "mount", fits=[k.fit("block", "oil_pan")])], aftermarket=True)
    k.add("drag_oil_pan", "Racer drag oil pan (Test Mopar V8, 8 qt)", "oil_pan", 6, {"friction": 0.0002, "capacity": 7.5},
          slots=[slot(10, "mount", fits=[k.fit("block", "oil_pan")])], aftermarket=True)
    k.add("mounting_hardware", "Racer dress-up kit mounting hardware (Test Mopar V8)", "engine_part", 0.5, aftermarket=True)
    k.add("intercooler", "Racer twin turbo intercooler (Test Mopar V8)", "engine_part", 6, aftermarket=True)

    # The order a build lists its parts is the order they go on, and so the order the work order names them in
    stock = ["block", "crankshaft", "crankshaft_timing_gear", "connecting_rods", "pistons", "camshaft", "camshaft_timing_gear", "timing_chain",
             "timing_cover", "harmonic_damper", "crankshaft_pulley", "left_head", "left_valve_cover", "left_header", "right_head", "right_valve_cover",
             "right_header", "oil_pan", "oil_filter", "flywheel", "clutch", "water_pump", "block_cover", "intake_manifold", "carburetor", "air_cleaner",
             "distributor", "alternator", "drive_belt", "fan", "radiator", "transmission", "starter", "fuel_pump", "ignition_coil",
             # The base game's battery, which no slot of an engine takes: a kit's part that stays loose on the shelf
             "engines/stock/12_volt_battery_black"]
    swap = lambda parts, **by: [by.get(p, p) for p in parts]
    # The speed parts' top end: a cam, a high-rise with a bigger carburettor and an open cleaner, tube headers
    top_end = dict(camshaft="speed_camshaft", intake_manifold="high_rise_intake_manifold", carburetor="racer_carburetor",
                   air_cleaner="racer_air_cleaner", left_header="left_tube_header", right_header="right_tube_header")
    k.build("factory/test-v8", "Test Mopar V8 factory", "factory", stock)
    k.build("rated/test-v8", "Test Mopar V8 rated", "rated", swap(stock, transmission="four_speed") + ["steering_pump", "steering_belt"],
            rated=RATED["rated/test-v8"])
    k.build("factory/test-v8-big", "Test Mopar V8 big block factory", "factory", swap(stock, block="big_block", carburetor="big_carburetor", air_cleaner="big_air_cleaner"))
    k.build("kit/test-v8-stroker", "Test Mopar V8 stroker kit", "kit",
            swap(stock, crankshaft="stroker_crankshaft", pistons="stroker_pistons", **top_end))
    k.build("kit/test-v8-racer", "Test Mopar V8 Racer street", "kit",
            swap(stock, radiator="radiator_electric_fan", fan=None, drive_belt=None, **top_end), aftermarket=True)
    # The blower on the stock carburettor, which its boost runs out of fuel: the bigger carburettor makes power on it
    k.build("kit/test-v8-blown", "Test Mopar V8 blown", "kit",
            swap(stock, intake_manifold="blower_intake_manifold", air_cleaner="racer_air_cleaner") + ["roots_blower", "blower_belt"])
    k.build("kit/test-v8-nitrous", "Test Mopar V8 nitrous", "kit", stock + ["nitrous"])
    return k


# ---- test_six -----------------------------------------------------------------------------------------------------------
def six():
    k = Pack("test_six", "ford", "Test Ford Six")
    k.add("block", "Test Ford Six block", "engine_block", 70,
          {"cylinders": 6, "bore": 90.0, "deck_height": 270.0, "idle_rpm": 700, "rev_limit": 5000, "spark_advance": 28.0,
           "spark_advance_high": 24.0, "spark_rpm_low": 1500, "spark_rpm_high": 3500},
          {"crankshaft": 8, "transmission": 5, "oil_pan": 9, "head": 45, "camshaft": 10},
          [slot(17, "mount"), slot(9, "oil pan", takes=[k.fit("block", "oil_pan")]), slot(5, "transmission", takes=[k.fit("block", "transmission")]),
           slot(45, "head", takes=[k.fit("block", "head")], at=(0, 0.2, 0)), slot(8, "crankshaft", takes=[k.fit("block", "crankshaft")]),
           slot(20, "alternator", takes=[k.fit("block", "alternator")], at=(0.15, 0.1, 0.3)), slot(10, "camshaft", takes=[k.fit("block", "camshaft")]),
           slot(55, "oil filter", takes=[k.fit("block", "oil_filter")]), slot(56, "timing cover", takes=[k.fit("block", "timing_cover")], at=(0, 0, 0.4)),
           slot(57, "water pump", takes=[k.fit("block", "water_pump")], at=(0, 0.1, 0.45)), slot(58, "radiator", takes=[k.fit("block", "radiator")], at=(0, 0.1, 0.75)),
           slot(59, "distributor", takes=[k.fit("block", "distributor")], at=(0.15, 0.05, 0.1)),
           slot(61, "starter", takes=[k.fit("block", "starter")], at=(0.15, -0.1, -0.3)),
           slot(64, "main bearings", takes=[k.fit("block", "bearing_bridge")], at=(0, -0.1, 0))],
          [(20, "alternator"), (56, "timing cover"), (55, "oil filter"), (58, "radiator"), (57, "water pump"), (59, "ignition distributor"),
           (45, "cylinder head"), (8, "crankshaft"), (9, "oil pan"), (5, "transmission")])
    k.parts["block"]["slot_roles"]["crank_bearing"] = 64
    bottom_end(k, 80, 150, 80, crank_max=7000)
    k.add("bearing_bridge", "main bearing bridge", "engine_part", 4, slots=[slot(64, "mount", fits=[k.fit("block", "bearing_bridge")])])   # 90 x 80: 3.05 l, 9.5:1 with the 60 cc chamber
    camshaft(k, duration=(220, 280))
    timing(k)
    driveline(k, gears=(2.8, 1.6, 1.0), final=3.0)
    k.add("head", "cylinder head", "cylinder_head", 25,
          {"chamber": 60, "heat_loss": 0, "intake_valve": 45, "intake_valves": 1, "exhaust_valve": 38, "exhaust_valves": 1, "inertia": 0.005},
          {"exhaust_header": 37, "cover": 12, "engine_block": 45, "intake_manifold": 14},
          [slot(45, "mount", fits=[k.fit("block", "head")], at=(0, 0.2, 0)), slot(12, "cover", takes=[k.fit("head", "cover")]),
           slot(37, "exhaust", takes=[k.fit("head", "exhaust_header")], at=(-0.15, 0.15, 0)),
           slot(14, "intake", takes=[k.fit("head", "intake_manifold")], at=(0.15, 0.2, 0))])
    k.add("header", "exhaust manifold", "exhaust_header", 10, {"flow": 0.4}, slots=[slot(15, "mount", fits=[k.fit("head", "exhaust_header")])])
    k.add("valve_cover", "valve cover", "engine_part", 1, slots=[slot(12, "mount", fits=[k.fit("head", "cover")])])
    k.add("intake_manifold", "1-bbl intake manifold", "intake_manifold", 6, {"flow": 0.8}, {"fuel_rail": 7},
          [slot(14, "mount", fits=[k.fit("head", "intake_manifold")]), slot(7, "carburettor", takes=["carb:1bbl"])], comes_off=["out"])
    carb_and_cleaner(k, pad="carb:1bbl", air="air:1bbl", name="Test Ford Six 1-bbl carburetor", flow=(20, 300), cleaner_flow=150)
    k.add("distributor", "side distributor", "engine_part", 2, slots=[slot(59, "mount", fits=[k.fit("block", "distributor")])], kind="side_distributor")
    k.add("starter", "starter", "engine_part", 7, slots=[slot(61, "mount", fits=[k.fit("block", "starter")])])
    cooling(k)
    k.parts["alternator"]["comes_off"] = ["up"]
    k.build("factory/test-six", "Test Ford Six factory", "factory",
            ["block", "crankshaft", "crankshaft_timing_gear", "connecting_rods", "pistons", "camshaft", "camshaft_timing_gear", "timing_chain",
             "timing_cover", "oil_pan", "oil_filter", "flywheel", "clutch", "crankshaft_pulley", "water_pump", "fan", "drive_belt", "head",
             "header", "valve_cover", "intake_manifold", "carburetor", "air_cleaner", "distributor", "alternator", "radiator", "transmission",
             "starter", "bearing_bridge"])
    return k


# ---- test_flathead ------------------------------------------------------------------------------------------------------
def flathead():
    k = Pack("test_flathead", "ford", "Test Ford Flathead")
    k.add("block", "Test Ford Flathead V8 block", "engine_block", 90,
          {"cylinders": 8, "bore": 80.0, "deck_height": 275.0, "idle_rpm": 600, "rev_limit": 4500, "spark_advance": 26.0,
           "spark_advance_high": 22.0, "spark_rpm_low": 1200, "spark_rpm_high": 3000},
          {"crankshaft": 8, "transmission": 5, "oil_pan": 9, "intake_manifold": 14, "left_head": 45, "right_head": 46, "camshaft": 10},
          [slot(17, "mount"), slot(9, "oil pan", takes=[k.fit("block", "oil_pan")]), slot(5, "transmission", takes=[k.fit("block", "transmission")]),
           slot(45, "left head", takes=[k.fit("block", "left_head")], at=(0.18, 0.12, 0)),
           slot(46, "right head", takes=[k.fit("block", "right_head")], at=(-0.18, 0.12, 0)),
           slot(8, "crankshaft", takes=[k.fit("block", "crankshaft")]), slot(14, "intake", takes=[k.fit("block", "intake_manifold")], at=(0, 0.2, 0)),
           slot(10, "camshaft", takes=[k.fit("block", "camshaft")]), slot(55, "oil filter", takes=[k.fit("block", "oil_filter")]),
           slot(56, "timing cover", takes=[k.fit("block", "timing_cover")], at=(0, 0, 0.35)),
           slot(57, "water pump", takes=[k.fit("block", "water_pump")], at=(0, 0.1, 0.4)), slot(58, "radiator", takes=[k.fit("block", "radiator")], at=(0, 0.1, 0.7)),
           slot(59, "distributor", takes=[k.fit("block", "distributor")], at=(0, 0.1, 0.38)),
           slot(20, "generator", takes=[k.fit("block", "alternator")], at=(0.15, 0.15, 0.3))],
          [(56, "timing cover"), (55, "oil filter"), (58, "radiator"), (57, "water pump"), (59, "ignition distributor"),
           (45, "left cylinder head"), (46, "right cylinder head"), (14, "intake"), (8, "crankshaft"), (9, "oil pan"), (5, "transmission")])
    bottom_end(k, 90, 150, 80, crank_max=6000)   # 80 x 90: 3.62 l, 8.5:1 with the 60 cc heads
    camshaft(k, lift=(12.0, 14.0), duration=(220, 270))
    timing(k)
    driveline(k, gears=(2.8, 1.6, 1.0), final=3.5)
    for side, sid, x in (("left", 45, 0.18), ("right", 46, -0.18)):
        k.add(f"{side}_head", f"{side} cylinder head", "cylinder_head", 12,
              {"chamber": 60, "heat_loss": 75, "intake_valve": 48, "intake_valves": 1, "exhaust_valve": 42, "exhaust_valves": 1, "inertia": 0.006},
              {"exhaust_header": 37, "engine_block": sid},
              [slot(sid, "mount", fits=[k.fit("block", f"{side}_head")], at=(x, 0.12, 0)), slot(37, "exhaust", takes=[k.fit(f"{side}_head", "exhaust_header")])])
        k.add(f"{side}_header", f"{side} exhaust manifold", "exhaust_header", 8, {"flow": 0.4},
              slots=[slot(15, "mount", fits=[k.fit(f"{side}_head", "exhaust_header")])])
        # The speed shop's: aluminium heads of a smaller chamber, tube headers
        k.add(f"{side}_aluminium_head", f"Racer aluminium {side} cylinder head (Test Ford Flathead)", "cylinder_head", 8,
              {"chamber": 52, "heat_loss": 75, "intake_valve": 48, "intake_valves": 1, "exhaust_valve": 42, "exhaust_valves": 1, "inertia": 0.005},
              {"exhaust_header": 37, "engine_block": sid},
              [slot(sid, "mount", fits=[k.fit("block", f"{side}_head")], at=(x, 0.12, 0)), slot(37, "exhaust", takes=[k.fit(f"{side}_head", "exhaust_header")])],
              aftermarket=True)
        k.add(f"{side}_tube_header", f"Racer {side} tube exhaust header (Test Ford Flathead)", "exhaust_header", 5, {"flow": 0.55},
              slots=[slot(15, "mount", fits=[k.fit(f"{side}_head", "exhaust_header")])], aftermarket=True)
    # Row intakes: every pad of a row enforced, the carburettors breathing through their own horns or the row's cleaner
    pads = (7, 300, 301, 302)
    words = ("first", "second", "third", "fourth")
    for name, display, n, flow, aftermarket in (("intake_manifold", "1-bbl intake manifold", 1, 0.5, False),
                                                ("dual_intake_manifold", "Racer 2x2-bbl intake manifold (Test Ford Flathead)", 2, 0.55, True),
                                                ("quad_intake_manifold", "Racer 4x2-bbl racing intake manifold (Test Ford Flathead)", 4, 0.6, True)):
        extra = {"aftermarket": True} if aftermarket else {}
        k.add(name, display, "intake_manifold", 6 + n, {"flow": flow}, {"fuel_rail": 7},
              [slot(3, "mount", fits=[k.fit("block", "intake_manifold")]), *[slot(p, "carburettor", takes=["carb:97"]) for p in pads[:n]]],
              [(p, f"{words[i]} carburettor") for i, p in enumerate(pads[:n])] if n > 1 else [(7, "carburettor")], **extra)
    k.add("carburetor", "Test 97 carburetor (2-bbl)", "fuel_system", 2.5, {"mixture_ratio": 12.5, "fuel_flow": 20, "air_flow": 400},
          slots=[slot(10, "mount", fits=["carb:97", "carb:2bbl"]), slot(11, "air cleaner", takes=["air:97"])])
    k.add("air_cleaner", "oil bath air cleaner", "air_cleaner", 2, {"air_flow": 200, "clogging": 0.3}, slots=[slot(12, "mount", fits=["air:97"])])
    k.add("velocity_stack", "Racer velocity stack (97)", "air_cleaner", 0.4, {"air_flow": 400, "clogging": 0},
          slots=[slot(12, "mount", fits=["air:97"])], aftermarket=True)
    k.add("distributor", "ignition distributor", "engine_part", 2, slots=[slot(59, "mount", fits=[k.fit("block", "distributor")])])
    k.add("generator", "Test Ford Flathead alternator", "engine_part", 6, slots=[slot(20, "mount", fits=[k.fit("block", "alternator")])])
    cooling(k, alternator=False)
    base = ["block", "crankshaft", "crankshaft_timing_gear", "connecting_rods", "pistons", "camshaft", "camshaft_timing_gear", "timing_chain",
            "timing_cover", "oil_pan", "oil_filter", "flywheel", "clutch", "crankshaft_pulley", "water_pump", "fan", "drive_belt", "left_head",
            "left_header", "right_head", "right_header", "distributor", "generator", "radiator", "transmission"]
    k.build("factory/test-flathead", "Test Ford Flathead factory", "factory", base + ["intake_manifold", "carburetor", "air_cleaner"])
    k.build("kit/test-flathead-dual", "Test Ford Flathead dual carbs", "kit", base + ["dual_intake_manifold"] + ["carburetor", "air_cleaner"] * 2,
            aftermarket=True)
    k.build("kit/test-flathead-quad", "Test Ford Flathead four carbs", "kit", base + ["quad_intake_manifold"] + ["carburetor", "velocity_stack"] * 4,
            aftermarket=True)
    return k


# ---- test_four ----------------------------------------------------------------------------------------------------------
def four():
    k = Pack("test_four", None, "Test Peugeot Four")
    k.add("block", "Test Peugeot Four block", "engine_block", 40,
          {"cylinders": 4, "bore": 80.0, "deck_height": 270.0, "idle_rpm": 850, "rev_limit": 6500, "spark_advance": 30.0,
           "spark_advance_high": 26.0, "spark_rpm_low": 1500, "spark_rpm_high": 4000},
          {"crankshaft": 8, "transmission": 5, "oil_pan": 9, "head": 45, "camshaft": 10},
          [slot(17, "mount"), slot(9, "oil pan", takes=[k.fit("block", "oil_pan")]), slot(5, "transmission", takes=[k.fit("block", "transmission")]),
           slot(45, "head", takes=[k.fit("block", "head")], at=(0, 0.2, 0)), slot(8, "crankshaft", takes=[k.fit("block", "crankshaft")]),
           slot(20, "alternator", takes=[k.fit("block", "alternator")], at=(0.12, 0.1, 0.2)), slot(10, "camshaft", takes=[k.fit("block", "camshaft")]),
           slot(55, "oil filter", takes=[k.fit("block", "oil_filter")]), slot(56, "timing cover", takes=[k.fit("block", "timing_cover")], at=(0, 0, 0.25)),
           slot(57, "water pump", takes=[k.fit("block", "water_pump")], at=(0, 0.1, 0.3)), slot(58, "radiator", takes=[k.fit("block", "radiator")], at=(0, 0.1, 0.55)),
           slot(59, "distributor", takes=[k.fit("block", "distributor")], at=(0.1, 0.1, -0.1))],
          [(20, "alternator"), (56, "timing cover"), (55, "oil filter"), (58, "radiator"), (57, "water pump"), (59, "ignition distributor"),
           (45, "cylinder head"), (8, "crankshaft"), (9, "oil pan"), (5, "transmission")])
    bottom_end(k, 80, 150, 80, crank_max=8000)   # 80 x 80: 1.61 l, 9.9:1 with the 45 cc chamber
    camshaft(k, duration=(240, 290))
    timing(k)
    driveline(k, gears=(3.5, 2.0, 1.4, 1.0), drive="front", final=3.9)
    k.add("head", "cylinder head", "cylinder_head", 12,
          {"chamber": 45, "heat_loss": 0, "intake_valve": 40, "intake_valves": 1, "exhaust_valve": 34, "exhaust_valves": 1, "inertia": 0.003},
          {"exhaust_header": 37, "cover": 12, "engine_block": 45, "intake_manifold": 14},
          [slot(45, "mount", fits=[k.fit("block", "head")], at=(0, 0.2, 0)), slot(12, "cover", takes=[k.fit("head", "cover")]),
           slot(37, "exhaust", takes=[k.fit("head", "exhaust_header")], at=(-0.12, 0.15, 0)),
           slot(14, "intake", takes=[k.fit("head", "intake_manifold")], at=(0.12, 0.2, 0))])
    k.add("header", "exhaust manifold", "exhaust_header", 6, {"flow": 0.45}, slots=[slot(15, "mount", fits=[k.fit("head", "exhaust_header")])])
    k.add("valve_cover", "valve cover", "engine_part", 1, slots=[slot(12, "mount", fits=[k.fit("head", "cover")])])
    k.add("intake_manifold", "2-bbl intake manifold", "intake_manifold", 4, {"flow": 0.85}, {"fuel_rail": 7},
          [slot(14, "mount", fits=[k.fit("head", "intake_manifold")]), slot(7, "carburettor", takes=["carb:2bbl"])])
    carb_and_cleaner(k, pad="carb:2bbl", air="air:2bbl", name="Test Peugeot Four 2-bbl carburetor", flow=(15, 250), cleaner_flow=120)
    k.add("distributor", "ignition distributor", "engine_part", 1.5, slots=[slot(59, "mount", fits=[k.fit("block", "distributor")])])
    cooling(k)
    k.build("factory/test-four", "Test Peugeot Four factory", "factory",
            ["block", "crankshaft", "crankshaft_timing_gear", "connecting_rods", "pistons", "camshaft", "camshaft_timing_gear", "timing_chain",
             "timing_cover", "oil_pan", "oil_filter", "flywheel", "clutch", "crankshaft_pulley", "water_pump", "fan", "drive_belt", "head",
             "header", "valve_cover", "intake_manifold", "carburetor", "air_cleaner", "distributor", "alternator", "radiator", "transmission"])
    return k


# What the rated builds make on the game's dyno (EngineBench show), held to it by EngineBench rated
RATED = {"rated/test-v8": 275}

if __name__ == "__main__":
    packs = [v8(), six(), flathead(), four()]
    if os.path.isdir(OUT):
        shutil.rmtree(os.path.join(OUT, "engines"), ignore_errors=True)
    for k in packs:
        folder = os.path.join(OUT, "engines", k.ns)
        os.makedirs(folder, exist_ok=True)
        for name, p in k.parts.items():
            with open(os.path.join(folder, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
                json.dump(p, f, indent=2)
                f.write("\n")
    with open(os.path.join(OUT, "engine_builds.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(BUILDS, f, indent=2)
        f.write("\n")
    print(sum(len(k.parts) for k in packs), "parts,", len(BUILDS), "builds")
