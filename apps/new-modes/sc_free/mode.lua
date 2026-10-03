--[[
  Street Corsa free run (manifest.ini): AC's practice session as it is, with the parts the car shows that its model
  does not have put on (car_parts.lua, race.ini [STREET_CORSA] CAR_0_*).
]]

local told = false

-- Nothing to wait for: the session goes as AC runs it
function script.prepare(dt)
  return true
end

function script.update(dt)
  local ok, err = pcall(function() require('car_parts').update() end)
  if not ok and not told then
    told = true
    ac.log('[Street Corsa] Parts: ' .. tostring(err))
  end
end
