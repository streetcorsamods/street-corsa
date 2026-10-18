"""
Runs the race mode's rims (apps/new-modes/sc_race/car_parts.lua) on a stub car, without Assetto Corsa.

The stub car is the Valiant's wheels as its KN5 has them (atcc_chrysler_valiant_s-series, measured 2026-10-03), the
part rim, the E-T mag (rims/mopar/e_t_mag_wheel_15x5), every node at the car's origin unturned. It checks which meshes go, how
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
function V:length() return math.sqrt(self.x ^ 2 + self.y ^ 2 + self.z ^ 2) end
function V:normalize()
  local l = math.sqrt(self.x ^ 2 + self.y ^ 2 + self.z ^ 2)
  if l > 0 then self.x, self.y, self.z = self.x / l, self.y / l, self.z / l end
  return self
end

-- Row-major, a point a row vector (p * M), as CSP's mat4x4; a row is m[i][j] here and m.rowN.x/y/z/w as CSP has it
local M = {}
local ROW = { x = 1, y = 2, z = 3, w = 4 }
local Row = {
  __index = function(t, k) return rawget(t, ROW[k] or k) end,
  __newindex = function(t, k, v) rawset(t, ROW[k] or k, v) end
}
M.__index = function(t, k)
  local n = type(k) == 'string' and k:match('^row(%d)$')
  if n then return rawget(t, tonumber(n)) end
  return M[k]
end
local function rows(r1, r2, r3, r4, d)
  local function row(r, i) return setmetatable(r and { r.x, r.y, r.z, r.w } or d(i), Row) end
  return setmetatable({ row(r1, 1), row(r2, 2), row(r3, 3), row(r4, 4) }, M)
end
-- A row not given is zeros here: code that wants the identity asks for mat4x4.identity()
mat4x4 = setmetatable({
  identity = function()
    return rows(nil, nil, nil, nil, function(i) local r = { 0, 0, 0, 0 }; r[i] = 1; return r end)
  end
}, { __call = function(_, r1, r2, r3, r4) return rows(r1, r2, r3, r4, function() return { 0, 0, 0, 0 } end) end })
function M:clone() local r = mat4x4.identity(); r:set(self); return r end
function M:transformVector(p)
  local o = {}
  for j = 1, 3 do o[j] = p.x * self[1][j] + p.y * self[2][j] + p.z * self[3][j] end
  return vec3(o[1], o[2], o[3])
end
function M:set(o) for i = 1, 4 do for j = 1, 4 do self[i][j] = o[i][j] end end; return self end
function M:mul(o)
  local r = mat4x4.identity()
  for i = 1, 4 do for j = 1, 4 do
    local s = 0
    for k = 1, 4 do s = s + self[i][k] * o[k][j] end
    r[i][j] = s
  end end
  return r
end
-- Only scales, half turns and moves here: the inverse of the 3x3 is its transpose over the scales squared
function M:inverse()
  local r = mat4x4.identity()
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
  local n = { name = name, parent = parent, children = {}, transform = mat4x4.identity(), visible = true }
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
function R:findAny(name) return ref(walk(self.items[1], {}, function(c) return c.name == name end)) end
function R:createNode(name) return ref({ node(name, self.items[1]) }) end
function R:at(i) return ref({ self.items[i] }) end
function R:name(i) return self.items[i or 1].name end
function R:materialName(i) return self.items[i or 1].material or self.items[i or 1].name end
function R:findNodes(name) return ref(walk(self.items[1], {}, function(c) return not c.mesh and c.name == name end)) end
function R:findMeshes() return ref(walk(self.items[1], {}, function(c) return c.mesh end)) end
function R:getParent() return ref({ self.items[1].parent }) end
function R:contains(other) for _, n in ipairs(self.items) do if n == other.items[1] then return true end end return false end
function R:getWorldTransformationRaw() return worldOf(self.items[1]) end
function R:getTransformationRaw() return self.items[1].transform end
function R:getLocalAABB() return self.items[1].min, self.items[1].max end
function R:setVisible(v) for _, n in ipairs(self.items) do n.visible = v end; return self end
function R:dispose() end
function R:getChildrenCount() return #self.items[1].children end
function R:getChild(i) return ref({ self.items[1].children[i or 1] }) end
function R:setParent(p)
  local n = self.items[1]
  for k, c in ipairs(n.parent.children) do if c == n then table.remove(n.parent.children, k) break end end
  n.parent = p.items[1]
  table.insert(n.parent.children, n)
  return self
end
-- The car's surface: world.hood is its height over every point (nil: nothing there), one-sided as most hoods are, its
-- face up (world.hoodDown: its face down, as a hood drawn from inside). Culling as CSP's (lib_scene.lua): 1, the
-- default, hits front faces only, 0 back faces only, -1 both; a ray coming down meets the face of a face-up hood
function R:raycast(ray, _, _, _, _, culling)
  if not world.hood then return -1 end
  culling = culling or 1
  local front = not world.hoodDown
  if (front and culling == 0) or (not front and culling == 1) then return -1 end
  return ray.pos.y - world.hood
end
function R:loadKN5(file)
  local root = node(file, self.items[1])
  world.loaded[#world.loaded + 1] = { file = file, node = root }
  if file:find('looks') then
    -- A wear layer: the body's wear, and a door's in a group standing where the door stands at rest (WearLayer.MoverPrefix)
    mesh('sc_wear_0', root, { -0.9, 0.3, -2.2 }, { 0.9, 1.2, 2.2 })
    local door = node('sc_move_DOOR_L', root)
    door.transform[4][1], door.transform[4][2], door.transform[4][3] = 0.95, 0.75, 0.6
    mesh('sc_wear_1', door, { -0.01, -0.35, -1 }, { 0.01, 0.35, 0 })
    node('sc_move_TRUNK', root)
    return ref({ root })
  end
  if file:find('tyre') then
    -- tyres/mopar/Tyre_15_8 as the converter wrote it
    mesh('Tyre_15_8_0', root, { -0.101, -0.312, -0.313 }, { 0.097, 0.312, 0.312 })
    return ref({ root })
  end
  if file:find('wide') then
    -- rims/mopar/cragar_11, a 15x11
    mesh('cragar_11_0', root, { -0.095, -0.165, -0.165 }, { 0.13, 0.165, 0.165 })
    mesh('cragar_11_1', root, { -0.129, -0.187, -0.187 }, { 0.13, 0.187, 0.187 })
    return ref({ root })
  end
  -- rims/mopar/rim_8_5 as the converter wrote it
  mesh('rim_8_5_0', root, { -0.029, -0.028, -0.028 }, { -0.029, 0.029, 0.028 })
  mesh('rim_8_5_1', root, { -0.031, -0.052, -0.059 }, { 0.01, 0.062, 0.059 })
  mesh('rim_8_5_2', root, { -0.022, -0.147, -0.147 }, { 0.058, 0.147, 0.147 })
  mesh('rim_8_5_3', root, { -0.047, -0.187, -0.187 }, { 0.103, 0.187, 0.187 })
  return ref({ root })
end

-- The Valiant: the left front wheel as measured, the right front its mirror
local carRoot = node('carRoot')
local car = node('CARNODE', carRoot)
local body = node('BODYTR', car)
world.floor = node('FLOOR', body)
-- The left door, on its hinge in the model (under FLOOR, the model's frame node)
world.door = node('DOOR_L', world.floor)
world.door.transform[4][1], world.door.transform[4][2], world.door.transform[4][3] = 0.95, 0.75, 0.6
local function wheel(name, side, suffix)
  local w = node(name, node('WHEEL_TR', car))
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
local chevelle = node('WHEEL_LR', node('WHEEL_TR', car))
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

-- A mod that names its wheel in German: no tyre by name, the tyre is the mesh well outside the rest (right side: -X out)
local reifen = node('WHEEL_RR', node('WHEEL_TR', car))
local function rr(n, x0, x1, r) mesh(n, node(n, reifen), { -x1, -r, -r }, { -x0, r, r }) end
rr('Reifen_RR', -0.1, 0.05, 0.31)
rr('Felge_RR', -0.1, 0.04, 0.19)
rr('Stern_RR', 0.0, 0.03, 0.15)
rr('Bremse_disc_RR', -0.09, -0.06, 0.14)
world.wheels.RR = reifen

-- A blurred rim outside the wheel, as some cars keep it
mesh('RIM_BLUR_LF', node('RIM_BLUR_LF', car), { -0.1, -0.2, -0.2 }, { 0.1, 0.2, 0.2 })
world.blur = car.children[#car.children].children[1]

ac = {
  log = function(s) world.log[#world.log + 1] = s end,
  getSim = function() return { carsCount = 1 } end,
  getCar = function() return { wheels = { [0] = { tyreRadius = 0.30 }, [1] = { tyreRadius = 0.30 }, [2] = { tyreRadius = 0.30 }, [3] = { tyreRadius = 0.30 } } } end,
  -- world.late: the car's model is not there yet
  findNodes = function(f) return f == 'carRoot:0' and not world.late and ref({ carRoot }) or ref({}) end,
  getFolder = function() return 'docs' end,
  FolderID = { ACDocuments = 1 },
  INIConfig = {
    raceConfig = function() world.iniLoads = (world.iniLoads or 0) + 1; return { get = function(_, section, key, default)
      return world.ini[key] or default
    end } end,
    -- blurred_objects.ini: world.blurred = { 'WHEEL_LF', 'RIM_BLUR_LF' }, all of wheel 0
    carData = function() return {
      iterate = function()
        local i = 0
        return function()
          i = i + 1
          if world.blurred and world.blurred[i] then return i, 'OBJECT_' .. (i - 1) end
        end
      end,
      get = function(_, section, key, default)
        local i = tonumber(section:match('%d+')) + 1
        if key == 'WHEEL_INDEX' then return 0 end
        if key == 'NAME' then return world.blurred[i] end
        return default
      end
    } end
  }
}
io = { save = function() return true end }
render = { createRay = function(pos, dir, length) return { pos = pos, dir = dir, length = length } end }
'''


def run(ini, blurred=None, hood=None, floor_y=None, late=False, hood_down=False):
    lua = lupa.LuaRuntime()
    world = lua.table_from({'ini': lua.table_from(ini), 'log': lua.table(), 'loaded': lua.table(),
                            'blurred': lua.table_from(blurred or []), 'hood': hood, 'late': late, 'hoodDown': hood_down})
    lua.execute(STUB, world)
    if floor_y is not None:
        world.floor.transform[4][2] = floor_y
    with open(PARTS, encoding='utf-8') as f:
        parts = lua.execute(f.read())
    parts.update()
    world.late = False
    parts.update()
    world.parts = parts
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


def matrix(t):
    return [[t[i][j] for j in range(1, 5)] for i in range(1, 5)]


def rim(world, wheel):
    """The transform of the node the parts of a wheel are in"""
    for item in world.loaded.values():
        if item.node.parent.name == 'sc_wheel_' + wheel:
            return matrix(item.node.parent.transform)
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
    _, world = run({'CAR_0_RIM_LF': r'parts\rims_mopar_e_t_mag_wheel_15x5.kn5'})
    check('a corner not named keeps its own rim', rim(world, 'RF') is None and all(visible(world, 'RF').values()))


def the_rim_goes_and_the_tyre_and_brake_stay():
    _, world = run({'CAR_0_RIM_LF': r'parts\rims_mopar_e_t_mag_wheel_15x5.kn5'})
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


def a_tyre_makes_the_whole_wheel_ours_as_big_as_acs_tyre():
    at = '1,0,0, 0,1,0, 0,0,1, 0.027,0,0'
    _, world = run({'CAR_0_RIM_RF': 'rim.kn5', 'CAR_0_TYRE_RF': 'tyre.kn5', 'CAR_0_TYRE_RF_AT': at})
    log = list(world.log.values())
    shown = visible(world, 'RF')
    group = rim(world, 'RF')
    scale = 0.30 / 0.313
    # The car's tyre spans -0.045..0.125 (centre 0.04), the part's 0.027-0.101..0.027+0.097 in the rim's space
    check('a tyre hides the car tyre too, the brake stays',
          not shown['tire_tread_RF'] and not shown['whitewall_RF'] and shown['brake_drum_RF'], log)
    check('the wheel is scaled so the tyre is as big as AC says', group and near(group[1][1], scale), log)
    check('the tyre is centred on the car tyre', group and near(group[3][0] + (0.027 - 0.002) * scale, 0.04), log)
    tyre = [i for i in world.loaded.values() if i.file == 'tyre.kn5']
    check('the tyre sits on the rim where its slot puts it', tyre and near(matrix(tyre[0].node.transform)[3][0], 0.027), log)


def a_tyre_without_its_place_on_the_rim_leaves_the_car_its_tyre():
    _, world = run({'CAR_0_RIM_RF': 'rim.kn5', 'CAR_0_TYRE_RF': 'tyre.kn5'})
    check('a tyre without its place on the rim leaves the car its tyre', visible(world, 'RF')['tire_tread_RF'],
          list(world.log.values()))


def blurred_objects_go_and_a_blurred_wheel_node_is_kept_shown():
    lua, world = run({'CAR_0_RIM_LF': 'rim.kn5'}, ['WHEEL_LF', 'RIM_BLUR_LF'])
    log = list(world.log.values())
    check('a blurred rim outside the wheel is hidden', not world.blur.visible, log)
    world.wheels.LF.visible = False   # AC swaps the whole wheel at speed
    world.blur.visible = True
    world.parts.update()
    check('a blurred wheel node is kept shown, its blurred one hidden again', world.wheels.LF.visible and not world.blur.visible, log)


def an_engine_part_goes_into_the_node_holding_the_wheels():
    """With no car over it (no hood in the stub), a part stands out: it goes into the body node, where it sits"""
    _, world = run({'CAR_0_PART_1': 'parts/engines_blower.kn5', 'CAR_0_PART_1_AT': '1,0,0, 0,1,0, 0,0,1, 0,0.8,1.2',
                    'CAR_0_PART_1_BOX': '-0.1,0,-0.2, 0.1,0.3,0.2'})
    part = [i for i in world.loaded.values() if i.file.endswith('engines_blower.kn5')]
    t = part and matrix(part[0].node.transform)
    check('an engine part goes into the body node, never a wheel, where it sits',
          part and part[0].node.parent.name == 'BODYTR' and near(t[3][1], 0.8) and near(t[3][2], 1.2), list(world.log.values()))


def only_the_parts_that_stand_out_of_the_hood_are_shown_never_moved():
    ini = {'CAR_0_PART_1': 'blower.kn5', 'CAR_0_PART_1_AT': '1,0,0, 0,1,0, 0,0,1, 0,0.38,1.3', 'CAR_0_PART_1_BOX': '-0.15,0,-0.3, 0.15,0.29,0.3',
           'CAR_0_PART_2': 'filter.kn5', 'CAR_0_PART_2_AT': '1,0,0, 0,1,0, 0,0,1, 0,0.67,1.44', 'CAR_0_PART_2_BOX': '-0.18,0,-0.18, 0.18,0.1,0.18'}
    _, world = run(ini, hood=0.72)
    at = {i.file: matrix(i.node.transform) for i in world.loaded.values()}
    log = list(world.log.values())
    check('a part under the hood is not shown', 'blower.kn5' not in at, log)
    check('a part standing out of the hood is shown where the engine has it',
          'filter.kn5' in at and near(at['filter.kn5'][3][1], 0.67), log)


def the_hood_is_found_whichever_way_its_faces_look():
    """The rays take both sides of every face: a one-sided hood is found from above whether its face is up or down"""
    ini = {'CAR_0_PART_1': 'blower.kn5', 'CAR_0_PART_1_AT': '1,0,0, 0,1,0, 0,0,1, 0,0.38,1.3', 'CAR_0_PART_1_BOX': '-0.15,0,-0.3, 0.15,0.29,0.3'}
    _, world = run(ini, hood=0.72)
    check('a part under a face-up hood stays under it', not any(i.file == 'blower.kn5' for i in world.loaded.values()),
          list(world.log.values()))
    _, world = run(ini, hood=0.72, hood_down=True)
    check('a part under a face-down hood stays under it', not any(i.file == 'blower.kn5' for i in world.loaded.values()),
          list(world.log.values()))


def a_race_ini_value_is_read_once():
    """A car whose model comes late is tried every frame: race.ini is loaded once, each value read once"""
    _, world = run({'CAR_0_RIM_LF': 'a.kn5', 'CAR_0_PART_1': 'filter.kn5'}, late=True)
    check('race.ini is loaded once however many frames a car waits', world.iniLoads == 1, list(world.log.values()))


def engine_parts_are_placed_and_judged_in_the_models_own_space():
    """AC draws the model by the car's graphics offset (35 cm down on the Chevelle): the parts and the rays follow it"""
    ini = {'CAR_0_MODEL_NODE': 'FLOOR', 'CAR_0_MODEL_AT': '1,0,0, 0,1,0, 0,0,1, 0,0,0',
           'CAR_0_PART_1': 'filter.kn5', 'CAR_0_PART_1_AT': '1,0,0, 0,1,0, 0,0,1, 0,0.67,1.44', 'CAR_0_PART_1_BOX': '-0.18,0,-0.18, 0.18,0.1,0.18'}
    # The hood at 0.72 in the world is 1.07 in the model's space: the filter (top 0.77) stays under it
    _, world = run(ini, hood=0.72, floor_y=-0.35)
    log = list(world.log.values())
    check('in the model space the filter stays under a hood the offset puts higher', not any(i.file == 'filter.kn5' for i in world.loaded.values()), log)
    _, world = run(ini, hood=0.38, floor_y=-0.35)
    part = [i for i in world.loaded.values() if i.file == 'filter.kn5']
    check('and one that stands out is placed in the model space, the offset with it',
          part and near(matrix(part[0].node.transform)[3][1], 0.32), list(world.log.values()))


