"""
Runs the race mode's rims (apps/new-modes/sc_race/car_parts.lua) on a stub car, without Assetto Corsa.

The stub car is the Valiant's wheels as its KN5 has them (atcc_chrysler_valiant_s-series, measured 2026-10-03), the
part SLRR's E-T mag (rims/mopar/rim_8_5), every node at the car's origin unturned. It checks which meshes go, how
big the part is made and where its face lands. Not how CSP draws it, which only the game can show.

    python tools/sc_race_harness/test_parts.py
"""

import os
import sys

try:
    import lupa.luajit21 as lupa
except ImportError:
    sys.exit('lupa has no LuaJIT 2.1 runtime here (pip install -r tools/sc_race_harness/requirements.txt)')

HERE = os.path.dirname(os.path.abspath(__file__))
PARTS = os.path.join(HERE, '..', '..', 'apps', 'new-modes', 'sc_race', 'car_parts.lua')

STUB = r'''
local world = ...

local V = {}
V.__index = V
function vec3(x, y, z) return setmetatable({ x = x or 0, y = y or 0, z = z or 0 }, V) end
function vec4(x, y, z, w) local v = vec3(x, y, z); v.w = w or 0; return v end
function V:set(x, y, z) self.x, self.y, self.z = x, y, z; return self end

-- Row-major, a point a row vector (p * M), as CSP's mat4x4
local M = {}
M.__index = M
function mat4x4(r1, r2, r3, r4)
  local function row(r, d) return r and { r.x, r.y, r.z, r.w } or d end
  return setmetatable({ row(r1, { 1, 0, 0, 0 }), row(r2, { 0, 1, 0, 0 }), row(r3, { 0, 0, 1, 0 }), row(r4, { 0, 0, 0, 1 }) }, M)
end
function M:set(o) for i = 1, 4 do for j = 1, 4 do self[i][j] = o[i][j] end end; return self end
function M:mul(o)
  local r = mat4x4()
  for i = 1, 4 do for j = 1, 4 do
    local s = 0
    for k = 1, 4 do s = s + self[i][k] * o[k][j] end
    r[i][j] = s
  end end
  return r
end
-- Only scales, half turns and moves here: the inverse of the 3x3 is its transpose over the scales squared
function M:inverse()
  local r = mat4x4()
  for i = 1, 3 do for j = 1, 3 do
    local s2 = self[j][1] ^ 2 + self[j][2] ^ 2 + self[j][3] ^ 2
    r[i][j] = self[j][i] / s2
  end end
  for j = 1, 3 do r[4][j] = -(self[4][1] * r[1][j] + self[4][2] * r[2][j] + self[4][3] * r[3][j]) end
  return r
end
function M:transformPoint(p)
  local v = { p.x, p.y, p.z, 1 }
  local o = {}
  for j = 1, 3 do o[j] = v[1] * self[1][j] + v[2] * self[2][j] + v[3] * self[3][j] + v[4] * self[4][j] end
  return vec3(o[1], o[2], o[3])
end

-- Scene: nodes and meshes; a reference is a list of them
local R = {}
R.__index = R
local function ref(items) return setmetatable({ items = items }, R) end
local function node(name, parent)
  local n = { name = name, parent = parent, children = {}, transform = mat4x4(), visible = true }
  if parent then table.insert(parent.children, n) end
  return n
end
local function mesh(name, parent, min, max, material)
  local m = node(name, parent)
  m.material = material
  m.mesh = true
  m.min, m.max = vec3(min[1], min[2], min[3]), vec3(max[1], max[2], max[3])
  return m
end
local function worldOf(n)
  local w = n.transform
  local p = n.parent
  while p do w = w:mul(p.transform); p = p.parent end
  return w
end
local function walk(n, out, test)
  for _, c in ipairs(n.children) do
    if test(c) then out[#out + 1] = c end
    walk(c, out, test)
  end
  return out
end
function R:size() return #self.items end
function R:at(i) return ref({ self.items[i] }) end
function R:name(i) return self.items[i or 1].name end
function R:materialName(i) return self.items[i or 1].material or self.items[i or 1].name end
function R:findNodes(name) return ref(walk(self.items[1], {}, function(c) return not c.mesh and c.name == name end)) end
function R:findMeshes() return ref(walk(self.items[1], {}, function(c) return c.mesh end)) end
function R:getParent() return ref({ self.items[1].parent }) end
function R:getWorldTransformationRaw() return worldOf(self.items[1]) end
function R:getTransformationRaw() return self.items[1].transform end
function R:getLocalAABB() return self.items[1].min, self.items[1].max end
function R:setVisible(v) for _, n in ipairs(self.items) do n.visible = v end; return self end
function R:dispose() end
function R:loadKN5(file)
  local root = node(file, self.items[1])
  -- rims/mopar/rim_8_5 as the converter wrote it
  mesh('rim_8_5_0', root, { -0.029, -0.028, -0.028 }, { -0.029, 0.029, 0.028 })
  mesh('rim_8_5_1', root, { -0.031, -0.052, -0.059 }, { 0.01, 0.062, 0.059 })
  mesh('rim_8_5_2', root, { -0.022, -0.147, -0.147 }, { 0.058, 0.147, 0.147 })
  mesh('rim_8_5_3', root, { -0.047, -0.187, -0.187 }, { 0.103, 0.187, 0.187 })
  world.loaded[#world.loaded + 1] = { file = file, node = root }
  return ref({ root })
end

-- The Valiant: the left front wheel as measured, the right front its mirror
local car = node('carRoot')
local function wheel(name, side, suffix)
  local w = node(name, car)
  local function m(n, x0, x1, r)
    local a, b = side * x0, side * x1
    mesh(n .. suffix, node(n .. suffix, w), { math.min(a, b), -r, -r }, { math.max(a, b), r, r })
  end
  m('whitewall_', 0.036, 0.044, 0.227)
  m('inner_rim_', -0.121, 0.024, 0.187)
  m('rim_centre_', -0.001, 0.029, 0.151)
  m('rim_outer_', -0.005, 0.037, 0.187)
  m('tire_tread_', -0.108, 0.026, 0.302)
  m('tire_wall_', -0.125, 0.042, 0.284)
  m('tyre_cap_', 0.028, 0.045, 0.163)
  m('wheel_hub_', 0.012, 0.029, 0.029)
  m('Wheel_Nuts_', 0.021, 0.035, 0.062)
  m('brake_drum_', -0.119, -0.049, 0.139)
  m('hubcap_S_', 0.021, 0.08, 0.188)
  return w
end
world.wheels = { LF = wheel('WHEEL_LF', 1, 'LF'), RF = wheel('WHEEL_RF', -1, 'RF') }

-- The Chevelle '70's left rear as its KN5 has the left front: meshes named after the node, materials saying what they are
local chevelle = node('WHEEL_LR', car)
local function sub(n, material, x0, x1, r)
  mesh('WHEEL_LR_SUB' .. n, node('WHEEL_LR_SUB' .. n, chevelle), { x0, -r, -r }, { x1, r, r }, material)
end
sub(5, 'brakedisc', -0.021, 0.021, 0.142)
sub(4, 'EXT_tire', -0.067, 0.164, 0.316)
sub(3, 'chevelle_rim_chrome', 0.041, 0.058, 0.084)
sub(2, 'chevelle_rim_black', 0.041, 0.153, 0.167)
sub(1, 'chevelle_rim_inner', -0.072, 0.162, 0.231)
sub(0, 'chevelle_rim', 0.067, 0.164, 0.217)
world.wheels.LR = chevelle

ac = {
  log = function(s) world.log[#world.log + 1] = s end,
  getSim = function() return { carsCount = 1 } end,
  findNodes = function(f) return f == 'carRoot:0' and ref({ car }) or ref({}) end,
  getFolder = function() return 'docs' end,
  FolderID = { ACDocuments = 1 },
  INIConfig = { raceConfig = function() return { get = function(_, section, key, default)
    return world.ini[key] or default
  end } end }
}
io = { save = function() return true end }
'''


