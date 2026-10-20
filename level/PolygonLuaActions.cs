using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Weland
{
    public sealed class LuaActionParameter
    {
        public string Id,Label;
        public double Default,Minimum,Maximum;
        public int Digits;
        public LuaActionParameter(string id,string label,double value,double min,double max,int digits=0) { Id=id;Label=label;Default=value;Minimum=min;Maximum=max;Digits=digits; }
    }
    public sealed class PolygonLuaAction
    {
        public string Id,Category,Label,Description;
        public LuaActionParameter[] Parameters;
        public bool HasText;
        public string TextLabel="Message",TextDefault="Trigger activated";
    }
    public static class PolygonLuaActions
    {
        static LuaActionParameter Amount()=>new LuaActionParameter("amount","Health points",50,1,450);
        static LuaActionParameter Cap()=>new LuaActionParameter("cap","Health cap (150 = one bar)",150,1,450);
        static LuaActionParameter Polygon()=>new LuaActionParameter("polygon","Polygon ID (-1 = triggering polygon)",-1,-1,32766);
        static LuaActionParameter FogTarget()=>new LuaActionParameter("underwater","Fog layer (0 = normal, 1 = underwater)",0,0,1);
        static LuaActionParameter FogDepth()=>new LuaActionParameter("depth","Depth (world units)",35,0.1,1024,1);
        static LuaActionParameter Red()=>new LuaActionParameter("red","Red (0–1)",0.5,0,1,3);
        static LuaActionParameter Green()=>new LuaActionParameter("green","Green (0–1)",0.5,0,1,3);
        static LuaActionParameter Blue()=>new LuaActionParameter("blue","Blue (0–1)",0.5,0,1,3);
        static LuaActionParameter Sound()=>new LuaActionParameter("sound","Sound ID",0,0,32766);
        static PolygonLuaAction Action(string id,string category,string label,string description,params LuaActionParameter[] parameters)=>new PolygonLuaAction{Id=id,Category=category,Label=label,Description=description,Parameters=parameters};
        static LuaActionParameter SunAzimuth()=>new LuaActionParameter("azimuth","Azimuth (degrees; 0 = north)",250,0,360,1);
        static LuaActionParameter SunDuration()=>new LuaActionParameter("seconds","Duration (seconds)",300,0.1,36000,1);
        static LuaActionParameter[] SunSettings(bool fade)=>new[]{
            new LuaActionParameter("path_position","Position on sun path (0–1)",1,0,1,3),
            new LuaActionParameter("size","Sun diameter (degrees)",1,.05,20,2),
            new LuaActionParameter("red","Red",1,0,1,3),new LuaActionParameter("green","Green",.91,0,1,3),new LuaActionParameter("blue","Blue",.72,0,1,3),
            new LuaActionParameter("brightness","Brightness",1,0,4,2),new LuaActionParameter("shaft_strength","Shaft strength",1,0,4,2),
            new LuaActionParameter("glare_strength","Glare strength",1,0,4,2),new LuaActionParameter("bloom_strength","Bloom strength",1,0,4,2)
        }.Concat(fade?new[]{SunDuration()}:new LuaActionParameter[0]).ToArray();
        static LuaActionParameter[] SunCycle(bool sunset)=>new[]{SunDuration(),
            new LuaActionParameter("start","Starting path position",sunset?1:0,0,1,3),
            new LuaActionParameter("end","Ending path position",sunset?0:1,0,1,3),
            new LuaActionParameter("start_brightness","Starting brightness",sunset?1:.2,0,4,2),
            new LuaActionParameter("end_brightness","Ending brightness",sunset?0:1,0,4,2)};
        static readonly string[] SunFields={"path_position","size","red","green","blue","brightness","shaft_strength","glare_strength","bloom_strength"};
        static string SunCode(string id,Func<string,string> number) {
            if(id=="sun_path")return "-- Forge Ahead sun path: "+string.Join(";",Enumerable.Range(0,8).Select(i=>number("az"+i)+","+number("el"+i)))+"\n-- Declared automatically at level-script load; no action runs here.\n";
            string code="local ok, sun = pcall(function() local s = Level.sun; local p = s.path_position; return s end)\nif not ok or not sun then\n  player:print('Sun actions require Sprintathon with Level.sun support')\nelse\n";
            if(id=="sun_reset")return code+"Game._forge_ahead_sun = nil\n"+string.Join("",SunFields.Where(f=>f!="path_position").Concat(new[]{"enabled"}).Select(f=>"sun."+f+" = nil\n"))+"end\n";
            code+="local current = {enabled=sun.enabled}\n"+string.Join("",SunFields.Select(f=>"current."+f+" = sun."+f+"\n"));
            if(id=="sun_stop" || id=="sun_on" || id=="sun_off") {
                if(id!="sun_stop")code+="current.enabled = "+(id=="sun_on"?"true":"false")+"\nsun.enabled = current.enabled\n";
                return code+"Game._forge_ahead_sun = {current=current}\nend\n";
            }
            code="-- Forge Ahead sun path required\n"+code;
            code+="current.enabled = true\nsun.enabled = true\n";
            bool cycle=id=="sunrise" || id=="sunset";
            if(cycle) {
                bool sunset=id=="sunset";
                code+="current.path_position = "+number("start")+"\ncurrent.brightness = "+number("start_brightness")+"\ncurrent.red = 1\ncurrent.green = "+(sunset?"0.91":"0.35")+"\ncurrent.blue = "+(sunset?"0.72":"0.15")+"\n";
                code+="local target = {}\nfor key, value in pairs(current) do target[key] = value end\ntarget.path_position = "+number("end")+"\ntarget.brightness = "+number("end_brightness")+"\ntarget.green = "+(sunset?"0.35":"0.91")+"\ntarget.blue = "+(sunset?"0.15":"0.72")+"\n";
            } else code+="local target = {enabled=true, "+string.Join(", ",SunFields.Select(f=>f+"="+number(f)))+"}\n";
            if(id=="sun_set")code+="current = target\nGame._forge_ahead_sun = {current=current}\n";
            else code+="Game._forge_ahead_sun = {current=current, from=current, target=target, elapsed=0, ticks=math.max(1,math.floor("+number("seconds")+" * 30 + 0.5))}\n";
            return code+"for key, value in pairs(current) do sun[key] = value end\nend\n";
        }
        public static string SunPathDeclaration(IEnumerable<string> codes) {
            var scripts=codes.ToArray();string selected=null;
            foreach(string code in scripts)foreach(Match match in Regex.Matches(code,@"^-- Forge Ahead sun path: ([^\r\n]+)$",RegexOptions.Multiline)) {
                var steps=match.Groups[1].Value.Split(';');
                if(steps.Length!=8)throw new ArgumentException("Sun path must contain exactly eight steps.");
                var values=new List<string>();
                foreach(string step in steps) {
                    var pair=step.Split(',');double az,el;
                    if(pair.Length!=2 || !double.TryParse(pair[0],NumberStyles.Float,CultureInfo.InvariantCulture,out az) || !double.TryParse(pair[1],NumberStyles.Float,CultureInfo.InvariantCulture,out el) || double.IsNaN(az) || double.IsNaN(el) || double.IsInfinity(az) || double.IsInfinity(el) || az<0 || az>360 || el< -90 || el>90)
                        throw new ArgumentException("Invalid sun path angle.");
                    values.Add("{azimuth="+az.ToString("0.########",CultureInfo.InvariantCulture)+", elevation="+el.ToString("0.########",CultureInfo.InvariantCulture)+"}");
                }
                string path="{\n  "+string.Join(",\n  ",values)+"\n}";
                if(selected!=null && selected!=path)throw new ArgumentException("A level supports one sun path. Use the same eight steps in every path declaration.");
                selected=path;
            }
            bool explicitPath=selected!=null;
            if(!explicitPath && !scripts.Any(c=>c.Contains("-- Forge Ahead sun path required")))return "";
            if(!explicitPath)selected="{\n  "+string.Join(",\n  ",Enumerable.Range(0,8).Select(i=>"{azimuth=250, elevation="+(-3+38.0*i/7).ToString("0.########",CultureInfo.InvariantCulture)+"}"))+"\n}";
            return "-- Eight-step sun path: declared before lighting loads.\ndo\n  local ok, sun = pcall(function() local s = Level.sun; local path = s.path; return s end)\n  if ok and sun then\n"+
                (explicitPath?"":"    if sun.path == nil then\n")+"    sun.path = "+selected+"\n"+(explicitPath?"":"    end\n")+"  end\nend\n";
        }
        public static readonly PolygonLuaAction[] Catalog={
            new PolygonLuaAction{Id="camera_play",Category="Camera",Label="Play camera path",Description="Enter the exact camera path name. Returns to the player after the final hold. Rename references when renaming a path.",HasText=true,Parameters=Array.Empty<LuaActionParameter>()},
            Action("camera_stop","Camera","Stop camera path","Return the view to the player."),
            Action("sun_path","Sun","Declare eight-step sun path","One path per level, declared before lighting loads. Insert on an enabled polygon trigger; the editor moves the declaration to level-script top level.",Enumerable.Range(0,8).SelectMany(i=>new[]{new LuaActionParameter("az"+i,"Step "+(i+1)+" azimuth",250,0,360,2),new LuaActionParameter("el"+i,"Step "+(i+1)+" elevation",-3+38.0*i/7,-90,90,3)}).ToArray()),
            Action("sun_set","Sun","Set sun instantly","Sprintathon only. Enables the visible sun with these settings; respects player graphics toggles.",SunSettings(false)),
            Action("sun_fade","Sun","Transition sun smoothly","Moves from the current sun settings to these values. Moves along the declared eight-step path and replaces the running transition.",SunSettings(true)),
            Action("sunrise","Sun","Smooth sunrise","Starts at the chosen path position with warm dawn colors, then rises to daylight. Runs after leaving the polygon.",SunCycle(false)),
            Action("sunset","Sun","Smooth sunset","Runs backward along the declared path, warms toward sunset and fades to the ending brightness.",SunCycle(true)),
            Action("sun_on","Sun","Enable sun","Enables the sun and stops its current transition. Respects player graphics toggles."),
            Action("sun_off","Sun","Disable sun","Hides the visible sun and stops its transition; independent landscape shafts retain their settings."),
            Action("sun_stop","Sun","Stop sun transition","Freezes the current sun settings."),
            Action("sun_reset","Sun","Reset sun to preferences","Stops the transition and clears appearance overrides; the precomputed path remains declared."),
            Action("ammo","Ammo & weapons","Refill all ammo","Fill available ammunition to carrying limits."),
            Action("weapons","Ammo & weapons","All weapons and ammo","Includes dual pistols and shotguns; leaves fists intact."),
            Action("ammo_item","Ammo & weapons","Give an ammo item","Stock ammo IDs: 2, 4, 6, 7, 9, 14, 16, 23, 35.",new LuaActionParameter("item","Ammo item ID",2,0,35),new LuaActionParameter("count","Magazines / packs to add",5,1,999)),
            Action("heal","Health & oxygen","Heal instantly","Adds health up to the cap; never lowers existing health.",Amount(),Cap()),
            Action("full_health","Health & oxygen","Refill health","Raises health to the chosen cap.",Cap()),
            Action("slow_heal","Health & oxygen","Heal gradually","Continues after leaving. Re-entering restarts the effect; death stops it.",Amount(),new LuaActionParameter("rate","Health points per second",10,1,450),Cap()),
            Action("oxygen","Health & oxygen","Refill oxygen","Restores oxygen to the engine maximum."),
            Action("sound_player","Sounds","Play sound to entering player","Only the player who enters hears this sound.",Sound(),new LuaActionParameter("pitch","Pitch multiplier",1,0.1,4,2)),
            Action("sound_polygon","Sounds","Play sound from polygon","Positional sound audible to nearby players.",Sound(),Polygon(),new LuaActionParameter("pitch","Pitch multiplier",1,0.1,4,2)),
            Action("monsters_move","Monsters","Move monsters between polygons","Orders living non-player monsters currently in the source polygon to walk to the destination. Includes friendly monsters. Requires a traversable route; combat AI may override the order.",new LuaActionParameter("source_polygon","Source polygon (-1 = triggering polygon)",-1,-1,32766),new LuaActionParameter("destination_polygon","Destination polygon",0,0,32766),new LuaActionParameter("stop_destination","Stop at destination",0,0,1)),
            Action("spawn","Monsters","Spawn monsters","Spawns active monsters on the floor, at the average of the polygon vertices. Preloads embedded physics or standard Marathon graphics. For external/custom physics, specify the matching extra collection ID.",new LuaActionParameter("monster","Monster type ID (8 = minor fighter)",8,1,32766),Polygon(),new LuaActionParameter("count","Number of monsters",1,1,16),new LuaActionParameter("collection","Extra collection ID (-1 = none)",-1,-1,31)),
            Action("delay","Timing","Run an action after a delay","Select script code to wrap, or insert an empty block and replace its placeholder. Uses the original player and polygon; player actions are skipped if that player is dead when due.",new LuaActionParameter("seconds","Delay (seconds)",3,0.1,3600,1)),
            new PolygonLuaAction { Id="flag_set",Category="Logic",Label="Set a named flag",Description="Flags are shared by all players in this level and survive saved games.",HasText=true,TextLabel="Flag name",TextDefault="reactor_online",Parameters=new[]{new LuaActionParameter("value","Value (0 = false, 1 = true)",1,0,1)} },
            new PolygonLuaAction { Id="flag_if",Category="Logic",Label="Only run if a flag is set",Description="Select script code to wrap. Checks the flag when this block runs; an unset flag counts as false.",HasText=true,TextLabel="Flag name",TextDefault="reactor_online",Parameters=new[]{new LuaActionParameter("value","Required value (0 = false, 1 = true)",1,0,1)} },
            Action("waves","Monsters","Start enemy waves","Spawns a wave immediately. After its monsters die, waits before the next wave. One encounter per destination polygon; re-entry does not restart it.",Polygon(),new LuaActionParameter("monster","Monster type",8,1,32766),new LuaActionParameter("count","Monsters per wave",3,1,32),new LuaActionParameter("waves","Number of waves",3,1,100),new LuaActionParameter("pause","Seconds between defeated waves",5,0.1,3600,1),new LuaActionParameter("collection","Extra collection ID (-1 = none)",-1,-1,31)),
            Action("spawner","Monsters","Start monster spawner at polygon","Starts one spawner per destination polygon. Spawns immediately, then at the interval. Maximum counts successful spawns; indefinite ignores this limit. Re-entering does not restart an existing spawner.",Polygon(),new LuaActionParameter("monster","Monster type",8,1,32766),new LuaActionParameter("interval","Seconds between spawns",5,0.1,3600,1),new LuaActionParameter("maximum","Maximum total monsters",10,1,10000),new LuaActionParameter("indefinite","Indefinite",0,0,1),new LuaActionParameter("collection","Extra collection ID (-1 = none)",-1,-1,31)),
            Action("spawner_stop","Monsters","Stop monster spawner at polygon","Stops spawning at this polygon. Monsters already spawned remain.",Polygon()),
            Action("fog_set","Fog","Set fog instantly","Changes fog for the whole view until another trigger changes it. Smaller depth means denser fog.",FogTarget(),FogDepth(),Red(),Green(),Blue(),new LuaActionParameter("landscapes","Affect landscapes (0 = no, 1 = yes)",0,0,1)),
            Action("fog_fade","Fog","Transition fog smoothly","Smoothly changes color and depth over the chosen duration. Replaces any running transition on this layer.",FogTarget(),FogDepth(),Red(),Green(),Blue(),new LuaActionParameter("seconds","Duration (seconds)",2,0.1,600,1)),
            Action("fog_on","Fog","Enable fog","Enables the selected fog layer using its current settings.",FogTarget()),
            Action("fog_off","Fog","Disable fog","Disables the selected fog layer.",FogTarget()),
            Action("fog_color","Fog","Change fog color","Changes color without changing depth or enabling disabled fog.",FogTarget(),Red(),Green(),Blue()),
            Action("fog_depth","Fog","Change fog depth","Changes density without changing color or enabling disabled fog. Smaller depth means denser fog.",FogTarget(),FogDepth()),
            Action("teleport","World","Teleport player","Uses the normal in-level teleport effect.",Polygon()),
            Action("lights_dim","Lights","Dim all lights","Subtracts percentage points from every light state, clamped to 0–100%. Zero seconds preserves pulse/flicker timing; a positive duration replaces it with smooth transitions. Takes effect as lights reach their next phase; can also affect light-controlled liquids.",new LuaActionParameter("amount","Brightness decrease (percentage points)",20,0,100,1),new LuaActionParameter("seconds","Transition seconds (0 = preserve timing)",0,0,600,1)),
            Action("lights_brighten","Lights","Brighten all lights","Adds percentage points to every light state, clamped to 0–100%. Zero seconds preserves pulse/flicker timing; a positive duration replaces it with smooth transitions. Takes effect as lights reach their next phase; can also affect light-controlled liquids.",new LuaActionParameter("amount","Brightness increase (percentage points)",20,0,100,1),new LuaActionParameter("seconds","Transition seconds (0 = preserve timing)",0,0,600,1)),
            Action("light_on","Lights","Activate light","Activate a light by ID.",new LuaActionParameter("light","Light ID",0,0,32766)),
            Action("light_off","Lights","Deactivate light","Deactivate a light by ID.",new LuaActionParameter("light","Light ID",0,0,32766)),
            Action("tag_on","Tags","Activate tag","Activates lights and platforms with this tag, using the engine's normal activation rules.",new LuaActionParameter("tag","Tag ID",1,1,32767)),
            Action("tag_off","Tags","Deactivate tag","Deactivates lights and platforms with this tag, using the engine's normal activation rules.",new LuaActionParameter("tag","Tag ID",1,1,32767)),
            Action("tag_toggle","Tags","Toggle tag","If any tagged light or platform is active, switches the group off; otherwise switches it on. Does not invert each member separately.",new LuaActionParameter("tag","Tag ID",1,1,32767)),
            Action("platform_on","World","Activate platform / door","The destination polygon must be a platform.",Polygon()),
            Action("platform_off","World","Deactivate platform / door","The destination polygon must be a platform.",Polygon()),
            new PolygonLuaAction{Id="message",Category="Messages",Label="Show message",Description="Print text to the entering player's HUD.",HasText=true,Parameters=Array.Empty<LuaActionParameter>()}
        };
        // Standard Marathon labels; custom Shapes/Sounds may use different names.
        public static IEnumerable<KeyValuePair<int,string>> NamedChoices(string parameter)
        {
            switch(parameter) {
                case "sound": return new KeyValuePair<int,string>[] {
                    new KeyValuePair<int,string>(0, "startup"),
                    new KeyValuePair<int,string>(1, "teleport in"),
                    new KeyValuePair<int,string>(2, "teleport out"),
                    new KeyValuePair<int,string>(3, "crushed"),
                    new KeyValuePair<int,string>(4, "nuclear hard death"),
                    new KeyValuePair<int,string>(5, "absorbed"),
                    new KeyValuePair<int,string>(6, "breathing"),
                    new KeyValuePair<int,string>(7, "oxygen warning"),
                    new KeyValuePair<int,string>(8, "suffocation"),
                    new KeyValuePair<int,string>(9, "energy refuel"),
                    new KeyValuePair<int,string>(10, "oxygen refuel"),
                    new KeyValuePair<int,string>(11, "cant toggle switch"),
                    new KeyValuePair<int,string>(12, "switch on"),
                    new KeyValuePair<int,string>(13, "switch off"),
                    new KeyValuePair<int,string>(14, "puzzle switch"),
                    new KeyValuePair<int,string>(15, "chip insertion"),
                    new KeyValuePair<int,string>(16, "pattern buffer"),
                    new KeyValuePair<int,string>(17, "destroy control panel"),
                    new KeyValuePair<int,string>(18, "adjust volume"),
                    new KeyValuePair<int,string>(19, "got powerup"),
                    new KeyValuePair<int,string>(20, "get item"),
                    new KeyValuePair<int,string>(21, "bullet ricochet"),
                    new KeyValuePair<int,string>(22, "metallic ricochet"),
                    new KeyValuePair<int,string>(23, "empty gun"),
                    new KeyValuePair<int,string>(24, "spht door opening"),
                    new KeyValuePair<int,string>(25, "spht door closing"),
                    new KeyValuePair<int,string>(26, "spht door obstructed"),
                    new KeyValuePair<int,string>(27, "spht platform starting"),
                    new KeyValuePair<int,string>(28, "spht platform stopping"),
                    new KeyValuePair<int,string>(29, "loon"),
                    new KeyValuePair<int,string>(30, "smg firing"),
                    new KeyValuePair<int,string>(31, "smg reloading"),
                    new KeyValuePair<int,string>(32, "heavy spht platform starting"),
                    new KeyValuePair<int,string>(33, "heavy spht platform stopping"),
                    new KeyValuePair<int,string>(34, "fist hitting"),
                    new KeyValuePair<int,string>(35, "pistol firing"),
                    new KeyValuePair<int,string>(36, "pistol reloading"),
                    new KeyValuePair<int,string>(37, "assault rifle firing"),
                    new KeyValuePair<int,string>(38, "grenade launcher firing"),
                    new KeyValuePair<int,string>(39, "grenade expolding"),
                    new KeyValuePair<int,string>(40, "grenade flyby"),
                    new KeyValuePair<int,string>(41, "fusion firing"),
                    new KeyValuePair<int,string>(42, "fusion exploding"),
                    new KeyValuePair<int,string>(43, "fusion flyby"),
                    new KeyValuePair<int,string>(44, "fusion charging"),
                    new KeyValuePair<int,string>(45, "rocket exploding"),
                    new KeyValuePair<int,string>(46, "rocket flyby"),
                    new KeyValuePair<int,string>(47, "rocket firing"),
                    new KeyValuePair<int,string>(48, "flamethrower"),
                    new KeyValuePair<int,string>(49, "body falling"),
                    new KeyValuePair<int,string>(50, "body exploding"),
                    new KeyValuePair<int,string>(51, "bullet hit flesh"),
                    new KeyValuePair<int,string>(52, "fighter activate"),
                    new KeyValuePair<int,string>(53, "fighter wail"),
                    new KeyValuePair<int,string>(54, "fighter scream"),
                    new KeyValuePair<int,string>(55, "fighter chatter"),
                    new KeyValuePair<int,string>(56, "fighter attack"),
                    new KeyValuePair<int,string>(57, "fighter projectile hit"),
                    new KeyValuePair<int,string>(58, "fighter projectile flyby"),
                    new KeyValuePair<int,string>(59, "spht attack"),
                    new KeyValuePair<int,string>(60, "spht death"),
                    new KeyValuePair<int,string>(61, "spht hit"),
                    new KeyValuePair<int,string>(62, "spht projectile flyby"),
                    new KeyValuePair<int,string>(63, "spht projectile hit"),
                    new KeyValuePair<int,string>(64, "cyborg moving"),
                    new KeyValuePair<int,string>(65, "cyborg attack"),
                    new KeyValuePair<int,string>(66, "cyborg hit"),
                    new KeyValuePair<int,string>(67, "cyborg death"),
                    new KeyValuePair<int,string>(68, "cyborg projectile bounce"),
                    new KeyValuePair<int,string>(69, "cyborg projectile hit"),
                    new KeyValuePair<int,string>(70, "cyborg projectile flyby"),
                    new KeyValuePair<int,string>(71, "drone activate"),
                    new KeyValuePair<int,string>(72, "drone start attack"),
                    new KeyValuePair<int,string>(73, "drone attack"),
                    new KeyValuePair<int,string>(74, "drone dying"),
                    new KeyValuePair<int,string>(75, "drone death"),
                    new KeyValuePair<int,string>(76, "drone projectile hit"),
                    new KeyValuePair<int,string>(77, "drone projectile flyby"),
                    new KeyValuePair<int,string>(78, "bob wail"),
                    new KeyValuePair<int,string>(79, "bob scream"),
                    new KeyValuePair<int,string>(80, "bob hit"),
                    new KeyValuePair<int,string>(81, "bob chatter"),
                    new KeyValuePair<int,string>(82, "assimilated bob chatter"),
                    new KeyValuePair<int,string>(83, "bob trash talk"),
                    new KeyValuePair<int,string>(84, "bob apology"),
                    new KeyValuePair<int,string>(85, "bob activation"),
                    new KeyValuePair<int,string>(86, "bob clear"),
                    new KeyValuePair<int,string>(87, "bob angry"),
                    new KeyValuePair<int,string>(88, "bob secure"),
                    new KeyValuePair<int,string>(89, "bob kill the player"),
                    new KeyValuePair<int,string>(90, "water"),
                    new KeyValuePair<int,string>(91, "sewage"),
                    new KeyValuePair<int,string>(92, "lava"),
                    new KeyValuePair<int,string>(93, "goo"),
                    new KeyValuePair<int,string>(94, "underwater"),
                    new KeyValuePair<int,string>(95, "wind"),
                    new KeyValuePair<int,string>(96, "waterfall"),
                    new KeyValuePair<int,string>(97, "siren"),
                    new KeyValuePair<int,string>(98, "fan"),
                    new KeyValuePair<int,string>(99, "spht door"),
                    new KeyValuePair<int,string>(100, "spht platform"),
                    new KeyValuePair<int,string>(101, "alien harmonics"),
                    new KeyValuePair<int,string>(102, "heavy spht platform"),
                    new KeyValuePair<int,string>(103, "light machinery"),
                    new KeyValuePair<int,string>(104, "heavy machinery"),
                    new KeyValuePair<int,string>(105, "transformer"),
                    new KeyValuePair<int,string>(106, "sparking transformer"),
                    new KeyValuePair<int,string>(107, "water drip"),
                    new KeyValuePair<int,string>(108, "walking in water"),
                    new KeyValuePair<int,string>(109, "exiting water"),
                    new KeyValuePair<int,string>(110, "entering water"),
                    new KeyValuePair<int,string>(111, "small water splash"),
                    new KeyValuePair<int,string>(112, "medium water splash"),
                    new KeyValuePair<int,string>(113, "large water splash"),
                    new KeyValuePair<int,string>(114, "walking in lava"),
                    new KeyValuePair<int,string>(115, "entering lava"),
                    new KeyValuePair<int,string>(116, "exiting lava"),
                    new KeyValuePair<int,string>(117, "small lava splash"),
                    new KeyValuePair<int,string>(118, "medium lava splash"),
                    new KeyValuePair<int,string>(119, "large lava splash"),
                    new KeyValuePair<int,string>(120, "walking in sewage"),
                    new KeyValuePair<int,string>(121, "exiting sewage"),
                    new KeyValuePair<int,string>(122, "entering sewage"),
                    new KeyValuePair<int,string>(123, "small sewage splash"),
                    new KeyValuePair<int,string>(124, "medium sewage splash"),
                    new KeyValuePair<int,string>(125, "large sewage splash"),
                    new KeyValuePair<int,string>(126, "walking in goo"),
                    new KeyValuePair<int,string>(127, "exiting goo"),
                    new KeyValuePair<int,string>(128, "entering goo"),
                    new KeyValuePair<int,string>(129, "small goo splash"),
                    new KeyValuePair<int,string>(130, "medium goo splash"),
                    new KeyValuePair<int,string>(131, "large goo splash"),
                    new KeyValuePair<int,string>(132, "major fusion firing"),
                    new KeyValuePair<int,string>(133, "major fusion charged"),
                    new KeyValuePair<int,string>(134, "assault rifle reloading"),
                    new KeyValuePair<int,string>(135, "assault rifle shell casings"),
                    new KeyValuePair<int,string>(136, "shotgun firing"),
                    new KeyValuePair<int,string>(137, "shotgun reloading"),
                    new KeyValuePair<int,string>(138, "ball bounce"),
                    new KeyValuePair<int,string>(139, "you are it"),
                    new KeyValuePair<int,string>(140, "got ball"),
                    new KeyValuePair<int,string>(141, "computer login"),
                    new KeyValuePair<int,string>(142, "computer logout"),
                    new KeyValuePair<int,string>(143, "computer page"),
                    new KeyValuePair<int,string>(144, "heavy shpt door"),
                    new KeyValuePair<int,string>(145, "heavy spht door opening"),
                    new KeyValuePair<int,string>(146, "heavy spht door closing"),
                    new KeyValuePair<int,string>(147, "heavy spht door open"),
                    new KeyValuePair<int,string>(148, "heavy spht door closed"),
                    new KeyValuePair<int,string>(149, "heavy spht door obstructed"),
                    new KeyValuePair<int,string>(150, "hunter activate"),
                    new KeyValuePair<int,string>(151, "hunter attack"),
                    new KeyValuePair<int,string>(152, "hunter dying"),
                    new KeyValuePair<int,string>(153, "hunter landing"),
                    new KeyValuePair<int,string>(154, "hunter exploding"),
                    new KeyValuePair<int,string>(155, "hunter projectile hit"),
                    new KeyValuePair<int,string>(156, "hunter projectile flyby"),
                    new KeyValuePair<int,string>(157, "enforcer activate"),
                    new KeyValuePair<int,string>(158, "enforcer attack"),
                    new KeyValuePair<int,string>(159, "enforcer projectile hit"),
                    new KeyValuePair<int,string>(160, "enforcer projectile flyby"),
                    new KeyValuePair<int,string>(161, "flickta melee attack"),
                    new KeyValuePair<int,string>(162, "flickta melee hit"),
                    new KeyValuePair<int,string>(163, "flickta projectile attack"),
                    new KeyValuePair<int,string>(164, "flickta projectile sewage hit"),
                    new KeyValuePair<int,string>(165, "flickta projectile sewage flyby"),
                    new KeyValuePair<int,string>(166, "flickta projectile lava hit"),
                    new KeyValuePair<int,string>(167, "flickta projectile lava flyby"),
                    new KeyValuePair<int,string>(168, "flickta dying"),
                    new KeyValuePair<int,string>(169, "machine binder"),
                    new KeyValuePair<int,string>(170, "machine bookpress"),
                    new KeyValuePair<int,string>(171, "machine puncher"),
                    new KeyValuePair<int,string>(172, "electric hum"),
                    new KeyValuePair<int,string>(173, "alarm"),
                    new KeyValuePair<int,string>(174, "night wind"),
                    new KeyValuePair<int,string>(175, "surface explosion"),
                    new KeyValuePair<int,string>(176, "underground explosion"),
                    new KeyValuePair<int,string>(177, "sphtkr attack"),
                    new KeyValuePair<int,string>(178, "sphtkr projectile hit"),
                    new KeyValuePair<int,string>(179, "sphtkr projectile flyby"),
                    new KeyValuePair<int,string>(180, "sphtkr hit"),
                    new KeyValuePair<int,string>(181, "sphtkr exploding"),
                    new KeyValuePair<int,string>(182, "tick chatter"),
                    new KeyValuePair<int,string>(183, "tick falling"),
                    new KeyValuePair<int,string>(184, "tick flapping"),
                    new KeyValuePair<int,string>(185, "tick exploding"),
                    new KeyValuePair<int,string>(186, "ceiling lamp exploding"),
                    new KeyValuePair<int,string>(187, "pfhor platform starting"),
                    new KeyValuePair<int,string>(188, "pfhor platform stopping"),
                    new KeyValuePair<int,string>(189, "pfhor platform"),
                    new KeyValuePair<int,string>(190, "pfhor door opening"),
                    new KeyValuePair<int,string>(191, "pfhor door closing"),
                    new KeyValuePair<int,string>(192, "pfhor door obstructed"),
                    new KeyValuePair<int,string>(193, "pfhor door"),
                    new KeyValuePair<int,string>(194, "pfhor switch off"),
                    new KeyValuePair<int,string>(195, "pfhor switch on"),
                    new KeyValuePair<int,string>(196, "juggernaut firing"),
                    new KeyValuePair<int,string>(197, "juggernaut warning"),
                    new KeyValuePair<int,string>(198, "juggernaut exploding"),
                    new KeyValuePair<int,string>(199, "juggernaut start attack"),
                    new KeyValuePair<int,string>(200, "enforcer exploding"),
                    new KeyValuePair<int,string>(201, "alien noise 1"),
                    new KeyValuePair<int,string>(202, "alien noise 2"),
                    new KeyValuePair<int,string>(203, "vacbob wail"),
                    new KeyValuePair<int,string>(204, "vacbob scream"),
                    new KeyValuePair<int,string>(205, "vacbob hit"),
                    new KeyValuePair<int,string>(206, "vacbob chatter"),
                    new KeyValuePair<int,string>(207, "assimilated vacbob chatter"),
                    new KeyValuePair<int,string>(208, "vacbob trash talk"),
                    new KeyValuePair<int,string>(209, "vacbob apology"),
                    new KeyValuePair<int,string>(210, "vacbob activation"),
                    new KeyValuePair<int,string>(211, "vacbob clear"),
                    new KeyValuePair<int,string>(212, "vacbob angry"),
                    new KeyValuePair<int,string>(213, "vacbob secure"),
                    new KeyValuePair<int,string>(214, "vacbob kill the player"),
                };
                case "item": return new KeyValuePair<int,string>[] {
                    new KeyValuePair<int,string>(2, "pistol ammo"),
                    new KeyValuePair<int,string>(4, "fusion pistol ammo"),
                    new KeyValuePair<int,string>(6, "assault rifle ammo"),
                    new KeyValuePair<int,string>(7, "assault rifle grenades"),
                    new KeyValuePair<int,string>(9, "missile launcher ammo"),
                    new KeyValuePair<int,string>(14, "alien weapon ammo"),
                    new KeyValuePair<int,string>(16, "flamethrower ammo"),
                    new KeyValuePair<int,string>(23, "shotgun ammo"),
                    new KeyValuePair<int,string>(35, "smg ammo"),
                };
                case "collection": return new KeyValuePair<int,string>[] {
                    new KeyValuePair<int,string>(-1, "Automatic"),
                    new KeyValuePair<int,string>(0, "interface"),
                    new KeyValuePair<int,string>(1, "weapons in hand"),
                    new KeyValuePair<int,string>(2, "juggernaut"),
                    new KeyValuePair<int,string>(3, "tick"),
                    new KeyValuePair<int,string>(4, "explosions"),
                    new KeyValuePair<int,string>(5, "hunter"),
                    new KeyValuePair<int,string>(6, "player"),
                    new KeyValuePair<int,string>(7, "items"),
                    new KeyValuePair<int,string>(8, "trooper"),
                    new KeyValuePair<int,string>(9, "fighter"),
                    new KeyValuePair<int,string>(10, "defender"),
                    new KeyValuePair<int,string>(11, "yeti"),
                    new KeyValuePair<int,string>(12, "bob"),
                    new KeyValuePair<int,string>(13, "vacbob"),
                    new KeyValuePair<int,string>(14, "enforcer"),
                    new KeyValuePair<int,string>(15, "drone"),
                    new KeyValuePair<int,string>(16, "compiler"),
                    new KeyValuePair<int,string>(17, "water"),
                    new KeyValuePair<int,string>(18, "lava"),
                    new KeyValuePair<int,string>(19, "sewage"),
                    new KeyValuePair<int,string>(20, "jjaro"),
                    new KeyValuePair<int,string>(21, "pfhor"),
                    new KeyValuePair<int,string>(22, "water scenery"),
                    new KeyValuePair<int,string>(23, "lava scenery"),
                    new KeyValuePair<int,string>(24, "sewage scenery"),
                    new KeyValuePair<int,string>(25, "jjaro scenery"),
                    new KeyValuePair<int,string>(26, "pfhor scenery"),
                    new KeyValuePair<int,string>(27, "day"),
                    new KeyValuePair<int,string>(28, "night"),
                    new KeyValuePair<int,string>(29, "moon"),
                    new KeyValuePair<int,string>(30, "space"),
                    new KeyValuePair<int,string>(31, "cyborg"),
                };

                case "monster": return new KeyValuePair<int,string>[] {
                    new KeyValuePair<int,string>(1, "Tick Energy"),
                    new KeyValuePair<int,string>(2, "Tick Oxygen"),
                    new KeyValuePair<int,string>(3, "Tick Kamakazi"),
                    new KeyValuePair<int,string>(4, "Compiler Minor"),
                    new KeyValuePair<int,string>(5, "Compiler Major"),
                    new KeyValuePair<int,string>(6, "Compiler Minor Invisible"),
                    new KeyValuePair<int,string>(7, "Compiler Major Invisible"),
                    new KeyValuePair<int,string>(8, "Fighter Minor"),
                    new KeyValuePair<int,string>(9, "Fighter Major"),
                    new KeyValuePair<int,string>(10, "Fighter Minor Projectile"),
                    new KeyValuePair<int,string>(11, "Fighter Major Projectile"),
                    new KeyValuePair<int,string>(12, "Civilian Crew"),
                    new KeyValuePair<int,string>(13, "Civilian Science"),
                    new KeyValuePair<int,string>(14, "Civilian Security"),
                    new KeyValuePair<int,string>(15, "Civilian Assimilated"),
                    new KeyValuePair<int,string>(16, "Hummer Minor"),
                    new KeyValuePair<int,string>(17, "Hummer Major"),
                    new KeyValuePair<int,string>(18, "Hummer Big Minor"),
                    new KeyValuePair<int,string>(19, "Hummer Big Major"),
                    new KeyValuePair<int,string>(20, "Hummer Possessed"),
                    new KeyValuePair<int,string>(21, "Cyborg Minor"),
                    new KeyValuePair<int,string>(22, "Cyborg Major"),
                    new KeyValuePair<int,string>(23, "Cyborg Flame Minor"),
                    new KeyValuePair<int,string>(24, "Cyborg Flame Major"),
                    new KeyValuePair<int,string>(25, "Enforcer Minor"),
                    new KeyValuePair<int,string>(26, "Enforcer Major"),
                    new KeyValuePair<int,string>(27, "Hunter Minor"),
                    new KeyValuePair<int,string>(28, "Hunter Major"),
                    new KeyValuePair<int,string>(29, "Trooper Minor"),
                    new KeyValuePair<int,string>(30, "Trooper Major"),
                    new KeyValuePair<int,string>(31, "Mother of all Cyborgs"),
                    new KeyValuePair<int,string>(32, "Mother of all Hunters"),
                    new KeyValuePair<int,string>(33, "Sewage Yeti"),
                    new KeyValuePair<int,string>(34, "Water Yeti"),
                    new KeyValuePair<int,string>(35, "Lava Yeti"),
                    new KeyValuePair<int,string>(36, "Defender Minor"),
                    new KeyValuePair<int,string>(37, "Defender Major"),
                    new KeyValuePair<int,string>(38, "Juggernaut Minor"),
                    new KeyValuePair<int,string>(39, "Juggernaut Major"),
                    new KeyValuePair<int,string>(40, "Tiny Figher"),
                    new KeyValuePair<int,string>(41, "Tiny Bob"),
                    new KeyValuePair<int,string>(42, "Tiny Yeti"),
                    new KeyValuePair<int,string>(43, "Civilian Fusion Crew"),
                    new KeyValuePair<int,string>(44, "Civilian Fusion Science"),
                    new KeyValuePair<int,string>(45, "Civilian Fusion Security"),
                    new KeyValuePair<int,string>(46, "Civilian Fusion Assimilated"),
                };
                case "underwater": return new[]{new KeyValuePair<int,string>(0,"Normal fog"),new KeyValuePair<int,string>(1,"Underwater fog")};
                case "landscapes": return new[]{new KeyValuePair<int,string>(0,"No"),new KeyValuePair<int,string>(1,"Yes")};
                default: return Array.Empty<KeyValuePair<int,string>>();
            }
        }
        public static string Quote(string value)
        {
            var result=new StringBuilder("\"");
            foreach(char c in value??"") {
                if(c=='\\' || c=='\"')result.Append('\\').Append(c);
                else if(c<32)result.Append('\\').Append(((int)c).ToString("D3",CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
        public static string UpgradeSpawnPresets(string code)
        {
            return Regex.Replace(code, @"(?m)^-- Spawn monsters
do
(?<preload>-- Forge Ahead preload collection:(?<collection>\d+)
)?local target = (?<target>polygon|Polygons\[(?<polygon>\d+)\])
if target and MonsterTypes\[(?<monster>\d+)\] then
  for i = 1, (?<count>\d+) do
    local monster = Monsters.new\(target.x, target.y, (?:0|target.floor.height), target, \k<monster>\)
    if monster then monster.active = true else player:print\('Unable to spawn monster'\); break end
  end
else player:print\('Spawn polygon or monster type does not exist'\) end
end
", match => {
                var values = new Dictionary<string,double>();
                foreach(string key in new[]{"monster","count","polygon","collection"})
                    values[key] = match.Groups[key].Success ? double.Parse(match.Groups[key].Value,CultureInfo.InvariantCulture) : -1;
                try { return Generate("spawn",values).TrimStart('\n'); }
                catch(ArgumentException) { return match.Value; }
            });
        }
        public static string Generate(string id,IDictionary<string,double> values=null,string text="Trigger activated",string body=null)
        {
            var action=Catalog.SingleOrDefault(a=>a.Id==id)??throw new ArgumentException("Unknown Lua action.");
            var numbers=new Dictionary<string,string>();
            foreach(var p in action.Parameters) {
                double value=values!=null && values.TryGetValue(p.Id,out double entered)?entered:p.Default;
                if(double.IsNaN(value) || double.IsInfinity(value) || value<p.Minimum || value>p.Maximum || (p.Digits==0 && value!=Math.Truncate(value)))throw new ArgumentException("Invalid value for "+p.Label+".");
                numbers[p.Id]=value.ToString("0.########",CultureInfo.InvariantCulture);
            }
            string N(string name)=>numbers[name];
            string Target()=>N("polygon")=="-1"?"polygon":"Polygons["+N("polygon")+"]";
            string ammo="Game.proper_item_accounting = true\nfor _, item in ipairs({2,4,6,7,9,14,16,23,35}) do player.items[item] = 999 end\n";
            string code;
            switch(id) {
                case "camera_play": code="if forge_ahead_camera_play then forge_ahead_camera_play("+Quote(text??"")+", player) else player:print('No camera paths in this level') end\n";break;
                case "camera_stop": code="if player.local_ and forge_ahead_camera_stop then forge_ahead_camera_stop() end\n";break;
                case "sun_path":case "sun_set":case "sun_fade":case "sunrise":case "sunset":case "sun_on":case "sun_off":case "sun_stop":case "sun_reset":
                    code=SunCode(id,N);break;
                case "flag_set":case "flag_if":
                    if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter a flag name.");
                    string flag=Quote(text.Trim());
                    code=id=="flag_set"?"Game._forge_ahead_flags = Game._forge_ahead_flags or {}\nGame._forge_ahead_flags["+flag+"] = "+(N("value")=="1"?"true":"false")+"\n":
                        "if ((Game._forge_ahead_flags and Game._forge_ahead_flags["+flag+"]) == true) == "+(N("value")=="1"?"true":"false")+" then\n"+(body??"-- Insert actions here\n")+"\nend\n";
                    break;
                case "delay":
                    string delayed=body??"player:print('Delayed action')";
                    code="";
                    foreach(Match marker in Regex.Matches(delayed,@"(?m)^-- Forge Ahead preload (?:monster|collection):\d+\s*$"))code+=marker.Value.Trim()+"\n";
                    foreach(Match spawn in Regex.Matches(delayed,@"Monsters\.new\([^\r\n]*, target, (\d+)\)"))code+="-- Forge Ahead preload monster:"+spawn.Groups[1].Value+"\n";
                    code+="Game._forge_ahead_delays = Game._forge_ahead_delays or {}\ntable.insert(Game._forge_ahead_delays, {ticks=math.max(1,math.floor("+N("seconds")+" * 30 + 0.5)),player=player.index,polygon=polygon.index,code="+Quote(delayed)+"})\n";
                    break;
                case "waves":
                    code="-- Forge Ahead preload monster:"+N("monster")+"\n"+(N("collection")=="-1"?"":"-- Forge Ahead preload collection:"+N("collection")+"\n")+"local target = "+Target()+"\nif target and MonsterTypes["+N("monster")+"] then\n  Game._forge_ahead_waves = Game._forge_ahead_waves or {}\n  if not Game._forge_ahead_waves[target.index] then\n    Game._forge_ahead_waves[target.index] = {monster="+N("monster")+",size="+N("count")+",left="+N("waves")+",pause=math.max(1,math.floor("+N("pause")+" * 30 + 0.5)),wait=0,alive={},pending="+N("count")+",finished=false}\n  end\nelse player:print('Wave polygon or monster type does not exist') end\n";break;
                case "ammo": code=ammo;break;
                case "weapons": code="Game.proper_item_accounting = true\nfor _, weapon in ipairs({{1,2},{3,1},{5,1},{8,1},{13,1},{15,1},{22,2},{34,1}}) do\n  player.items[weapon[1]] = math.max(player.items[weapon[1]], weapon[2])\nend\n"+ammo;break;
                case "ammo_item":
                    int item=int.Parse(N("item"),CultureInfo.InvariantCulture);
                    if(!new[]{2,4,6,7,9,14,16,23,35}.Contains(item))throw new ArgumentException("Choose an ammunition item ID: 2, 4, 6, 7, 9, 14, 16, 23 or 35.");
                    code="Game.proper_item_accounting = true\nplayer.items["+item+"] = player.items["+item+"] + "+N("count")+"\n";break;
                case "heal":code="if player.life < "+N("cap")+" then player.life = math.min("+N("cap")+", player.life + "+N("amount")+") end\n";break;
                case "full_health":code="player.life = math.max(player.life, "+N("cap")+")\n";break;
                case "slow_heal":code="player._forge_ahead_heal = {remaining="+N("amount")+", rate="+N("rate")+", cap="+N("cap")+", credit=0}\n";break;
                case "oxygen":code="player.oxygen = 32767 -- engine clamps to full oxygen\n";break;
                case "sound_player":code="player:play_sound("+N("sound")+", "+N("pitch")+")\n";break;
                case "sound_polygon":code="local target = "+Target()+"\nif target then target:play_sound(target.x, target.y, target.floor.height, "+N("sound")+", "+N("pitch")+") else player:print('Sound polygon does not exist') end\n";break;
                case "monsters_move":
                    code="local source = "+(N("source_polygon")=="-1"?"polygon":"Polygons["+N("source_polygon")+"]")+"\nlocal destination = Polygons["+N("destination_polygon")+"]\nif source and destination then\n  if source ~= destination then\n    for monster in Monsters() do\n      if not monster.player and monster.polygon == source and\n         (monster.vitality > 0 or (not monster.active and monster.vitality == -1)) and\n         monster.action ~= 'dying hard' and monster.action ~= 'dying soft' and monster.action ~= 'dying flaming' then\n        monster:move_by_path(destination)\n        Game._forge_ahead_move_orders = Game._forge_ahead_move_orders or {}\n        Game._forge_ahead_move_orders[monster.index] = {destination=destination.index,wait=30 + monster.index % 15,remaining=3600}\n        Game._forge_ahead_move_stops = Game._forge_ahead_move_stops or {}\n        Game._forge_ahead_move_stops[monster.index] = "+(N("stop_destination")=="1"?"destination.index":"nil")+"\n      end\n    end\n  end\nelse player:print('Source or destination polygon does not exist') end\n";break;
                case "spawn":code=(N("collection")=="-1"?"":"-- Forge Ahead preload collection:"+N("collection")+"\n")+"local target = "+Target()+"\nif target and MonsterTypes["+N("monster")+"] then\n  local x, y, count = 0, 0, 0\n  for vertex in target.endpoints() do\n    x, y, count = x + vertex.x, y + vertex.y, count + 1\n  end\n  if count < 3 then player:print('Invalid spawn polygon'); return end\n  x, y = x / count, y / count\n  -- Monsters.new uses a height relative to the floor: zero is floor height.\n  for i = 1, "+N("count")+" do\n    local monster = Monsters.new(x, y, 0, target, "+N("monster")+")\n    if monster then monster.active = true else player:print('Unable to spawn monster'); break end\n  end\nelse player:print('Spawn polygon or monster type does not exist') end\n";break;
                case "spawner":
                    code="-- Forge Ahead preload monster:"+N("monster")+"\n"+(N("collection")=="-1"?"":"-- Forge Ahead preload collection:"+N("collection")+"\n")+"local target = "+Target()+"\nif target and MonsterTypes["+N("monster")+"] then\n  Game._forge_ahead_spawners = Game._forge_ahead_spawners or {}\n  if not Game._forge_ahead_spawners[target.index] then\n    Game._forge_ahead_spawners[target.index] = {monster="+N("monster")+",interval=math.max(1,math.floor("+N("interval")+" * 30 + 0.5)),wait=0,remaining="+(N("indefinite")=="1"?"-1":N("maximum"))+",finished=false}\n  end\nelse player:print('Spawn polygon or monster type does not exist') end\n";break;
                case "spawner_stop":
                    code="local target = "+Target()+"\nif target and Game._forge_ahead_spawners then Game._forge_ahead_spawners[target.index] = nil end\n";break;
                case "fog_fade":
                    code="local key = "+Quote(N("underwater")=="1"?"underwater_fog":"fog")+"\nlocal fog = Level[key]\nGame._forge_ahead_fog = Game._forge_ahead_fog or {}\nGame._forge_ahead_fog[key] = {elapsed=0, ticks=math.max(1, math.floor("+N("seconds")+" * 30 + 0.5)), from={r=fog.color.r,g=fog.color.g,b=fog.color.b,depth=fog.active and fog.depth or 1024}, to={r="+N("red")+",g="+N("green")+",b="+N("blue")+",depth="+N("depth")+"}}\nif not fog.active then fog.depth = 1024 end\nfog.active = true\n";break;
                case "fog_set":case "fog_on":case "fog_off":case "fog_color":case "fog_depth":
                    code="local fog = Level."+(N("underwater")=="1"?"underwater_fog":"fog")+"\n";
                    code+="if Game._forge_ahead_fog then Game._forge_ahead_fog["+Quote(N("underwater")=="1"?"underwater_fog":"fog")+"] = nil end\n";
                    if(id=="fog_set" || id=="fog_color")code+="fog.color.r = "+N("red")+"\nfog.color.g = "+N("green")+"\nfog.color.b = "+N("blue")+"\n";
                    if(id=="fog_set" || id=="fog_depth")code+="fog.depth = "+N("depth")+"\n";
                    if(id=="fog_set")code+="fog.affects_landscapes = "+(N("landscapes")=="1"?"true":"false")+"\n";
                    if(id=="fog_set" || id=="fog_on" || id=="fog_off")code+="fog.active = "+(id=="fog_off"?"false":"true")+"\n";
                    break;
                case "teleport":code="local target = "+Target()+"\nif target then player:teleport(target) else player:print('Teleport polygon does not exist') end\n";break;
                case "lights_dim":case "lights_brighten":
                    code="local change = "+(id=="lights_dim"?"-":"")+N("amount")+" / 100\nfor light in Lights() do\n  for i = 0, #light.states - 1 do\n    local state = light.states[i]\n    state.intensity = math.max(0, math.min(1, state.intensity + change))\n"+(N("seconds")=="0"?"":"    state.light_function = \"smooth\"\n    state.period = math.max(1, math.floor("+N("seconds")+" * 30 + 0.5))\n    state.delta_period = 0\n")+"  end\nend\n";break;
                case "light_on":case "light_off":code="local light = Lights["+N("light")+"]\nif light then light.active = "+(id=="light_on"?"true":"false")+" else player:print('Light does not exist') end\n";break;
                case "tag_on":case "tag_off":case "tag_toggle":
                    code="local tag = Tags["+N("tag")+"]\ntag.active = "+(id=="tag_toggle"?"not tag.active":id=="tag_on"?"true":"false")+"\n";break;
                case "platform_on":case "platform_off":code="local target = "+Target()+"\nif target and target.platform then target.platform.active = "+(id=="platform_on"?"true":"false")+" else player:print('Destination is not a platform') end\n";break;
                case "message":code="player:print("+Quote(text)+")\n";break;
                default:throw new ArgumentException("Unknown Lua action.");
            }
            return "\n-- "+action.Label+"\ndo\n"+code+"end\n";
        }
    }
}