def a_rim_too_wide_for_the_cars_tyre_brings_its_factory_tyre_along():
    at = '1,0,0, 0,1,0, 0,0,1, 0.027,0,0'
    _, world = run({'CAR_0_RIM_LR': 'wide_rim.kn5', 'CAR_0_TYRE_LR': 'tyre.kn5', 'CAR_0_TYRE_LR_AT': at, 'CAR_0_TYRE_LR_OPTIONAL': '1'})
    shown = visible(world, 'LR')
    check('a rim too wide for the car tyre brings its factory tyre along',
          not shown['WHEEL_LR_SUB4'] and any(i.file == 'tyre.kn5' for i in world.loaded.values()), list(world.log.values()))
    _, world = run({'CAR_0_RIM_LR': 'rim.kn5', 'CAR_0_TYRE_LR': 'tyre.kn5', 'CAR_0_TYRE_LR_AT': at, 'CAR_0_TYRE_LR_OPTIONAL': '1'})
    shown = visible(world, 'LR')
    check('a rim that fits the car tyre leaves the factory tyre out',
          shown['WHEEL_LR_SUB4'] and not any(i.file == 'tyre.kn5' for i in world.loaded.values()), list(world.log.values()))


def a_wheel_that_names_no_tyre_keeps_the_mesh_well_outside_the_rest_as_its_tyre():
    _, world = run({'CAR_0_RIM_RR': 'a.kn5'})
    shown = visible(world, 'RR')
    group = rim(world, 'RR')
    check('a wheel that names no tyre keeps its outermost mesh as the tyre, the brake too',
          shown['Reifen_RR'] and shown['Bremse_disc_RR'] and not shown['Felge_RR'] and not shown['Stern_RR'], list(world.log.values()))
    check('and the part is sized to the rim, not the tyre', group and near(group[1][1], 0.19 / 0.187), list(world.log.values()))