def run(ini):
    lua = lupa.LuaRuntime()
    world = lua.table_from({'ini': lua.table_from(ini), 'log': lua.table(), 'loaded': lua.table()})
    lua.execute(STUB, world)
    with open(PARTS, encoding='utf-8') as f:
        parts = lua.execute(f.read())
    parts.update()
    parts.update()
    return lua, world


def visible(world, wheel):
    out = {}
    def walk(n):
        for c in n.children.values():
            if c.mesh:
                out[c.name] = c.visible
            walk(c)
    walk(world.wheels[wheel])
    return out


def rim(world, wheel):
    for item in world.loaded.values():
        if item.node.parent.name == 'WHEEL_' + wheel:
            t = item.node.transform
            return [[t[i][j] for j in range(1, 5)] for i in range(1, 5)]
    return None


failures = 0


def check(name, condition, log=None):
    global failures
    print(('ok  ' if condition else 'FAIL') + '  ' + name)
    if not condition:
        failures += 1
        for line in (log or []):
            print('      ' + line)


def near(a, b, tolerance=0.0005):
    return abs(a - b) <= tolerance


def a_corner_not_named_keeps_its_own_rim():
    _, world = run({'CAR_0_RIM_LF': r'parts\rims_mopar_rim_8_5.kn5'})
    check('a corner not named keeps its own rim', rim(world, 'RF') is None and all(visible(world, 'RF').values()))


