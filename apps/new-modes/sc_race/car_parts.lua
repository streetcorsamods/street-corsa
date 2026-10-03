--[[
  Street Corsa race mode: the parts a car shows in AC that are not the car's own (docs/roadmap.md, step 28 idea 5).

  Wheels: race.ini [STREET_CORSA] names, for a corner (LF, RF, LR, RR as AC names its wheels):
    CAR_n_RIM_xx=parts\....kn5        the rim's model, a file the game puts next to this one
    CAR_n_TYRE_xx=parts\....kn5       the tyre's model, when the tyre is not the car's own (then the rim is always named)
    CAR_n_TYRE_xx_AT=m11,...,m43      where the tyre sits in the rim's space (rows of a 4x3 matrix, as PartAssembler
                                      mounts it on the rim's slot)
  A corner not named keeps the car's own wheel. On a named corner the part models go in a node of their own under the
  car's WHEEL_xx node, so they turn and steer with it, and the car's own meshes they replace are hidden. Nothing is
  written to the car's folder.

  What the car's meshes are: under the wheel node, the tyre and the brake by mesh or material name (TYRE and BRAKE
  below), the rim everything else. With a rim alone the car keeps its tyre, and the part fills the hole the hidden rim
  leaves: its outer lip as big as the car's, flush with where the car's lip was (no further out than the tyre's
  sidewall), the barrel going in. With a tyre the whole wheel is the parts', scaled together so the tyre is as big as
  AC's tyre (the size the car's data gives it for that tyre), centred on the car's tyre.

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

-- A part mesh at least this share of the biggest one's radius is the lip
local LIP_SHARE = 0.9

local cars = {}   -- by car index: { corners = { { shown, hidden } ... } }, false once nothing is to be done

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

local function raceText(key)
  local ok, value = pcall(function() return ac.INIConfig.raceConfig():get('STREET_CORSA', key, '') end)
  if ok and type(value) == 'string' and value ~= '' then return value end
  return nil
end

