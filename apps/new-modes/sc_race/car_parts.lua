--[[
  Street Corsa race mode: the parts a car shows in AC that are not the car's own (docs/roadmap.md, step 28 idea 5).

  Rims: race.ini [STREET_CORSA] CAR_n_RIM_LF / _RF / _LR / _RR name the KN5 of the rim on that corner, a file the game
  puts next to this one (parts\...). A corner not named keeps the car's own rim. On a named corner the car's rim meshes
  under its WHEEL_xx node are hidden and the part's model goes in their place, a child of the wheel node, so it turns and
  steers with it. Nothing is written to the car's folder.

  What is the car's rim: every mesh under the wheel node except the tyre and the brake (by mesh or material name, TYRE
  and BRAKE below).
  The part fills the hole the hidden rim leaves: its outer lip as big as the car's, flush with where the car's lip was
  (no further out than the tyre's sidewall), the barrel going in. Part models are SLRR's: the axle along X, the face
  towards -X.

  Only the car's main model is touched: its far LODs keep their own rims.
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

local cars = {}   -- by car index: { corners = { { wheel, hidden, rim } ... } }, false once nothing is to be done

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

local function rimFile(carIndex, corner)
  local ok, value = pcall(function()
    return ac.INIConfig.raceConfig():get('STREET_CORSA', 'CAR_' .. carIndex .. '_RIM_' .. CORNERS[corner], '')
  end)
  if ok and type(value) == 'string' and value ~= '' then return value end
  return nil
end

local function named(name, words)
  local lower = string.lower(name or '')
  for _, word in ipairs(words) do
    if string.find(lower, word, 1, true) then return true end
  end
  return false
end

-- The box of a mesh in the wheel's space: x along the axle, and its radius (the farthest y or z off the axle)
local _corner = vec3()
local function boxInWheel(mesh, toWheel)
  local min, max = mesh:getLocalAABB()
  if not min or not max then return nil end
  local parent = mesh:getParent()
  local m = parent:getWorldTransformationRaw():mul(toWheel)
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

-- The lip of a set of boxes: its radius, and how far out it reaches (x, outboard being `out`: 1 or -1)
local function lipOf(boxes, out)
  local radius = 0
  for _, box in ipairs(boxes) do radius = math.max(radius, box.radius) end
  local face = nil
  for _, box in ipairs(boxes) do
    if box.radius >= radius * LIP_SHARE then
      local x = out > 0 and box.maxX or box.minX
      face = face == nil and x or (out > 0 and math.max(face, x) or math.min(face, x))
    end
  end
  return radius, face
end

local function setUpCorner(carIndex, root, corner, file)
  local wheel = root:findNodes('WHEEL_' .. CORNERS[corner])
  if wheel:size() == 0 then
    log('car ' .. carIndex .. ' has no WHEEL_' .. CORNERS[corner] .. ': its own rim stays')
    return nil
  end
  wheel = wheel:at(1)
  local toWheel = wheel:getWorldTransformationRaw():inverse()

  -- Left wheels have their outboard side towards +X, right ones towards -X
  local out = (corner == 1 or corner == 3) and 1 or -1

  local meshes = wheel:findMeshes('?')
  local hidden, ownBoxes, tyreFace = {}, {}, nil
  for j = 1, meshes:size() do
    local mesh = meshes:at(j)
    local name = meshes:name(j)
    local material = meshes:materialName(j)
    local box = boxInWheel(mesh, toWheel)
    local tyre = named(name, TYRE) or named(material, TYRE)
    local keep = tyre or named(name, BRAKE) or named(material, BRAKE)
    log(string.format('car %d %s mesh %s (%s): %s, x %.3f..%.3f, radius %.3f', carIndex, CORNERS[corner], tostring(name),
      tostring(material), keep and 'kept' or 'hidden', box and box.minX or 0, box and box.maxX or 0, box and box.radius or 0))
    if not keep then
      hidden[#hidden + 1] = mesh
      if box then ownBoxes[#ownBoxes + 1] = box end
    elseif tyre and box then
      local x = out > 0 and box.maxX or box.minX
      tyreFace = tyreFace == nil and x or (out > 0 and math.max(tyreFace, x) or math.min(tyreFace, x))
    end
  end
  if #ownBoxes == 0 then
    log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ': no rim meshes found, its own rim stays')
    return nil
  end

  local rim = wheel:loadKN5(file)
  if not rim then
    log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ': ' .. file .. ' did not load, its own rim stays')
    return nil
  end

  -- The part's lip in its own space (identity, face towards -X)
  local partBoxes = {}
  local partMeshes = rim:findMeshes('?')
  local toRim = rim:getWorldTransformationRaw():inverse()
  for j = 1, partMeshes:size() do
    local box = boxInWheel(partMeshes:at(j), toRim)
    if box then partBoxes[#partBoxes + 1] = box end
  end
  -- A hubcap bulges past the lip: the face goes no further out than the tyre's sidewall
  local ownRadius, ownFace = lipOf(ownBoxes, out)
  if tyreFace then ownFace = out > 0 and math.min(ownFace, tyreFace) or math.max(ownFace, tyreFace) end
  local partRadius, partFace = lipOf(partBoxes, -1)
  if partRadius <= 0 or not partFace then
    log('car ' .. carIndex .. ' ' .. CORNERS[corner] .. ': ' .. file .. ' has nothing to measure, its own rim stays')
    rim:dispose()
    return nil
  end

  -- Scaled to the car's lip, turned to face out (a left wheel: half a turn about the vertical), the lip moved onto the
  -- car's lip
  local scale = ownRadius / partRadius
  local side = out > 0 and -1 or 1
  local x = ownFace - side * partFace * scale
  rim:getTransformationRaw():set(mat4x4(
    vec4(side * scale, 0, 0, 0),
    vec4(0, scale, 0, 0),
    vec4(0, 0, side * scale, 0),
    vec4(x, 0, 0, 1)))

  for _, mesh in ipairs(hidden) do mesh:setVisible(false) end
  log(string.format('car %d %s: %s in, scale %.3f (lip %.3f over %.3f), face at %.3f, %d own meshes hidden',
    carIndex, CORNERS[corner], file, scale, ownRadius, partRadius, ownFace, #hidden))
  return { wheel = wheel, hidden = hidden, rim = rim }
end

local function setUpCar(carIndex)
  local files, any = {}, false
  for corner = 1, 4 do
    files[corner] = rimFile(carIndex, corner)
    any = any or files[corner] ~= nil
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

-- Every frame: each car set up once its model is there, then its own rims kept hidden (AC may show a mesh again)
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
        for _, mesh in ipairs(corner.hidden) do mesh:setVisible(false) end
      end
    end
  end
end

return M