def the_rim_goes_and_the_tyre_and_brake_stay():
    _, world = run({'CAR_0_RIM_LF': r'parts\rims_mopar_rim_8_5.kn5'})
    shown = visible(world, 'LF')
    stay = ['whitewall_LF', 'tire_tread_LF', 'tire_wall_LF', 'tyre_cap_LF', 'brake_drum_LF']
    go = ['inner_rim_LF', 'rim_centre_LF', 'rim_outer_LF', 'wheel_hub_LF', 'Wheel_Nuts_LF', 'hubcap_S_LF']
    check('the rim goes, the tyre and the brake stay',
          all(shown[n] for n in stay) and not any(shown[n] for n in go), list(world.log.values()))


def the_part_fills_the_lip_flush_with_the_sidewall_facing_out():
    _, world = run({'CAR_0_RIM_LF': 'a.kn5', 'CAR_0_RIM_RF': 'a.kn5'})
    log = list(world.log.values())
    left, right = rim(world, 'LF'), rim(world, 'RF')
    scale = 0.188 / 0.187
    # The left wheel's outboard side is +X: the part (face towards -X) turned half about the vertical, its lip
    # (x -0.047 in the part) on the tyre's sidewall at +0.045, not on the hubcap's bulge at +0.08
    check('the left part is turned to face out and scaled to the lip',
          left and near(left[0][0], -scale) and near(left[1][1], scale) and near(left[2][2], -scale), log)
    check('the left part\'s face is on the sidewall', left and near(left[3][0] + 0.047 * scale, 0.045), log)
    check('the right part faces -X as it is, its face on the sidewall',
          right and near(right[0][0], scale) and near(right[3][0] - 0.047 * scale, -0.045), log)


def a_mesh_named_after_its_node_goes_by_its_material():
    _, world = run({'CAR_0_RIM_LR': 'a.kn5'})
    shown = visible(world, 'LR')
    check('a mesh named after its node goes by its material (the Chevelle tyre and disc stay)',
          shown['WHEEL_LR_SUB4'] and shown['WHEEL_LR_SUB5'] and not any(shown['WHEEL_LR_SUB%d' % n] for n in range(4)),
          list(world.log.values()))


a_corner_not_named_keeps_its_own_rim()
a_mesh_named_after_its_node_goes_by_its_material()
the_rim_goes_and_the_tyre_and_brake_stay()
the_part_fills_the_lip_flush_with_the_sidewall_facing_out()
sys.exit(1 if failures else 0)