def a_doors_wear_goes_into_the_door_where_it_stands():
    """The wear of what an animation moves goes into the car's node of that name, where it stood: it opens with the door"""
    lua, world = run({'CAR_0_MODEL_NODE': 'FLOOR', 'CAR_0_MODEL_AT': '1,0,0, 0,1,0, 0,0,1, 0,0,0', 'CAR_0_LOOK': 'looks/car_1.kn5'}, floor_y=-0.35)
    group = [c for c in world.door.children.values() if c.name == 'sc_move_DOOR_L']
    world_of = lua.eval('function(n) local w = n.transform; local p = n.parent; while p do w = w:mul(p.transform); p = p.parent end; return w end')
    check('a door wear goes into the door, where it stood (the model 35 cm down)',
          group and near(matrix(world_of(group[0]))[3][1], 0.75 - 0.35) and near(matrix(group[0].transform)[3][0], 0), list(world.log.values()))
    layer = [i for i in world.loaded.values() if i.file == 'looks/car_1.kn5'][0].node
    check('one whose node the car does not have stays in the layer',
          any(c.name == 'sc_move_TRUNK' for c in layer.children.values()) and not any(c.name == 'sc_move_DOOR_L' for c in layer.children.values()),
          list(world.log.values()))


def a_car_whose_model_comes_late_gets_its_parts_once_it_is_there():
    _, world = run({'CAR_0_RIM_LF': 'a.kn5'}, late=True)
    check('a car whose model is not there at first gets its parts once it is', rim(world, 'LF') is not None, list(world.log.values()))


