--[[
  Street Corsa free run (manifest.ini): AC's practice session as it is, with the parts the car shows that its model
  does not have put on (car_parts.lua, race.ini [STREET_CORSA] CAR_0_*).
]]

local told = false

-- The parts module, loaded once rather than looked up every frame; one that does not load fails its update
local carParts = (function()
  local ok, module = pcall(require, 'car_parts')
  if ok and type(module) == 'table' and module.update then return module end
  return { update = function() error('car_parts did not load: ' .. tostring(module), 0) end }
end)()

-- Nothing to wait for: the session goes as AC runs it
function script.prepare(dt)
  return true
end

function script.update(dt)
  local ok, err = pcall(carParts.update)
  if not ok and not told then
    told = true
    ac.log('[Street Corsa] Parts: ' .. tostring(err))
  end
end