-- A matrix from 12 numbers (rows of 4x3); nil when the key is not there or is not 12 numbers. Read without a default:
-- CSP splits a value at its commas
local function raceMatrix(key)
  local ok, value = pcall(function() return ac.INIConfig.raceConfig():get('STREET_CORSA', key) end)
  if not ok or value == nil then return nil end
  local n = {}
  for _, item in ipairs(type(value) == 'table' and value or { value }) do
    for part in tostring(item):gmatch('[^,]+') do n[#n + 1] = tonumber(part) end
  end
  if #n ~= 12 then return nil end
  for i = 1, 12 do if not n[i] then return nil end end
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

local function setUpCorner(carIndex, root, corner, files)
  local where = 'car ' .. carIndex .. ' ' .. CORNERS[corner]
  local wheel = root:findNodes('WHEEL_' .. CORNERS[corner])
  if wheel:size() == 0 then
    log(where .. ': no WHEEL_' .. CORNERS[corner] .. ', its own wheel stays')
    return nil
  end
  wheel = wheel:at(1)
  local toWheel = wheel:getWorldTransformationRaw():inverse()
  local withTyre = files.tyre ~= nil

  -- Left wheels have their outboard side towards +X, right ones towards -X
  local out = (corner == 1 or corner == 3) and 1 or -1

  local meshes = wheel:findMeshes('?')
  local hidden, rimBoxes, tyreBoxes = {}, {}, {}
  for j = 1, meshes:size() do
    local mesh = meshes:at(j)
    local name, material = meshes:name(j), meshes:materialName(j)
    local box = boxInWheel(mesh, toWheel)
    local tyre = named(name, TYRE) or named(material, TYRE)
    local brake = not tyre and (named(name, BRAKE) or named(material, BRAKE))
    local hide = not brake and (withTyre or not tyre)
    log(string.format('%s mesh %s (%s): %s, x %.3f..%.3f, radius %.3f', where, tostring(name), tostring(material),
      hide and 'hidden' or 'kept', box and box.minX or 0, box and box.maxX or 0, box and box.radius or 0))
    if hide then hidden[#hidden + 1] = mesh end
    if box and tyre then tyreBoxes[#tyreBoxes + 1] = box end
    if box and not tyre and not brake then rimBoxes[#rimBoxes + 1] = box end
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

  local group = wheel:createNode('sc_wheel_' .. CORNERS[corner], false)
  local rim = group and group:loadKN5(files.rim)
  local tyre = rim and withTyre and group:loadKN5(files.tyre)
  if not rim or (withTyre and not tyre) then
    log(where .. ': ' .. (rim and files.tyre or files.rim) .. ' did not load, its own wheel stays')
    if group then group:dispose() end
    return nil
  end
  if tyre then tyre:getTransformationRaw():set(files.tyreAt) end

  -- Scaled, turned to face out (a left wheel: half a turn about the vertical), moved along the axle to x
  local scale, x
  local side = out > 0 and -1 or 1
  if withTyre then
    -- The tyre as big as AC's, centred on the car's tyre (or the wheel node, when the car's tyre is not found)
    local partTyre = union(modelBoxes(tyre, files.tyreAt))
    local own = union(tyreBoxes)
    local car = ac.getCar(carIndex)
    local acRadius = car and car.wheels and car.wheels[corner - 1] and car.wheels[corner - 1].tyreRadius or 0
    local radius = acRadius > 0 and acRadius or (own and own.radius or 0)
    if not partTyre or partTyre.radius <= 0 or radius <= 0 then
      log(where .. ': nothing to measure, its own wheel stays')
      group:dispose()
      return nil
    end
    scale = radius / partTyre.radius
    local centre = own and (own.minX + own.maxX) / 2 or 0
    x = centre - side * (partTyre.minX + partTyre.maxX) / 2 * scale
    log(string.format('%s: %s on %s, scale %.3f (tyre %.3f over %.3f), centred at %.3f', where, files.tyre, files.rim,
      scale, radius, partTyre.radius, centre))
  else
    local ownRadius, ownFace = lipOf(rimBoxes, out)
    local partRadius, partFace = lipOf(modelBoxes(rim, mat4x4()), -1)
    if ownRadius <= 0 or partRadius <= 0 or not ownFace or not partFace then
      log(where .. ': nothing to measure, its own wheel stays')
      group:dispose()
      return nil
    end
    -- A hubcap bulges past the lip: the face goes no further out than the tyre's sidewall
    local tyreFace = faceOf(tyreBoxes, out)
    if tyreFace then ownFace = out > 0 and math.min(ownFace, tyreFace) or math.max(ownFace, tyreFace) end
    scale = ownRadius / partRadius
    x = ownFace - side * partFace * scale
    log(string.format('%s: %s in, scale %.3f (lip %.3f over %.3f), face at %.3f', where, files.rim, scale, ownRadius,
      partRadius, ownFace))
  end
  group:getTransformationRaw():set(mat4x4(
    vec4(side * scale, 0, 0, 0),
    vec4(0, scale, 0, 0),
    vec4(0, 0, side * scale, 0),
    vec4(x, 0, 0, 1)))

  for _, object in ipairs(hidden) do object:setVisible(false) end
  log(where .. ': ' .. #hidden .. ' of the car\'s own hidden')
  return { shown = shown, hidden = hidden }
end

local function setUpCar(carIndex)
  local files, any = {}, false
  for corner = 1, 4 do
    local key = 'CAR_' .. carIndex .. '_'
    local rim = raceText(key .. 'RIM_' .. CORNERS[corner])
    local tyre = raceText(key .. 'TYRE_' .. CORNERS[corner])
    local tyreAt = tyre and raceMatrix(key .. 'TYRE_' .. CORNERS[corner] .. '_AT')
    if rim then
      files[corner] = { rim = rim, tyre = tyreAt and tyre, tyreAt = tyreAt }
      any = true
      if tyre and not tyreAt then log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ': the tyre has no place on the rim, the car keeps its tyre') end
    end
  end
  if not any then return false end

  local root = ac.findNodes('carRoot:' .. carIndex)
  if root:size() == 0 then
    log('car ' .. carIndex .. ' has no model to put parts on')
    return false
  end

  local corners = {}
  for corner = 1, 4 do
    if files[corner] then
      local ok, result = pcall(setUpCorner, carIndex, root, corner, files[corner])
      if not ok then log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ' failed: ' .. tostring(result)) end
      if ok and result then corners[#corners + 1] = result end
    end
  end
  return #corners > 0 and { corners = corners } or false
end

-- Every frame: each car set up once its model is there, then its own kept hidden and the wheel kept shown (AC shows and
-- hides its blurred objects by speed)
function M.update()
  local sim = ac.getSim()
  for carIndex = 0, math.min(1, sim.carsCount - 1) do
    local car = cars[carIndex]
    if car == nil then
      local ok, result = pcall(setUpCar, carIndex)
      if not ok then log('car ' .. carIndex .. ' failed: ' .. tostring(result)) end
      cars[carIndex] = ok and result or false
      if #logLines > 0 then saveLog() end
    elseif car then
      for _, corner in ipairs(car.corners) do
        for _, object in ipairs(corner.hidden) do object:setVisible(false) end
        for _, object in ipairs(corner.shown) do object:setVisible(true) end
      end
    end
  end
end

return M
