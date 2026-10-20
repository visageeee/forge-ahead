using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Weland
{
    public static class PolygonLua
    {
        public static readonly uint Tag=Wadfile.Chunk("FLUA");
        public const string ScriptName="Forge Ahead Polygon Triggers";
        public const string Header="-- Forge Ahead generated polygon triggers v1\n";
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        public static bool IsGenerated(EmbeddedLuaScript script) => script.Name==ScriptName && script.Text.StartsWith(Header,StringComparison.Ordinal);
        public static void Validate(string code)
        {
            if(code==null || code.Contains('\0') || Utf8.GetByteCount(code)>1024*1024)
                throw new ArgumentException("Trigger code must be valid UTF-8 without null characters, at most 1 MiB.");
        }
        public static void Load(Level level)
        {
            foreach(var polygon in level.Polygons) { polygon.LuaTriggerCode="";polygon.LuaTriggerEnabled=false;polygon.LuaTriggerOnce=false; }
            if(!level.Chunks.TryGetValue(Tag,out byte[] bytes))return;
            using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReaderBE(stream);
            var entries=new Dictionary<short,(byte Flags,string Code)>();
            if(stream.Length<4 || reader.ReadUInt16()!=1)throw new InvalidDataException("Unknown or truncated polygon Lua data.");
            int count=reader.ReadUInt16();
            for(int i=0;i<count;i++) {
                if(stream.Length-stream.Position<7)throw new InvalidDataException("Truncated polygon Lua entry.");
                short index=reader.ReadInt16();byte flags=reader.ReadByte();uint length=reader.ReadUInt32();
                if(index<0 || index>=level.Polygons.Count || entries.ContainsKey(index) || (flags&~3)!=0 || length>1024*1024 || length>stream.Length-stream.Position)
                    throw new InvalidDataException("Invalid polygon Lua entry.");
                string code=Utf8.GetString(reader.ReadBytes((int)length));Validate(code);entries.Add(index,(flags,code));
            }
            if(stream.Position!=stream.Length)throw new InvalidDataException("Unexpected polygon Lua data.");
            foreach(var pair in entries) {
                var p=level.Polygons[pair.Key];p.LuaTriggerCode=PolygonLuaActions.UpgradeSpawnPresets(pair.Value.Code);p.LuaTriggerEnabled=(pair.Value.Flags&1)!=0;p.LuaTriggerOnce=(pair.Value.Flags&2)!=0;
            }
        }
        public static void Save(Level level)
        {
            var bindings=level.Polygons.Select((p,i)=>(Polygon:p,Index:i)).Where(p=>!string.IsNullOrEmpty(p.Polygon.LuaTriggerCode)).ToList();
            if(bindings.Count==0 && !level.Chunks.ContainsKey(Tag))return;
            level.Chunks.TryGetValue(EmbeddedLua.Tag,out byte[] lua);
            var scripts=EmbeddedLua.Read(lua);scripts.RemoveAll(IsGenerated);
            if(bindings.Any(p=>p.Polygon.LuaTriggerEnabled) && scripts.Any(p=>p.Name==ScriptName))
                throw new InvalidDataException("An existing Lua script uses the reserved name '"+ScriptName+"'. Rename that script first.");
            byte[] metadata=null;
            if(bindings.Count>0) {
                using var stream=new MemoryStream();using var writer=new BinaryWriterBE(stream);writer.Write((ushort)1);writer.Write((ushort)bindings.Count);
                foreach(var binding in bindings) {
                    var p=binding.Polygon;Validate(p.LuaTriggerCode);byte[] code=Utf8.GetBytes(PolygonLuaActions.UpgradeSpawnPresets(p.LuaTriggerCode));
                    writer.Write((short)binding.Index);writer.Write((byte)((p.LuaTriggerEnabled?1:0)|(p.LuaTriggerOnce?2:0)));writer.Write((uint)code.Length);writer.Write(code);
                }
                metadata=stream.ToArray();
            }
            if(bindings.Any(p=>p.Polygon.LuaTriggerEnabled))scripts.Add(new EmbeddedLuaScript{Name=ScriptName,Text=Generate(level)});
            byte[] compiled=scripts.Count==0?null:EmbeddedLua.Write(scripts);
            if(metadata==null)level.Chunks.Remove(Tag);else level.Chunks[Tag]=metadata;
            if(compiled==null)level.Chunks.Remove(EmbeddedLua.Tag);else level.Chunks[EmbeddedLua.Tag]=compiled;
        }
        static readonly int[] StockMonsterCollections={6,3,3,3,16,16,16,16,9,9,9,9,12,12,12,12,15,15,15,15,15,31,31,31,31,14,14,5,5,8,8,31,5,11,11,11,10,10,2,2,9,12,11,13,13,13,13};
        public static int SpawnCollection(Level level,int type)
        {
            if(type<1 || type>=StockMonsterCollections.Length)return -1;
            // Aleph One's packed monster_definition is 156 bytes. Its first
            // big-endian short contains a 5-bit collection and CLUT above it.
            if(level.Chunks.TryGetValue(Wadfile.Chunk("MNpx"),out byte[] physics) &&
                physics.Length%156==0 && physics.Length>=(type+1)*156)
                return ((physics[type*156]<<8)|physics[type*156+1])&31;
            return StockMonsterCollections[type];
        }
        public static string Generate(Level level)
        {
            var source=new StringBuilder(Header+"-- Edit using Select > Polygon Lua trigger.\n");
            source.Append(PolygonLuaActions.SunPathDeclaration(level.Polygons.Where(p=>p.LuaTriggerEnabled).Select(p=>p.LuaTriggerCode)));
            var collections=new SortedSet<int>();
            foreach(var p in level.Polygons.Where(p=>p.LuaTriggerEnabled))
                foreach(Match match in Regex.Matches(p.LuaTriggerCode,@"^-- Forge Ahead preload collection:(\d+)\s*$",RegexOptions.Multiline)) {
                    if(int.TryParse(match.Groups[1].Value,out int collection) && collection>=0 && collection<32)collections.Add(collection);
                }
            // Collection userdata is unavailable until graphics load, so resolve
            // embedded MNpx physics here; fall back to Marathon 2/Infinity defaults.
            // Explicit preload comments remain available for external/custom physics.
            foreach(var p in level.Polygons.Where(p=>p.LuaTriggerEnabled))
                foreach(Match match in Regex.Matches(p.LuaTriggerCode,@"(?m)^    local monster = Monsters\.new\((?:target\.x, target\.y|x, y), (?:0|target\.floor\.height), target, (\d+)\)\r?$"))
                    if(int.TryParse(match.Groups[1].Value,out int type)) {
                        int collection=SpawnCollection(level,type);
                        if(collection>=0)collections.Add(collection);
                    }
            foreach(var p in level.Polygons.Where(p=>p.LuaTriggerEnabled))
                foreach(Match match in Regex.Matches(p.LuaTriggerCode,@"(?m)^-- Forge Ahead preload monster:(\d+)\s*$"))
                    if(int.TryParse(match.Groups[1].Value,out int type)) {
                        int collection=SpawnCollection(level,type);if(collection>=0)collections.Add(collection);
                    }
            if(collections.Count>0) {
                source.Append("CollectionsUsed = CollectionsUsed or {}\n");
                foreach(int collection in collections)source.Append("table.insert(CollectionsUsed, ").Append(collection).Append(")\n");
            }
            source.Append("local actions, once = {}, {}\n");
            for(int i=0;i<level.Polygons.Count;i++) {
                var p=level.Polygons[i];if(!p.LuaTriggerEnabled || string.IsNullOrWhiteSpace(p.LuaTriggerCode))continue;
                source.Append("actions[").Append(i).Append("] = function(player, polygon)\n").Append(PolygonLuaActions.UpgradeSpawnPresets(p.LuaTriggerCode)).Append("\nend\n");
                if(p.LuaTriggerOnce)source.Append("once[").Append(i).Append("] = true\n");
            }
            source.Append(@"Triggers = Triggers or {}
local previous_init, previous_postidle = Triggers.init, Triggers.postidle
local last_polygon = {}
local function apply_sun(state)
  local ok, sun = pcall(function() return Level.sun end)
  if ok and sun and state and state.current then
    for key, value in pairs(state.current) do sun[key] = value end
  end
end
function Triggers.init(restoring)
  if restoring then Game.restore_saved() end
  if previous_init then previous_init(restoring) end
  if not restoring or not Game._forge_ahead_polygon_fired then
    Game._forge_ahead_polygon_fired = {}
    Game._forge_ahead_fog = nil
    Game._forge_ahead_sun = nil
    Game._forge_ahead_spawners = nil
    Game._forge_ahead_delays = nil
    Game._forge_ahead_waves = nil
    Game._forge_ahead_flags = nil
    Game._forge_ahead_move_stops = nil
    Game._forge_ahead_move_orders = nil
  end
  apply_sun(Game._forge_ahead_sun)
  last_polygon = {}
  if restoring then
    for player in Players() do
      if player.polygon then last_polygon[player.index] = player.polygon.index end
    end
  end
end
local previous_killed = Triggers.monster_killed
function Triggers.monster_killed(monster, ...)
  if Game._forge_ahead_waves then
    for _, wave in pairs(Game._forge_ahead_waves) do wave.alive[monster.index] = nil end
  end
  if Game._forge_ahead_move_stops then Game._forge_ahead_move_stops[monster.index] = nil end
  if Game._forge_ahead_move_orders then Game._forge_ahead_move_orders[monster.index] = nil end
  if previous_killed then previous_killed(monster, ...) end
end
function Triggers.postidle()
  if previous_postidle then previous_postidle() end
  for index, destination in pairs(Game._forge_ahead_move_stops or {}) do
    local monster = Monsters[index]
    if not monster or monster.player or monster.vitality <= 0 then
      Game._forge_ahead_move_stops[index] = nil
    elseif monster.polygon and monster.polygon.index == destination then
      -- Stop without triggering the monster's teleport-out behavior.
      local teleports = monster.teleports_out
      monster.teleports_out = false
      monster.active = false
      monster.teleports_out = teleports
      -- Knockback can prevent deactivation; retry until it succeeds.
      if not monster.active then Game._forge_ahead_move_stops[index] = nil end
    end
  end
  -- Retry orders individually: activation/combat can replace a one-shot path.
  -- Sort IDs for deterministic multiplayer behavior and limit path work per tick.
  local orders = Game._forge_ahead_move_orders or {}
  local ids = {}
  for index in pairs(orders) do table.insert(ids, index) end
  table.sort(ids)
  local issued = false
  for _, index in ipairs(ids) do
    local order, monster = orders[index], Monsters[index]
    local destination = Polygons[order.destination]
    order.remaining = order.remaining - 1
    if not monster or monster.player or not destination or order.remaining <= 0 or
       (monster.vitality <= 0 and (monster.active or monster.vitality ~= -1)) or
       monster.action == 'dying hard' or monster.action == 'dying soft' or monster.action == 'dying flaming' then
      orders[index] = nil
      if Game._forge_ahead_move_stops then Game._forge_ahead_move_stops[index] = nil end
    elseif monster.polygon == destination then
      orders[index] = nil
    else
      order.wait = order.wait - 1
      if order.wait <= 0 and not issued then
        monster:move_by_path(destination)
        order.wait = monster.action == 'moving' and 90 or 30
        issued = true
      end
    end
  end
  -- Snapshot before entry actions: a newly queued delay starts counting next tick.
  local delays = Game._forge_ahead_delays or {}
  Game._forge_ahead_delays = {}
  for _, job in ipairs(delays) do
    job.ticks = job.ticks - 1
    if job.ticks > 0 then table.insert(Game._forge_ahead_delays, job)
    else
      local player, polygon = Players[job.player], Polygons[job.polygon]
      if player and not player.dead and polygon then
        local compile, error = load(""return function(player, polygon)\n"" .. job.code .. ""\nend"")
        if compile then
          local ok, reason = pcall(compile(), player, polygon)
          if not ok then player:print(""Delayed action: "" .. tostring(reason)) end
        else player:print(""Delayed action: "" .. tostring(error)) end
      end
    end
  end
  local fired = Game._forge_ahead_polygon_fired or {}
  Game._forge_ahead_polygon_fired = fired
  for player in Players() do
    local polygon = player.polygon
    if not player.dead and polygon then
      local index = polygon.index
      if last_polygon[player.index] ~= index then
        last_polygon[player.index] = index
        if actions[index] and (not once[index] or not fired[index]) then
          if once[index] then fired[index] = true end
          actions[index](player, polygon)
        end
      end
      local heal = player._forge_ahead_heal
      if heal then
        heal.credit = (heal.credit or 0) + heal.rate
        local gain = math.min(math.floor(heal.credit / 30), heal.remaining, math.max(0, heal.cap - player.life))
        heal.credit = heal.credit - gain * 30
        if gain > 0 then player.life = player.life + gain; heal.remaining = heal.remaining - gain end
        if heal.remaining <= 0 or player.life >= heal.cap then player._forge_ahead_heal = nil end
      end
    else
      player._forge_ahead_heal = nil
      last_polygon[player.index] = nil
    end
  end
  if Game._forge_ahead_waves then
    for index = 0, #Polygons - 1 do
      local wave = Game._forge_ahead_waves[index]
      if wave and not wave.finished then
        for id in pairs(wave.alive) do
          local monster = Monsters[id]
          if not monster or monster.vitality <= 0 then wave.alive[id] = nil end
        end
        if wave.pending > 0 then
          wave.wait = wave.wait - 1
          if wave.wait <= 0 then
            local target = Polygons[index]
            local x, y, count = 0, 0, 0
            for vertex in target.endpoints() do x,y,count=x+vertex.x,y+vertex.y,count+1 end
            if count >= 3 and MonsterTypes[wave.monster] then
              while wave.pending > 0 do
                local monster = Monsters.new(x/count,y/count,0,target,wave.monster)
                if not monster then break end
                wave.alive[monster.index] = true
                wave.pending = wave.pending - 1
                monster.active = true
              end
              wave.wait = 30 -- retry exhausted engine slots once per second
            else wave.finished = true end
          end
        elseif next(wave.alive) == nil then
          wave.left = wave.left - 1
          if wave.left <= 0 then wave.finished = true
          else wave.pending=wave.size;wave.wait=wave.pause end
        end
      end
    end
  end
  -- Iterate polygon order so simultaneous spawns are deterministic in multiplayer.
  if Game._forge_ahead_spawners then
    for index = 0, #Polygons - 1 do
      local spawner = Game._forge_ahead_spawners[index]
      if spawner and not spawner.finished then
        spawner.wait = spawner.wait - 1
        if spawner.wait <= 0 then
          spawner.wait = spawner.interval
          local target = Polygons[index]
          local x, y, count = 0, 0, 0
          for vertex in target.endpoints() do
            x, y, count = x + vertex.x, y + vertex.y, count + 1
          end
          if count >= 3 and MonsterTypes[spawner.monster] then
            local monster = Monsters.new(x/count, y/count, 0, target, spawner.monster)
            if monster then
              monster.active = true
              if spawner.remaining > 0 then
                spawner.remaining = spawner.remaining - 1
                if spawner.remaining == 0 then spawner.finished = true end
              end
            end
          else spawner.finished = true end
        end
      end
    end
  end
  local sun_fade = Game._forge_ahead_sun
  if sun_fade and sun_fade.target then
    sun_fade.elapsed = math.min(sun_fade.ticks, sun_fade.elapsed + 1)
    local t = sun_fade.elapsed / sun_fade.ticks
    t = t * t * (3 - 2 * t)
    local current = {enabled=true}
    for key, finish in pairs(sun_fade.target) do
      if key ~= ""enabled"" then
        local start = sun_fade.from[key]
        local delta = finish - start
        current[key] = start + delta * t
        if sun_fade.elapsed == sun_fade.ticks then current[key] = finish end
      end
    end
    sun_fade.current = current
    apply_sun(sun_fade)
    if sun_fade.elapsed == sun_fade.ticks then sun_fade.target = nil; sun_fade.from = nil end
  end
  -- Advance once per game tick, independently of player count.
  for _, key in ipairs({""fog"", ""underwater_fog""}) do
    local fade = Game._forge_ahead_fog and Game._forge_ahead_fog[key]
    if fade then
      fade.elapsed = math.min(fade.ticks, fade.elapsed + 1)
      local t = fade.elapsed / fade.ticks
      t = t * t * (3 - 2 * t)
      local fog = Level[key]
      fog.active = true
      for _, channel in ipairs({""r"", ""g"", ""b""}) do
        fog.color[channel] = fade.from[channel] + (fade.to[channel] - fade.from[channel]) * t
      end
      -- Fog strength is proportional to inverse depth. Interpolating depth
      -- directly leaves distant fog almost unchanged until a sudden final rush.
      local from_density = 1 / math.max(0.1, fade.from.depth)
      local to_density = 1 / math.max(0.1, fade.to.depth)
      fog.depth = 1 / (from_density + (to_density - from_density) * t)
      if fade.elapsed == fade.ticks then fog.depth = fade.to.depth end
      if fade.elapsed == fade.ticks then Game._forge_ahead_fog[key] = nil end
    end
  end
end
");return source.ToString();
        }
    }
}

namespace Weland
{
    public sealed class CameraWaypoint {
        public short X,Y,Z; public short Yaw,Pitch; public int Ticks=90;
        public string Title=""; public int TitleTicks=90;
        public int TitleX=50,TitleY=80,TitleSize=100;
        public int TitleRed=255,TitleGreen=255,TitleBlue=255;
        public int TitleFadeIn=0,TitleFadeOut=0;
    }
    public sealed class CameraPath {
        public string Name="Camera path";
        public List<CameraWaypoint> Points=new List<CameraWaypoint>();
    }
    public static class CameraPaths {
        public static readonly uint Tag=Wadfile.Chunk("FCAM");
        const string Name="Forge Ahead Camera Paths";
        const string Header="-- Forge Ahead camera paths v1\n";
        public static List<CameraPath> Read(Level level) {
            var paths=new List<CameraPath>();
            if(!level.Chunks.TryGetValue(Tag,out var bytes))return paths;
            using var stream=new MemoryStream(bytes,false);using var r=new BinaryReader(stream,Encoding.UTF8);
            int version=r.ReadInt32();if(version<1||version>3)throw new InvalidDataException("Unknown camera path format.");
            int count=r.ReadInt32();if(count<0||count>256)throw new InvalidDataException("Invalid camera path count.");
            for(int i=0;i<count;i++) {
                var path=new CameraPath{Name=r.ReadString()};int n=r.ReadInt32();
                if(n<0||n>4096)throw new InvalidDataException("Invalid camera waypoint count.");
                for(int j=0;j<n;j++) {
                    var v=new CameraWaypoint{X=r.ReadInt16(),Y=r.ReadInt16(),Z=r.ReadInt16(),Yaw=r.ReadInt16(),Pitch=r.ReadInt16(),Ticks=r.ReadInt32()};
                    if(version>=2){v.Title=r.ReadString();v.TitleTicks=r.ReadInt32();}
                    if(version>=3){v.TitleX=r.ReadInt32();v.TitleY=r.ReadInt32();v.TitleSize=r.ReadInt32();v.TitleRed=r.ReadInt32();v.TitleGreen=r.ReadInt32();v.TitleBlue=r.ReadInt32();v.TitleFadeIn=r.ReadInt32();v.TitleFadeOut=r.ReadInt32();}
                    path.Points.Add(v);
                }
                paths.Add(path);
            }
            if(stream.Position!=stream.Length)throw new InvalidDataException("Invalid camera path data.");
            Validate(paths);return paths;
        }
        static void Validate(List<CameraPath> paths) {
            if(paths.Count>256 || paths.Select(p=>p.Name).Distinct().Count()!=paths.Count)throw new ArgumentException("Camera path names must be unique.");
            foreach(var p in paths) {
                if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80||p.Name.Any(char.IsControl)||p.Points.Count>4096)throw new ArgumentException("Invalid camera path name or size.");
                foreach(var w in p.Points) {
                    if(w.Ticks<1||w.Ticks>108000||w.Yaw<0||w.Yaw>359||w.Pitch< -90||w.Pitch>90)throw new ArgumentException("Invalid camera waypoint timing or angle.");
                    if(w.TitleX<0||w.TitleX>100||w.TitleY<0||w.TitleY>100||w.TitleSize<50||w.TitleSize>400||w.TitleRed<0||w.TitleRed>255||w.TitleGreen<0||w.TitleGreen>255||w.TitleBlue<0||w.TitleBlue>255||w.TitleFadeIn<0||w.TitleFadeIn>3600||w.TitleFadeOut<0||w.TitleFadeOut>3600)throw new ArgumentException("Invalid camera caption style.");
                    if(w.Title==null || Encoding.UTF8.GetByteCount(w.Title)>240 || w.Title.Any(char.IsControl) || w.TitleTicks<1 || w.TitleTicks>3600)throw new ArgumentException("Waypoint captions must be a single line of at most 240 UTF-8 bytes, with a duration of 1 tick to 120 seconds.");
                }
            }
        }
        public static void Write(Level level,List<CameraPath> paths) {
            Validate(paths);
            using var stream=new MemoryStream();using var w=new BinaryWriter(stream,Encoding.UTF8);
            w.Write(3);w.Write(paths.Count);
            foreach(var p in paths){w.Write(p.Name);w.Write(p.Points.Count);foreach(var v in p.Points){w.Write(v.X);w.Write(v.Y);w.Write(v.Z);w.Write(v.Yaw);w.Write(v.Pitch);w.Write(v.Ticks);w.Write(v.Title);w.Write(v.TitleTicks);w.Write(v.TitleX);w.Write(v.TitleY);w.Write(v.TitleSize);w.Write(v.TitleRed);w.Write(v.TitleGreen);w.Write(v.TitleBlue);w.Write(v.TitleFadeIn);w.Write(v.TitleFadeOut);}}
            level.Chunks[Tag]=stream.ToArray();
        }
        static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        static string N(short n)=>(n/1024.0).ToString("0.########",System.Globalization.CultureInfo.InvariantCulture);
        public static string Warning(Level level,CameraPath path) {
            if(path.Points.Count<2)return "Add at least two waypoints.";
            for(int i=0;i<path.Points.Count;i++) {
                var a=path.Points[i];var b=path.Points[Math.Min(i+1,path.Points.Count-1)];
                int steps=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y))/32.0));
                for(int j=0;j<=steps;j++) {
                    double t=j/(double)steps;short x=(short)Math.Round(a.X+(b.X-a.X)*t),y=(short)Math.Round(a.Y+(b.Y-a.Y)*t);double z=a.Z+(b.Z-a.Z)*t;
                    short poly=level.GetEnclosingPolygon(new Point(x,y));
                    if(poly<0||z<=level.Polygons[poly].FloorHeight||z>=level.Polygons[poly].CeilingHeight)return "Waypoint or segment "+(i+1)+" leaves the open map volume. Adjust the path before playback.";
                }
            }
            return "Check playback in game; narrow walls and overlapping rooms require manual review.";
        }
        public static string Generate(Level level,List<CameraPath> paths) {
            var s=new StringBuilder(Header+@"local paths = {}
local active, remaining, reusable, playback_player
local caption, caption_age, caption_player, caption_native, caption_slot
local timeline, elapsed, point_index, next_point
local function clear_title()
  if caption_player then
    if caption_native then caption_player:caption(nil)
    elseif caption_slot then caption_player.overlays[caption_slot].text = nil end
  end
  caption, caption_age, caption_player, caption_native, caption_slot = nil, nil, nil, nil, nil
end
local function render_title()
  local alpha = 1
  if caption[16] > 0 then alpha = math.min(alpha, caption_age / caption[16]) end
  if caption[17] > 0 then alpha = math.min(alpha, (caption[9] - caption_age) / caption[17]) end
  alpha = math.max(0, math.min(1, alpha))
  alpha = alpha * alpha * (3 - 2 * alpha)
  if caption_native then
    caption_player:caption(caption[8], caption[10]/100, caption[11]/100, caption[12]/100,
      caption[13]/255, caption[14]/255, caption[15]/255, alpha)
  end
end
local function show_title(player, point)
  if point[8] ~= '' then
    clear_title()
    caption, caption_age, caption_player = point, 0, player
    local ok, method = pcall(function() return player.caption end)
    caption_native = ok and method ~= nil
    if caption_native then render_title()
    else
      -- Compatibility fallback for engines without styled captions.
      local slot = #player.overlays - 1
      if slot >= 0 then
        caption_slot = slot
        player.overlays[slot].text = point[8]
      end
    end
  end
end
function forge_ahead_camera_stop()
  if active then active:deactivate() end
  active, remaining, timeline, playback_player = nil, nil, nil, nil
  clear_title()
end
function forge_ahead_camera_play(name, player)
  if not player.local_ then return end
  local data = paths[name]
  if not data or #data < 2 then player:print('Camera path missing or needs two valid waypoints'); return end
  forge_ahead_camera_stop()
  local camera = reusable or Cameras.new()
  if not camera then player:print('Cannot create camera'); return end
  reusable = camera
  camera:clear()
  remaining = 0
  for _, p in ipairs(data) do
    camera.path_points:new(p[1],p[2],p[3],p[4],p[7])
    camera.path_angles:new(p[5],p[6],p[7])
    remaining = remaining + p[7]
  end
  active = camera
  timeline, elapsed, point_index, next_point = data, 0, 1, data[1][7]
  playback_player = player
  show_title(player, data[1])
  camera:activate(player)
end
Triggers = Triggers or {}
local previous = Triggers.postidle
function Triggers.postidle()
  if previous then previous() end
  if remaining then
    if caption then
      caption_age = caption_age + 1
      if caption_age >= caption[9] then clear_title() else render_title() end
    end
    elapsed = elapsed + 1
    while timeline and point_index < #timeline and elapsed >= next_point do
      point_index = point_index + 1
      show_title(playback_player, timeline[point_index])
      next_point = next_point + timeline[point_index][7]
    end
    remaining = remaining - 1
    if remaining <= 0 then forge_ahead_camera_stop() end
  end
end
local previous_init = Triggers.init
function Triggers.init(restoring)
  if previous_init then previous_init(restoring) end
  forge_ahead_camera_stop()
end
");
            foreach(var path in paths) {
                var rows=new List<string>();bool valid=path.Points.Count>=2;
                foreach(var v in path.Points) {
                    short polygon=level.GetEnclosingPolygon(new Point(v.X,v.Y));
                    if(polygon<0 || v.Z<=level.Polygons[polygon].FloorHeight || v.Z>=level.Polygons[polygon].CeilingHeight){valid=false;break;}
                    rows.Add("{"+N(v.X)+","+N(v.Y)+","+N(v.Z)+","+polygon+","+v.Yaw+","+v.Pitch+","+v.Ticks+","+Q(v.Title)+","+v.TitleTicks+","+v.TitleX+","+v.TitleY+","+v.TitleSize+","+v.TitleRed+","+v.TitleGreen+","+v.TitleBlue+","+v.TitleFadeIn+","+v.TitleFadeOut+"}");
                }
                if(valid)s.Append("paths[").Append(Q(path.Name)).Append("] = {").Append(string.Join(",",rows)).Append("}\n");
            }
            return s.ToString();
        }
        public static void Save(Level level) {
            if(!level.Chunks.ContainsKey(Tag))return;
            var paths=Read(level);level.Chunks.TryGetValue(EmbeddedLua.Tag,out var data);
            var scripts=EmbeddedLua.Read(data);
            if(scripts.Any(s=>s.Name==Name&&!s.Text.StartsWith(Header)))throw new InvalidDataException("Reserved camera script name is in use.");
            scripts.RemoveAll(s=>s.Name==Name);
            if(paths.Count>0)scripts.Add(new EmbeddedLuaScript{Name=Name,Text=Generate(level,paths)});
            if(scripts.Count==0)level.Chunks.Remove(EmbeddedLua.Tag);else level.Chunks[EmbeddedLua.Tag]=EmbeddedLua.Write(scripts);
        }
    }
}