def the_wear_layer_goes_on_the_body_in_the_models_space():
    """The car's wear layer is always loaded, into the body node, where the model is (35 cm down on the Chevelle)"""
    _, world = run({'CAR_0_MODEL_NODE': 'FLOOR', 'CAR_0_MODEL_AT': '1,0,0, 0,1,0, 0,0,1, 0,0,0', 'CAR_0_LOOK': 'looks/car_1.kn5'}, floor_y=-0.35)
    layer = [i for i in world.loaded.values() if i.file == 'looks/car_1.kn5']
    check('the wear layer goes into the body node, in the model space',
          layer and layer[0].node.parent.name == 'BODYTR' and near(matrix(layer[0].node.transform)[3][1], -0.35), list(world.log.values()))


a_wheel_that_names_no_tyre_keeps_the_mesh_well_outside_the_rest_as_its_tyre()
the_wear_layer_goes_on_the_body_in_the_models_space()
a_doors_wear_goes_into_the_door_where_it_stands()
a_car_whose_model_comes_late_gets_its_parts_once_it_is_there()
a_corner_not_named_keeps_its_own_rim()
engine_parts_are_placed_and_judged_in_the_models_own_space()
the_hood_is_found_whichever_way_its_faces_look()
a_race_ini_value_is_read_once()
a_rim_too_wide_for_the_cars_tyre_brings_its_factory_tyre_along()
an_engine_part_goes_into_the_node_holding_the_wheels()
only_the_parts_that_stand_out_of_the_hood_are_shown_never_moved()
a_tyre_makes_the_whole_wheel_ours_as_big_as_acs_tyre()
a_tyre_without_its_place_on_the_rim_leaves_the_car_its_tyre()
blurred_objects_go_and_a_blurred_wheel_node_is_kept_shown()
a_mesh_named_after_its_node_goes_by_its_material()
the_rim_goes_and_the_tyre_and_brake_stay()
the_part_fills_the_lip_flush_with_the_sidewall_facing_out()
sys.exit(1 if failures else 0)
