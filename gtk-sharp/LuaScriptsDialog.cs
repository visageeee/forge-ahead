using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        void SetupPolygonLuaActions(Box contents,TextView source,Dialog owner)
        {
            var frame=new Frame("Premade actions");var panel=new Box(Orientation.Vertical,6) { Margin=8 };
            var category=new ComboBoxText();var action=new ComboBoxText();
            var row=new Box(Orientation.Vertical,8);row.PackStart(category,false,false,0);row.PackStart(action,false,false,0);panel.PackStart(row,false,false,0);
            string[] categories=PolygonLuaActions.Catalog.Select(p=>p.Category).Distinct().ToArray();foreach(string name in categories)category.AppendText(name);
            var description=new Label("") { Xalign=0,LineWrap=true,MaxWidthChars=32 };panel.PackStart(description,false,false,0);
            var parameters=new Box(Orientation.Vertical,4);panel.PackStart(parameters,false,false,0);
            var inputs=new Dictionary<string,SpinButton>();Entry text=null;PolygonLuaAction[] choices=Array.Empty<PolygonLuaAction>();bool refreshing=false;
            void Display() {
                foreach(var widget in parameters.Children) { parameters.Remove(widget);widget.Destroy(); }
                inputs.Clear();text=null;if(action.Active<0 || action.Active>=choices.Length)return;
                var selected=choices[action.Active];description.Text=selected.Description;
                foreach(var parameter in selected.Parameters) {
                    var field=new Box(Orientation.Vertical,4);field.PackStart(new Label(parameter.Label) { Xalign=0,LineWrap=true,MaxWidthChars=32 },false,false,0);
                    var input=new SpinButton(parameter.Minimum,parameter.Maximum,parameter.Digits==0?1:0.1) { Numeric=true,Digits=(uint)parameter.Digits,Value=parameter.Default,WidthChars=7 };
                    if(parameter.Id=="indefinite") {
                        var check=new CheckButton("Indefinite") { Active=parameter.Default==1 };
                        check.Toggled+=(o,e)=>input.Value=check.Active?1:0;
                        inputs.Add(parameter.Id,input);parameters.PackStart(check,false,false,0);continue;
                    }
                    var names=PolygonLuaActions.NamedChoices(parameter.Id).ToList();
                    if(parameter.Id=="polygon") {
                        names.Add(new KeyValuePair<int,string>(-1,"Triggering polygon"));
                        for(int i=0;i<Level.Polygons.Count;i++)names.Add(new KeyValuePair<int,string>(i,"Polygon "+i+" — "+Level.Polygons[i].Type));
                    }
                    if(parameter.Id=="light")for(int i=0;i<Level.Lights.Count;i++)names.Add(new KeyValuePair<int,string>(i,"Light "+i));
                    if(names.Count>0) {
                        var picker=new ComboBoxText { TooltipText="Standard Marathon names; custom scenarios may differ. You can enter an ID directly." };
                        picker.AppendText("Custom ID");foreach(var name in names)picker.AppendText(name.Key+" — "+name.Value);
                        foreach(var cell in picker.Cells)if(cell is CellRendererText label)label.Ellipsize=Pango.EllipsizeMode.End;
                        picker.WidthRequest=230;
                        bool syncing=false;
                        void SyncName() { syncing=true;picker.Active=names.FindIndex(n=>n.Key==input.Value)+1;syncing=false; }
                        input.ValueChanged+=(o,e)=>SyncName();
                        picker.Changed+=(o,e)=> { if(!syncing && picker.Active>0)input.Value=names[picker.Active-1].Key; };
                        SyncName();field.PackStart(picker,false,false,0);
                    }
                    inputs.Add(parameter.Id,input);field.PackStart(input,false,false,0);parameters.PackStart(field,false,false,0);
                }
                if(selected.HasText) { text=new Entry { Text="Trigger activated" };parameters.PackStart(text,false,false,0); }
                parameters.ShowAll();
            }
            category.Changed+=(sender,args)=> {
                refreshing=true;action.RemoveAll();choices=category.Active<0?Array.Empty<PolygonLuaAction>():PolygonLuaActions.Catalog.Where(p=>p.Category==categories[category.Active]).ToArray();
                foreach(var choice in choices)action.AppendText(choice.Label);action.Active=choices.Length>0?0:-1;refreshing=false;Display();
            };
            action.Changed+=(sender,args)=> { if(!refreshing)Display(); };
            var insert=new Button("Insert at cursor →") { HeightRequest=48 };
            insert.StyleContext.AddClass("suggested-action");
            insert.TooltipText="Add this action to the script at the current cursor position.";
            insert.Clicked+=(sender,args)=> {
                if(action.Active<0)return;
                try {
                    foreach(var input in inputs.Values)input.Update();
                    string code=PolygonLuaActions.Generate(choices[action.Active].Id,inputs.ToDictionary(p=>p.Key,p=>p.Value.Value),text?.Text);
                    source.Buffer.InsertAtCursor(code);source.GrabFocus();
                } catch(Exception ex) { using var error=new MessageDialog(owner,DialogFlags.Modal,MessageType.Error,ButtonsType.Close,"Unable to insert action.");error.SecondaryText=ex.Message;error.Run(); }
            };
            var optionsScroll=new ScrolledWindow { HscrollbarPolicy=PolicyType.Never,VscrollbarPolicy=PolicyType.Automatic };
            optionsScroll.AddWithViewport(panel);
            var sidebar=new Box(Orientation.Vertical,10) { WidthRequest=340 };
            sidebar.PackStart(optionsScroll,true,true,0);sidebar.PackStart(insert,false,false,0);
            frame.Add(sidebar);contents.PackStart(frame,false,false,0);category.Active=0;
        }

        static void SetupLuaSyntaxColoring(TextView view)
        {
            var buffer=view.Buffer;
            var comment=new TextTag(null) { Foreground="#779977",Style=Pango.Style.Italic };
            var literal=new TextTag(null) { Foreground="#b68045" };
            var keyword=new TextTag(null) { Foreground="#8b79c6",Weight=Pango.Weight.Bold };
            var number=new TextTag(null) { Foreground="#529ca8" };
            foreach(var tag in new[]{comment,literal,keyword,number})buffer.TagTable.Add(tag);
            var tokens=new System.Text.RegularExpressions.Regex(
                @"(?<comment>--\[(?<eq>=*)\[[\s\S]*?\]\k<eq>\]|--[^\r\n]*)|(?<literal>\[(?<sq>=*)\[[\s\S]*?\]\k<sq>\]|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|(?<keyword>\b(?:and|break|do|else|elseif|end|false|for|function|goto|if|in|local|nil|not|or|repeat|return|then|true|until|while)\b)|(?<number>\b(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b)");
            uint pending=0;
            void Colorize() {
                string text=buffer.Text;
                // Gtk offsets count Unicode characters; .NET regex offsets count UTF-16 units.
                var offsets=new int[text.Length+1];int offset=0;
                for(int i=0;i<text.Length;i++) { offsets[i]=offset;if(char.IsHighSurrogate(text[i]) && i+1<text.Length && char.IsLowSurrogate(text[i+1]))offsets[++i]=offset;offset++; }
                offsets[text.Length]=offset;
                foreach(var tag in new[]{comment,literal,keyword,number})buffer.RemoveTag(tag,buffer.StartIter,buffer.EndIter);
                foreach(System.Text.RegularExpressions.Match match in tokens.Matches(text)) {
                    var tag=match.Groups["comment"].Success?comment:match.Groups["literal"].Success?literal:match.Groups["keyword"].Success?keyword:number;
                    buffer.ApplyTag(tag,buffer.GetIterAtOffset(offsets[match.Index]),buffer.GetIterAtOffset(offsets[match.Index+match.Length]));
                }
            }
            buffer.Changed+=(o,e)=> { if(pending!=0)GLib.Source.Remove(pending);pending=GLib.Timeout.Add(150,()=> { pending=0;Colorize();return false; }); };
            view.Destroyed+=(o,e)=> { if(pending!=0) { GLib.Source.Remove(pending);pending=0; } };
            Colorize();
        }

        void OnPolygonLua(object sender,EventArgs args)
        {
            var selected=editor.SelectedLuaPolygons();
            if(selected.Length==0) { using var error=new MessageDialog(window1,DialogFlags.Modal,MessageType.Info,ButtonsType.Close,"Select at least one polygon first.");error.Run();return; }
            var first=Level.Polygons[selected[0]];
            using var dialog=new Dialog("Polygon Lua trigger",window1,DialogFlags.Modal,"Cancel",ResponseType.Cancel,"Remove script action from polygon",ResponseType.Reject,"Apply script action to polygon",ResponseType.Accept);
            dialog.SetDefaultSize(1100,760);
            var applyButton=(Button)dialog.GetWidgetForResponse((int)ResponseType.Accept);
            var removeButton=(Button)dialog.GetWidgetForResponse((int)ResponseType.Reject);
            applyButton.StyleContext.AddClass("suggested-action");
            applyButton.HeightRequest=48;
            removeButton.HeightRequest=48;
            dialog.GetWidgetForResponse((int)ResponseType.Cancel).HeightRequest=48;
            removeButton.StyleContext.AddClass("destructive-action");
            using var actionStyle=new CssProvider();
            actionStyle.LoadFromData("button { background-image: none; background-color: #b52b36; color: white; } button:hover { background-color: #ce3441; } button:active { background-color: #95222c; }");
            removeButton.StyleContext.AddProvider(actionStyle,800);
            using var applyStyle=new CssProvider();
            applyStyle.LoadFromData("button { font-weight: bold; padding: 10px 18px; }");
            applyButton.StyleContext.AddProvider(applyStyle,800);
            var contents=new Box(Orientation.Vertical,8) { Margin=12 };
            var enabled=new CheckButton("Enabled") { Active=first.LuaTriggerEnabled || string.IsNullOrEmpty(first.LuaTriggerCode) };
            var once=new CheckButton("Once per level (shared by all players)") { Active=first.LuaTriggerOnce };
            var triggerOptions=new Box(Orientation.Horizontal,20);
            triggerOptions.PackStart(new Label("Polygons: "+string.Join(", ",selected)) { Xalign=0,Ellipsize=Pango.EllipsizeMode.End,TooltipText=string.Join(", ",selected) },true,true,0);
            triggerOptions.PackStart(enabled,false,false,0);triggerOptions.PackStart(once,false,false,0);
            contents.PackStart(triggerOptions,false,false,0);
            contents.PackStart(new Label("Runs when a living player enters, including spawning here.") { Xalign=0 },false,false,0);
            var source=new TextView { Monospace=true,WrapMode=WrapMode.None };
            source.Buffer.Text=string.IsNullOrEmpty(first.LuaTriggerCode) ? "player:print(\"Trigger activated\")\n-- Or call: my_level_function(player, polygon)\n" : first.LuaTriggerCode;
            var body=new Box(Orientation.Horizontal,12);
            var footer=new Box(Orientation.Horizontal,10);
            SetupPolygonLuaActions(body,source,dialog);
            foreach(var response in new[]{ResponseType.Accept,ResponseType.Reject,ResponseType.Cancel}) {
                var button=dialog.GetWidgetForResponse((int)response);
                ((Container)button.Parent).Remove(button);
                footer.PackEnd(button,false,false,0);
            }
            SetupLuaSyntaxColoring(source);
            var scriptPanel=new Box(Orientation.Vertical,6);
            scriptPanel.PackStart(new Label("Polygon script") { Xalign=0 },false,false,0);
            var scroll=new ScrolledWindow();scroll.Add(source);scriptPanel.PackStart(scroll,true,true,0);
            body.PackStart(scriptPanel,true,true,0);contents.PackStart(body,true,true,0);
            contents.PackStart(new Label("Save the map after Apply. Triggers are embedded automatically; no external Lua file is needed.") { Xalign=0,LineWrap=true },false,false,0);
            contents.PackStart(footer,false,false,0);
            dialog.ContentArea.PackStart(contents,true,true,0);dialog.ShowAll();
            while(true) {
                int response=dialog.Run();if(response!=(int)ResponseType.Accept && response!=(int)ResponseType.Reject)break;
                if(editor.SetSelectedPolygonLua(response==(int)ResponseType.Reject ? "" : source.Buffer.Text,response==(int)ResponseType.Accept && enabled.Active,response==(int)ResponseType.Accept && once.Active,out string reason)) { UpdateInspector();UpdateStatusBar();Redraw();break; }
                using var error=new MessageDialog(dialog,DialogFlags.Modal,MessageType.Error,ButtonsType.Close,"Unable to update polygon Lua triggers.");error.SecondaryText=reason;error.Run();
            }
        }

        protected void OnEmbeddedLua(object sender, EventArgs args)
        {
            using var dialog = new Dialog("Level Lua Scripts", window1, DialogFlags.Modal,
                "Cancel", ResponseType.Cancel, "Apply", ResponseType.Accept);
            dialog.SetDefaultSize(800, 600);
            void Error(Exception e) {
                using var message = new MessageDialog(dialog, DialogFlags.Modal, MessageType.Error,
                    ButtonsType.Close, "Unable to update Lua scripts.");
                message.SecondaryText = e.Message; message.Run();
            }
            List<EmbeddedLuaScript> scripts;
            Level.Chunks.TryGetValue(EmbeddedLua.Tag, out byte[] original);
            try {
                scripts = EmbeddedLua.Read(original);
                // Reject undecodable source without changing the existing chunk.
                foreach (var script in scripts) _ = script.Text;
            } catch (Exception e) { Error(e); return; }
            var contents = new Box(Orientation.Vertical, 8) { Margin = 12 };
            contents.PackStart(new Label("Apply embeds scripts in this level. Save the map to keep them.\n" +
                "Scripts run in list order in-game; scripts also run in visual mode.") { Xalign = 0 }, false, false, 0);
            var picker = new ComboBoxText();
            contents.PackStart(picker, false, false, 0);
            var name = new Entry { PlaceholderText = "Script name" };
            contents.PackStart(name, false, false, 0);
            var source = new TextView { Monospace = true, WrapMode = WrapMode.None };
            SetupLuaSyntaxColoring(source);
            var scroll = new ScrolledWindow(); scroll.Add(source);
            contents.PackStart(scroll, true, true, 0);
            var buttons = new Box(Orientation.Horizontal, 6);
            contents.PackStart(buttons, false, false, 0);
            int selected = -1;
            bool refreshing = false;
            void Flush() {
                if (selected < 0 || PolygonLua.IsGenerated(scripts[selected])) return;
                var script = scripts[selected];
                // Preserve original byte encoding, headers and flags when untouched.
                var validated = new EmbeddedLuaScript { Name = name.Text, Text = source.Buffer.Text };
                if (script.Name != name.Text) script.Name = validated.Name;
                if (script.Text != source.Buffer.Text) script.Source = validated.Source;
            }
            void Display(int index) {
                selected = index;
                name.Sensitive = index>=0 && !PolygonLua.IsGenerated(scripts[index]);
                source.Editable = index>=0 && !PolygonLua.IsGenerated(scripts[index]);
                source.Sensitive=index>=0;
                name.Text = index >= 0 ? scripts[index].Name : "";
                source.Buffer.Text = index >= 0 ? scripts[index].Text : "";
            }
            void Refresh(int index) {
                refreshing = true;
                try {
                    picker.RemoveAll();
                    foreach (var script in scripts) picker.AppendText(script.Name);
                    picker.Active = index; Display(index);
                } finally { refreshing = false; }
            }
            picker.Changed += (o, e) => {
                if (refreshing) return;
                int target = picker.Active;
                try { Flush(); Refresh(target); }
                catch (Exception ex) {
                    refreshing = true; picker.Active = selected; refreshing = false; Error(ex);
                }
            };
            void Button(string title, System.Action action) {
                var button = new Gtk.Button(title);
                button.Clicked += (o, e) => { try { action(); } catch (Exception ex) { Error(ex); } };
                buttons.PackStart(button, false, false, 0);
            }
            Button("Add", () => {
                Flush(); scripts.Add(new EmbeddedLuaScript { Name = "New script", Text = "-- Level script\n" });
                Refresh(scripts.Count - 1);
            });
            Button("Import .lua…", () => {
                Flush();
                using var chooser = new FileChooserDialog("Import Lua script", dialog, FileChooserAction.Open,
                    "Cancel", ResponseType.Cancel, "Import", ResponseType.Accept);
                var filter = new FileFilter { Name = "Lua scripts" }; filter.AddPattern("*.lua"); chooser.AddFilter(filter);
                var all = new FileFilter { Name = "All files" }; all.AddPattern("*"); chooser.AddFilter(all);
                if (chooser.Run() != (int)ResponseType.Accept) return;
                var script = new EmbeddedLuaScript { Name = Path.GetFileName(chooser.Filename),
                    Text = File.ReadAllText(chooser.Filename, new System.Text.UTF8Encoding(false, true)) };
                scripts.Add(script); Refresh(scripts.Count - 1);
            });
            Button("Export…", () => {
                if (selected < 0) return;
                Flush();
                using var chooser = new FileChooserDialog("Export Lua script", dialog, FileChooserAction.Save,
                    "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
                chooser.CurrentName = "level-script.lua"; chooser.DoOverwriteConfirmation = true;
                if (chooser.Run() == (int)ResponseType.Accept) File.WriteAllBytes(chooser.Filename, scripts[selected].Source);
            });
            Button("Remove", () => {
                if (selected < 0) return;
                if(PolygonLua.IsGenerated(scripts[selected]))throw new InvalidOperationException("Edit generated triggers using Select > Polygon Lua trigger.");
                int next = selected; scripts.RemoveAt(selected); Refresh(Math.Min(next, scripts.Count - 1));
            });
            void Move(int direction) {
                if (selected < 0 || selected + direction < 0 || selected + direction >= scripts.Count) return;
                Flush(); int target = selected + direction;
                var script = scripts[selected]; scripts.RemoveAt(selected); scripts.Insert(target, script); Refresh(target);
            }
            Button("↑", () => Move(-1)); Button("↓", () => Move(1));
            dialog.ContentArea.PackStart(contents, true, true, 0);
            Refresh(scripts.Count > 0 ? 0 : -1); dialog.ShowAll();
            while (dialog.Run() == (int)ResponseType.Accept) {
                try {
                    Flush();
                    byte[] bytes = scripts.Count == 0 ? null : EmbeddedLua.Write(scripts);
                    if (bytes == null ? original != null : original == null || !bytes.SequenceEqual(original)) {
                        editor.SetUndo("Edit level Lua scripts");
                        if (bytes == null) Level.Chunks.Remove(EmbeddedLua.Tag);
                        else Level.Chunks[EmbeddedLua.Tag] = bytes;
                        editor.Changed = true; Redraw();
                    }
                    break;
                } catch (Exception e) { Error(e); }
            }
        }
    }
}
