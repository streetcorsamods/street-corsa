--[[
  Street Corsa race mode: the parts a car shows in AC that are not the car's own (docs/roadmap.md, step 28 idea 5).

  Wheels: race.ini [STREET_CORSA] names, for a corner (LF, RF, LR, RR as AC names its wheels):
    CAR_n_RIM_xx=parts\....kn5        the rim's model, a file the game puts next to this one
    CAR_n_TYRE_xx=parts\....kn5       the tyre's model, when the tyre is not the car's own (then the rim is always named)
    CAR_n_TYRE_xx_AT=m11,...,m43      where the tyre sits in the rim's space (rows of a 4x3 matrix, as PartAssembler
                                      mounts it on the rim's slot)
    CAR_n_TYRE_xx_OPTIONAL=1          the tyre is the factory one: it comes along only when the rim alone would be
                                      too wide for the car's tyre (RIM_WIDER_THAN_TYRE)
  A corner not named keeps the car's own wheel. On a named corner the part models go in a node of their own under the
  car's WHEEL_xx node, so they turn and steer with it, and the car's own meshes they replace are hidden. Nothing is
  written to the car's folder.

  Engine parts that stand out of the car (an air cleaner, a supercharger, a scoop up through the hood): CAR_n_PART_1,
  _2... name a model of the engine's upper half, CAR_n_PART_k_AT where it sits in the space of the car's model (placed by
  the game as its garage places the engine) and CAR_n_PART_k_BOX the box its model fills (min x, y, z, max x, y, z). A
  part is loaded, where the engine has it, when it stands out of the car: a corner of its box or the middle of its top
  above the car's highest surface straight over it (a ray down onto the car), or nothing of the car over it. One under
  the hood is not loaded. They go into the car's body node (AC's BODYTR) so they move with the body and are drawn with
  it (a model loaded straight under the car's root was placed but never drawn, one in the node over a wheel went round
  with the wheel: found in the game 2026-10-03). A hood a part goes through stays as it is.

  Wear: CAR_n_LOOK=looks\....kn5 names the car's wear layer (rust, worn paint, scrapes, a cracked windscreen; the game
  draws it for the car, docs/roadmap.md step 28 ideas 3 and 4): a model over the car's body in the space of its model,
  loaded into the body node like the engine parts, always. Its groups named sc_move_<node> are the wear of what the car's
  animations move (a door, the hood): each goes into the car's node of that name, where it stands, so it opens with it.

  Blur: the car's blurred_objects.ini objects of a named corner are hidden (our parts have no blurred model), except a
  tyre's while the car keeps its tyre; one that is the wheel node itself or holds it (some cars swap the whole wheel at
  speed) is kept shown.

  Part models are SLRR's: the axle along X, the face towards -X, the meshes straight under an unturned root. Only the
  car's main model is touched: its far LODs keep their own wheels.
]]

local M = {}

-- AC's wheel order (0 left front, 1 right front, 2 left rear, 3 right rear) and node names
local CORNERS = { 'LF', 'RF', 'LR', 'RR' }

