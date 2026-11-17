local directory = os.getenv("CABINET_TAKE_DIRECTORY")
local effect = os.getenv("CABINET_TAKE_EFFECT")
local analysis = tonumber(os.getenv("CABINET_TAKE_ANALYSIS"))
local scan = tonumber(os.getenv("CABINET_TAKE_SCAN"))
local result = io.open(directory .. "/result.txt", "w")

local function say(text)
  result:write(text .. "\n")
  result:flush()
end

reaper.GetSetProjectInfo_String(0, "RECORD_PATH", directory, true)
reaper.InsertTrackAtIndex(0, true)
reaper.SetOnlyTrackSelected(reaper.GetTrack(0, 0))
reaper.SetEditCurPos(0, false, false)
reaper.InsertMedia(directory .. "/input.wav", 0)
local item = reaper.GetMediaItem(0, 0)
local started = reaper.time_precise()
local attached
local take

local function quit()
  reaper.Main_SaveProject(0, false)
  say("done")
  reaper.Main_OnCommand(40004, 0)
end

local function render()
  if reaper.time_precise() - attached < analysis then
    reaper.defer(render)
    return
  end
  reaper.SelectAllMediaItems(0, false)
  reaper.SetMediaItemSelected(item, true)
  reaper.Main_OnCommand(40209, 0)
  local source = reaper.GetMediaItemTake_Source(reaper.GetActiveTake(item))
  say("rendered " .. reaper.GetMediaSourceFileName(source, ""))
  quit()
end

local function attach()
  local index = reaper.TakeFX_AddByName(take, effect, -1)
  if index >= 0 then
    say("attached")
    reaper.TakeFX_Show(take, index, 3)
    attached = reaper.time_precise()
    reaper.defer(render)
  elseif reaper.time_precise() - started > scan then
    say("missing")
    quit()
  else
    reaper.defer(attach)
  end
end

if item then
  take = reaper.GetActiveTake(item)
  reaper.defer(attach)
else
  say("no item")
  quit()
end