-- Mesh or material names (lowercase) that are not the rim: the tyre and the brake turn with the wheel too. Some cars
-- name their meshes after the node only (the Chevelle '70: WHEEL_LF_SUB0..5), their materials say what they are
local TYRE = { 'tyre', 'tire', 'tread', 'wall' }
local BRAKE = { 'brake', 'drum', 'disc', 'disk', 'caliper', 'rotor' }

-- A car that names no tyre (a mod's 'rubber', 'pneu', 'Reifen'): the meshes at least TYRE_SHARE of the biggest radius are
-- its tyre, when every other is within RIM_IN_TYRE of it (WheelFit.Tyres)
local TYRE_SHARE = 0.97
local RIM_IN_TYRE = 0.85

-- A part mesh at least this share of the biggest one's radius is the lip
local LIP_SHARE = 0.9

-- How much wider than the car's tyre a rim alone may come out before the tyre on it comes along too (WheelFit)
local RIM_WIDER_THAN_TYRE = 1.15

local cars = {}   -- by car index: { corners = { { shown, hidden } ... } }, false once nothing is to be done
local waiting = {}   -- by car index: true while its model is not there yet (told once)

-- What was put where, also kept in Documents\Assetto Corsa\out\sc_race_parts.log (AC keeps no file of ac.log)
local logLines = {}
local function log(text)
  ac.log('[Street Corsa] Parts: ' .. text)
  logLines[#logLines + 1] = text
end
local function saveLog()
  pcall(function()
    io.save(ac.getFolder(ac.FolderID.ACDocuments) .. '\\out\\sc_race_parts.log', table.concat(logLines, '\n') .. '\n')
  end)
end

-- race.ini is loaded once and each [STREET_CORSA] value read once: a car whose model comes late is tried every frame,
-- some 15 keys a try, and each raceConfig() loads the file again. A value as text (with a default) and as CSP splits it
-- at its commas (without one) are kept apart; false is a value not there
local raceIni = nil
local raceRead = {}
local function raceValue(key, asText)
  local memo = (asText and 'text:' or 'raw:') .. key
  local value = raceRead[memo]
  if value == nil then
    local ok, read = pcall(function()
      raceIni = raceIni or ac.INIConfig.raceConfig()
      if asText then return raceIni:get('STREET_CORSA', key, '') end
      return raceIni:get('STREET_CORSA', key)
    end)
    value = ok and read ~= nil and read or false
    raceRead[memo] = value
  end
  if value == false then return nil end
  return value
end

local function raceText(key)
  local value = raceValue(key, true)
  if type(value) == 'string' and value ~= '' then return value end
  return nil
end

-- `count` numbers from a comma list; nil when the key is not there or is not that many numbers. Read without a
-- default: CSP splits a value at its commas
local function raceNumbers(key, count)
  local value = raceValue(key, false)
  if value == nil then return nil end
  local n = {}
  for _, item in ipairs(type(value) == 'table' and value or { value }) do
    for part in tostring(item):gmatch('[^,]+') do n[#n + 1] = tonumber(part) end
  end
  if #n ~= count then return nil end
  for i = 1, count do if not n[i] then return nil end end
  return n
end

-- A matrix from 12 numbers (rows of 4x3); nil when the key is not there or is not 12 numbers
local function raceMatrix(key)
  local n = raceNumbers(key, 12)
  if not n then return nil end
  return mat4x4(vec4(n[1], n[2], n[3], 0), vec4(n[4], n[5], n[6], 0), vec4(n[7], n[8], n[9], 0), vec4(n[10], n[11], n[12], 1))
end

local function named(name, words)
  local lower = string.lower(name or '')
  for _, word in ipairs(words) do
    if string.find(lower, word, 1, true) then return true end
  end
  return false
end

-- The box of a mesh's local corners through m: x along the axle, and its radius (the farthest y or z off the axle)
local _corner = vec3()
local function boxThrough(mesh, m)
  local min, max = mesh:getLocalAABB()
  if not min or not max then return nil end
  local box = { minX = math.huge, maxX = -math.huge, radius = 0 }
  for i = 0, 7 do
    _corner:set(i % 2 == 0 and min.x or max.x, math.floor(i / 2) % 2 == 0 and min.y or max.y, i < 4 and min.z or max.z)
    local p = m:transformPoint(_corner)
    box.minX = math.min(box.minX, p.x)
    box.maxX = math.max(box.maxX, p.x)
    box.radius = math.max(box.radius, math.abs(p.y), math.abs(p.z))
  end
  return box
end

-- A car's mesh in the wheel's space
local function boxInWheel(mesh, toWheel)
  return boxThrough(mesh, mesh:getParent():getWorldTransformationRaw():mul(toWheel))
end

-- A part model's meshes in the space it is set in by m (its meshes straight under its root)
local function modelBoxes(model, m)
  local boxes = {}
  local meshes = model:findMeshes('?')
  for j = 1, meshes:size() do
    local box = boxThrough(meshes:at(j), m)
    if box then boxes[#boxes + 1] = box end
  end
  return boxes
end

local function union(boxes)
  local all = { minX = math.huge, maxX = -math.huge, radius = 0 }
  for _, box in ipairs(boxes) do
    all.minX = math.min(all.minX, box.minX)
    all.maxX = math.max(all.maxX, box.maxX)
    all.radius = math.max(all.radius, box.radius)
  end
  return #boxes > 0 and all or nil
end

-- How far out a set of boxes reaches (x), outboard being `out` (1 or -1)
local function faceOf(boxes, out)
  local face = nil
  for _, box in ipairs(boxes) do
    local x = out > 0 and box.maxX or box.minX
    face = face == nil and x or (out > 0 and math.max(face, x) or math.min(face, x))
  end
  return face
end

-- The lip of a set of boxes: its radius, and how far out it reaches
local function lipOf(boxes, out)
  local radius = 0
  for _, box in ipairs(boxes) do radius = math.max(radius, box.radius) end
  local lip = {}
  for _, box in ipairs(boxes) do
    if box.radius >= radius * LIP_SHARE then lip[#lip + 1] = box end
  end
  return radius, faceOf(lip, out)
end

-- The car's blurred objects of a wheel (AC's index), by name
local function blurredOf(carIndex, wheelIndex)
  local names = {}
  pcall(function()
    local ini = ac.INIConfig.carData(carIndex, 'blurred_objects.ini')
    for _, section in ini:iterate('OBJECT') do
      if ini:get(section, 'WHEEL_INDEX', -1) == wheelIndex then
        local name = ini:get(section, 'NAME', '')
        if name ~= '' then names[#names + 1] = name end
      end
    end
  end)
  return names
end

-- The wheel node and the nodes holding it, by name
local function holders(wheel)
  local found = {}
  local node = wheel
  for _ = 1, 32 do
    if node:size() == 0 then break end
    found[node:name()] = node
    node = node:getParent()
  end
  return found
end

-- Where a rim alone goes: its scale and where along the axle (x), nil when there is nothing to measure. The part fills
-- the hole the hidden rim leaves: its lip as big as the car's, flush with where the car's lip was, no further out than the
-- tyre's sidewall (a hubcap bulges past the lip)
local function rimAlone(rimBoxes, tyreBoxes, partBoxes, out, side)
  local ownRadius, ownFace = lipOf(rimBoxes, out)
  local partRadius, partFace = lipOf(partBoxes, -1)
  if ownRadius <= 0 or partRadius <= 0 or not ownFace or not partFace then return nil end
  local tyreFace = faceOf(tyreBoxes, out)
  if tyreFace then ownFace = out > 0 and math.min(ownFace, tyreFace) or math.max(ownFace, tyreFace) end
  local scale = ownRadius / partRadius
  return scale, ownFace - side * partFace * scale, ownRadius, partRadius, ownFace
end

-- The tyre of a wheel that names none: the meshes well outside all the rest (WheelFit.Tyres)
local function tyreBySize(own)
  for _, m in ipairs(own) do if m.tyre then return end end
  local others, biggest = {}, 0
  for _, m in ipairs(own) do
    if not m.brake and m.box then
      others[#others + 1] = m
      biggest = math.max(biggest, m.box.radius)
    end
  end
  if #others < 2 then return end
  local outer = 0
  for _, m in ipairs(others) do
    if m.box.radius >= biggest * TYRE_SHARE then outer = outer + 1
    elseif m.box.radius > biggest * RIM_IN_TYRE then return end
  end
  if outer == #others then return end
  for _, m in ipairs(others) do
    if m.box.radius >= biggest * TYRE_SHARE then m.tyre = true end
  end
end

local function setUpCorner(carIndex, root, corner, files)
  local where = 'car ' .. carIndex .. ' ' .. CORNERS[corner]
  local wheel = root:findNodes('WHEEL_' .. CORNERS[corner])
  if wheel:size() == 0 then
    log(where .. ': no WHEEL_' .. CORNERS[corner] .. ', its own wheel stays')
    return nil
  end
  wheel = wheel:at(1)
  local toWheel = wheel:getWorldTransformationRaw():inverse()

  -- Left wheels have their outboard side towards +X, right ones towards -X; a left wheel's part gets half a turn
  local out = (corner == 1 or corner == 3) and 1 or -1
  local side = out > 0 and -1 or 1

  -- The car's meshes under the wheel: what each is and where
  local meshes = wheel:findMeshes('?')
  local own, rimBoxes, tyreBoxes = {}, {}, {}
  for j = 1, meshes:size() do
    local name, material = meshes:name(j), meshes:materialName(j)
    local tyre = named(name, TYRE) or named(material, TYRE)
    own[#own + 1] = { mesh = meshes:at(j), name = name, material = material, box = boxInWheel(meshes:at(j), toWheel), tyre = tyre,
      brake = not tyre and (named(name, BRAKE) or named(material, BRAKE)) }
  end
  tyreBySize(own)
  for _, m in ipairs(own) do
    if m.box and m.tyre then tyreBoxes[#tyreBoxes + 1] = m.box end
    if m.box and not m.tyre and not m.brake then rimBoxes[#rimBoxes + 1] = m.box end
  end

  local group = wheel:createNode('sc_wheel_' .. CORNERS[corner], false)
  local rim = group and group:loadKN5(files.rim)
  if not rim then
    log(where .. ': ' .. files.rim .. ' did not load, its own wheel stays')
    if group then group:dispose() end
    return nil
  end
  local partRim = modelBoxes(rim, mat4x4.identity())

  -- The factory tyre comes along only when the rim alone would be too wide for the car's tyre (WheelFit.RimWiderThanTyre)
  local withTyre = files.tyre ~= nil
  local aloneScale, aloneX, ownRadius, partRadius, ownFace = rimAlone(rimBoxes, tyreBoxes, partRim, out, side)
  if withTyre and files.tyreOptional then
    local ownTyre, partWidth = union(tyreBoxes), union(partRim)
    local rimWidth = aloneScale and partWidth and (partWidth.maxX - partWidth.minX) * aloneScale
    local tyreWidth = ownTyre and (ownTyre.maxX - ownTyre.minX)
    withTyre = not (rimWidth and tyreWidth and rimWidth <= tyreWidth * RIM_WIDER_THAN_TYRE)
    log(string.format('%s: the rim alone %.3f wide, the car\'s tyre %.3f: %s', where, rimWidth or 0, tyreWidth or 0,
      withTyre and 'the tyre comes along' or 'the car keeps its tyre'))
  end

  local tyre = withTyre and group:loadKN5(files.tyre)
  if withTyre and not tyre then
    log(where .. ': ' .. files.tyre .. ' did not load, its own wheel stays')
    group:dispose()
    return nil
  end
  if tyre then tyre:getTransformationRaw():set(files.tyreAt) end

  local scale, x
  if withTyre then
    -- The tyre as big as AC's, centred on the car's tyre (or the wheel node, when the car's tyre is not found)
    local partTyre = union(modelBoxes(tyre, files.tyreAt))
    local ownTyre = union(tyreBoxes)
    local car = ac.getCar(carIndex)
    local acRadius = car and car.wheels and car.wheels[corner - 1] and car.wheels[corner - 1].tyreRadius or 0
    local radius = acRadius > 0 and acRadius or (ownTyre and ownTyre.radius or 0)
    if not partTyre or partTyre.radius <= 0 or radius <= 0 then
      log(where .. ': nothing to measure, its own wheel stays')
      group:dispose()
      return nil
    end
    scale = radius / partTyre.radius
    local centre = ownTyre and (ownTyre.minX + ownTyre.maxX) / 2 or 0
    x = centre - side * (partTyre.minX + partTyre.maxX) / 2 * scale
    log(string.format('%s: %s on %s, scale %.3f (tyre %.3f over %.3f), centred at %.3f', where, files.tyre, files.rim,
      scale, radius, partTyre.radius, centre))
  else
    if not aloneScale then
      log(where .. ': nothing to measure, its own wheel stays')
      group:dispose()
      return nil
    end
    scale, x = aloneScale, aloneX
    log(string.format('%s: %s in, scale %.3f (lip %.3f over %.3f), face at %.3f', where, files.rim, scale, ownRadius,
      partRadius, ownFace))
  end
  group:getTransformationRaw():set(mat4x4(
    vec4(side * scale, 0, 0, 0),
    vec4(0, scale, 0, 0),
    vec4(0, 0, side * scale, 0),
    vec4(x, 0, 0, 1)))

  -- The car's own that go: the rim always, the tyre too when ours comes along; never the brake
  local hidden = {}
  for _, m in ipairs(own) do
    local hide = not m.brake and (withTyre or not m.tyre)
    log(string.format('%s mesh %s (%s): %s, x %.3f..%.3f, radius %.3f', where, tostring(m.name), tostring(m.material),
      hide and 'hidden' or 'kept', m.box and m.box.minX or 0, m.box and m.box.maxX or 0, m.box and m.box.radius or 0))
    if hide then hidden[#hidden + 1] = m.mesh end
  end

  -- The car's blurred objects of this wheel
  local shown, inWheel = {}, holders(wheel)
  for _, name in ipairs(blurredOf(carIndex, corner - 1)) do
    if inWheel[name] then
      shown[#shown + 1] = inWheel[name]
      log(where .. ': blurred object ' .. name .. ' holds the wheel, kept shown')
    elseif withTyre or not named(name, TYRE) then
      local objects = root:findAny(name)
      for j = 1, objects:size() do hidden[#hidden + 1] = objects:at(j) end
      log(where .. ': blurred object ' .. name .. ' hidden (' .. objects:size() .. ')')
    end
  end

  for _, object in ipairs(hidden) do object:setVisible(false) end
  log(where .. ': ' .. #hidden .. ' of the car\'s own hidden')
  return { shown = shown, hidden = hidden }
end

-- How far over a point the ray that finds the car's surface starts, metres (ShownEngineParts.RayFrom)
local RAY_FROM = 3

-- The car's highest surface straight over a point of its model (the height in the model's space); nil when nothing is
-- there. `model` takes the model's space to the world's
local function surfaceAbove(root, model, x, y, z)
  local from = model:transformPoint(vec3(x, y + RAY_FROM, z))
  local down = model:transformVector(vec3(0, -1, 0))
  local scale = down:length()
  -- Both sides of every face (culling -1; 0 would hit back faces only, and miss a one-sided hood's top): the first one
  -- hit from above is the car's highest surface there
  local distance = root:raycast(render.createRay(from, down:normalize(), RAY_FROM * 2 * scale), nil, nil, nil, nil, -1)
  if type(distance) ~= 'number' or distance < 0 then return nil end
  return y + RAY_FROM - distance / scale
end

-- What takes the space of the car's model (its KN5's) to the world: by the frame node the game names (CAR_n_MODEL_NODE,
-- where it sits in the KN5 in CAR_n_MODEL_AT), since AC moves the model by the car's GRAPHICS_OFFSET and pitch (the
-- Chevelle '70's is drawn 35 cm lower than its KN5 says); the car's root, as before, without one
local function modelSpace(carIndex, root)
  local name = raceText('CAR_' .. carIndex .. '_MODEL_NODE')
  local at = name and raceMatrix('CAR_' .. carIndex .. '_MODEL_AT')
  local node = at and root:findNodes(name)
  if node and node:size() > 0 then
    log('car ' .. carIndex .. ': the model space found by ' .. name)
    return at:inverse():mul(node:at(1):getWorldTransformationRaw())
  end
  if name then log('car ' .. carIndex .. ': no ' .. name .. ' in the model, the car root is taken for its space') end
  return root:getWorldTransformationRaw()
end

-- The node of the car's body, models in it drawn with the car and moving with its body: AC's BODYTR, else its CARNODE.
-- Not what holds a wheel node: AC puts each wheel in a WHEEL_TR of its own, which steers and turns (found in the game
-- 2026-10-03, the supercharger going round with the front wheel)
local function bodyNode(root)
  for _, name in ipairs({ 'BODYTR', 'CARNODE' }) do
    local node = root:findNodes(name)
    if node:size() > 0 then return node:at(1) end
  end
  return nil
end

-- How far a part must reach past the car's surface to count as standing out of it, metres (ShownEngineParts.OutBy)
local OUT_BY = 0.005

-- A box's eight corners and the middle of its top, in the car's model by `at`
local function boxPoints(box, at)
  local points = {}
  local min, max = box.min, box.max
  for i = 0, 7 do
    points[#points + 1] = at:transformPoint(vec3(i % 2 == 0 and min.x or max.x, math.floor(i / 2) % 2 == 0 and min.y or max.y,
      i < 4 and min.z or max.z))
  end
  points[#points + 1] = at:transformPoint(vec3((min.x + max.x) / 2, max.y, (min.z + max.z) / 2))
  return points
end

-- Whether a part stands out of the car (ShownEngineParts.StandsOut): a point of its box higher than the car's surface
-- straight over it, or with nothing of the car over it
local function standsOut(root, model, box, at)
  for _, p in ipairs(boxPoints(box, at)) do
    local surface = surfaceAbove(root, model, p.x, p.y, p.z)
    if not surface or p.y > surface + OUT_BY then return true end
  end
  return false
end

-- The engine parts that stand out of the car, where the engine has them, in its body node
local function setUpExtras(carIndex, root)
  local parts = {}
  for k = 1, 64 do
    local key = 'CAR_' .. carIndex .. '_PART_' .. k
    local file = raceText(key)
    if not file then break end
    local n = raceNumbers(key .. '_BOX', 6)
    local box = n and { min = vec3(n[1], n[2], n[3]), max = vec3(n[4], n[5], n[6]) }
    parts[#parts + 1] = { file = file, at = raceMatrix(key .. '_AT'), box = box }
  end

  -- Which stand out, judged before anything of ours is in (the rays would find our own parts)
  local model = modelSpace(carIndex, root)
  local standing = {}
  for _, part in ipairs(parts) do
    if part.at and part.box then
      local ok, out = pcall(standsOut, root, model, part.box, part.at)
      if not ok then log('car ' .. carIndex .. ': ' .. part.file .. ' could not be judged: ' .. tostring(out)) end
      if ok and out then standing[#standing + 1] = part end
      log('car ' .. carIndex .. ': ' .. part.file .. (ok and out and ' stands out' or ' stays under the hood'))
    end
  end
  if #standing == 0 then return {} end

  -- Where they go, and what takes them from the model's space to the holder's: through the world, whatever lies between
  local holder = bodyNode(root) or root
  local fromCar = model:mul(holder:getWorldTransformationRaw():inverse())
  log('car ' .. carIndex .. ': engine parts go into ' .. tostring(holder:name()) .. ' (in ' .. tostring(holder:getParent():name()) .. ')')

  local loaded = {}
  for _, part in ipairs(standing) do
    local partModel = holder:loadKN5(part.file)
    if partModel then
      partModel:getTransformationRaw():set(part.at:mul(fromCar))
      loaded[#loaded + 1] = partModel
    else
      log('car ' .. carIndex .. ': ' .. part.file .. ' did not load')
    end
  end
  return loaded
end

-- The start of the name of a group of the wear layer that moves with a node of the car (WearLayer.MoverPrefix)
local MOVER_PREFIX = 'sc_move_'

-- The wear of what the car's animations move (a door's, the hood's) into the car's node each moves with, where it stands
-- now (the car is loaded at rest, its doors shut): it opens with the door. One whose node the car does not have stays put.
local function putOnMovers(carIndex, root, holder, layer, fromCar)
  -- The groups hang off the model's root, which may be the node loaded or the one in it: looked for a few levels down
  local groups = {}
  -- toLayer: what takes a child of `at` into the layer's own space
  local function collect(at, toLayer, depth)
    for k = 1, at:getChildrenCount() do
      local child = at:getChild(k)
      local name = child:name()
      if name and name:sub(1, #MOVER_PREFIX) == MOVER_PREFIX then
        groups[#groups + 1] = { group = child, node = name:sub(#MOVER_PREFIX + 1), toLayer = toLayer }
      elseif depth < 2 then
        collect(child, child:getTransformationRaw():mul(toLayer), depth + 1)
      end
    end
  end
  collect(layer, mat4x4.identity(), 1)
  for _, g in ipairs(groups) do
    local found = holder:findNodes(g.node)
    if found:size() == 0 then found = root:findNodes(g.node) end
    if found:size() > 0 then
      local mover = found:at(1)
      -- Worked out, not read: the layer was placed this frame, and what CSP has for its groups' world may be from before
      local world = g.group:getTransformationRaw():mul(g.toLayer):mul(fromCar):mul(holder:getWorldTransformationRaw())
      g.group:setParent(mover)
      g.group:getTransformationRaw():set(world:mul(mover:getWorldTransformationRaw():inverse()))
      log('car ' .. carIndex .. ': wear of ' .. g.node .. ' moves with it')
    else
      log('car ' .. carIndex .. ': no node ' .. g.node .. ' for its wear, it stays put')
    end
  end
end

-- The car's wear layer over its body, where the car's model is
local function setUpLook(carIndex, root)
  local file = raceText('CAR_' .. carIndex .. '_LOOK')
  if not file then return nil end
  local holder = bodyNode(root) or root
  local fromCar = modelSpace(carIndex, root):mul(holder:getWorldTransformationRaw():inverse())
  local layer = holder:loadKN5(file)
  if not layer then
    log('car ' .. carIndex .. ': its wear ' .. file .. ' did not load')
    return nil
  end
  layer:getTransformationRaw():set(fromCar)
  log('car ' .. carIndex .. ': wear ' .. file .. ' put on in ' .. tostring(holder:name()))
  putOnMovers(carIndex, root, holder, layer, fromCar)
  return layer
end

-- The car's paint in the player's own colour: each tinted texture (CAR_n_PAINT_k) in the place of the car's texture of
-- that name (CAR_n_PAINT_k_TEXTURE), as a skin's file would be, wherever the car draws from it
local function setUpPaint(carIndex, root)
  local folder = ac.getFolder(ac.FolderID.ScriptOrigin)
  local skin = {}
  local count = 0
  for k = 1, 16 do
    local key = 'CAR_' .. carIndex .. '_PAINT_' .. k
    local file = raceText(key)
    local texture = raceText(key .. '_TEXTURE')
    if not file or not texture then break end
    skin[texture] = folder .. '\\' .. file
    count = count + 1
  end
  if count == 0 then return false end
  root:applySkin(skin)
  log('car ' .. carIndex .. ': painted (' .. count .. ' texture(s))')
  return true
end

local function setUpCar(carIndex)
  -- What the car has, from the values read once (a car that has nothing is done with before its model is looked for)
  local files, any = {}, raceText('CAR_' .. carIndex .. '_PART_1') ~= nil or raceText('CAR_' .. carIndex .. '_LOOK') ~= nil
    or raceText('CAR_' .. carIndex .. '_PAINT_1') ~= nil
  for corner = 1, 4 do
    local key = 'CAR_' .. carIndex .. '_'
    local rim = raceText(key .. 'RIM_' .. CORNERS[corner])
    local tyre = raceText(key .. 'TYRE_' .. CORNERS[corner])
    local tyreAt = tyre and raceMatrix(key .. 'TYRE_' .. CORNERS[corner] .. '_AT')
    if rim then
      files[corner] = { rim = rim, tyre = tyreAt and tyre, tyreAt = tyreAt,
        tyreOptional = raceText(key .. 'TYRE_' .. CORNERS[corner] .. '_OPTIONAL') == '1' }
      any = true
      if tyre and not tyreAt then log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ': the tyre has no place on the rim, the car keeps its tyre') end
    end
  end
  if not any then return false end

  -- No model yet (CSP may build a car's node after the first frame): nil, tried again the next frame
  local root = ac.findNodes('carRoot:' .. carIndex)
  if root:size() == 0 then
    if not waiting[carIndex] then log('car ' .. carIndex .. ' has no model to put parts on yet') end
    waiting[carIndex] = true
    return nil
  end

  local extrasOk, extras = pcall(setUpExtras, carIndex, root)
  if not extrasOk then log('car ' .. carIndex .. ' engine parts failed: ' .. tostring(extras)) end

  -- After the engine parts: their rays must not find the layer
  local lookOk, look = pcall(setUpLook, carIndex, root)
  if not lookOk then log('car ' .. carIndex .. ' wear failed: ' .. tostring(look)) end

  local paintOk, painted = pcall(setUpPaint, carIndex, root)
  if not paintOk then log('car ' .. carIndex .. ' paint failed: ' .. tostring(painted)) end

  local corners = {}
  for corner = 1, 4 do
    if files[corner] then
      local ok, result = pcall(setUpCorner, carIndex, root, corner, files[corner])
      if not ok then log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ' failed: ' .. tostring(result)) end
      if ok and result then corners[#corners + 1] = result end
    end
  end
  return (#corners > 0 or (extrasOk and #extras > 0) or (lookOk and look) or (paintOk and painted)) and { corners = corners } or false
end

-- Every frame: each car set up once its model is there, then its own kept hidden and the wheel kept shown (AC shows and
-- hides its blurred objects by speed)
function M.update()
  local sim = ac.getSim()
  for carIndex = 0, math.min(1, sim.carsCount - 1) do
    local car = cars[carIndex]
    if car == nil then
      local told = #logLines
      local ok, result = pcall(setUpCar, carIndex)
      if not ok then log('car ' .. carIndex .. ' failed: ' .. tostring(result)) end
      -- A failure is for good; nil (no model yet) leaves the car to the next frame
      if not ok then cars[carIndex] = false elseif result ~= nil then cars[carIndex] = result end
      if #logLines > told then saveLog() end
    elseif car then
      for _, corner in ipairs(car.corners) do
        for _, object in ipairs(corner.hidden) do object:setVisible(false) end
        for _, object in ipairs(corner.shown) do object:setVisible(true) end
      end
    end
  end
end

return M
