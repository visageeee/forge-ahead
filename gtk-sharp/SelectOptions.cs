using Gtk;
using System;

namespace Weland
{
    public partial class MapWindow
    {
        Frame selectOptions;

        void SetupSelectOptions(Box sidebar)
        {
            selectOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Select") { Xalign = 0 }, false, false, 0);
            var message = new Label("") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 };
            void Refresh(string reason = null) { message.Text = reason ?? ""; UpdateInspector(); UpdateStatusBar(); Redraw(); }
            var selectionRow = new Box(Orientation.Horizontal, 6);
            var all = new Button("Select all"); var none = new Button("Deselect all");
            all.Clicked += (sender, args) => { editor.SelectAllGeometry(); Refresh(); };
            none.Clicked += (sender, args) => { editor.DeselectAllGeometry(); Refresh(); };
            selectionRow.PackStart(all, true, true, 0); selectionRow.PackStart(none, true, true, 0);
            contents.PackStart(selectionRow, false, false, 0);
            contents.PackStart(new Label("Ctrl+A selects all geometry.\nCtrl+Shift+A clears the selection.") { Xalign = 0 }, false, false, 0);
            var clipboardRow = new Box(Orientation.Horizontal, 6);
            var copy = new Button("Copy"); var paste = new Button("Paste");
            copy.Clicked += (sender, args) => { editor.CopySelection(out string reason); Refresh(reason); };
            paste.Clicked += (sender, args) => { editor.BeginPaste(out string reason); Refresh(reason); };
            clipboardRow.PackStart(copy, true, true, 0); clipboardRow.PackStart(paste, true, true, 0);
            contents.PackStart(clipboardRow, false, false, 0);
            contents.PackStart(new Label("Ctrl+C copies; Ctrl+V starts paste.\nClick to place; Escape cancels.") { Xalign = 0 }, false, false, 0);
            var files=new Box(Orientation.Horizontal,6);
            var saveSelection=new Button("Export selected…");var loadSelection=new Button("Import from file…");
            void SelectionFile(bool save) {
                using(var dialog=new FileChooserDialog(save?"Export selected":"Import from file",window1,save?FileChooserAction.Save:FileChooserAction.Open,"Cancel",ResponseType.Cancel,save?"Save":"Load",ResponseType.Accept)) {
                    dialog.DoOverwriteConfirmation=save;if(save)dialog.CurrentName="Selection.sceA";
                    try {if(dialog.Run()==(int)ResponseType.Accept){string reason;if(save)editor.SaveSelectionFile(dialog.Filename,out reason);else editor.LoadSelectionFile(dialog.Filename,out reason);Refresh(reason);}}
                    catch(Exception ex){Refresh(ex.Message);}finally{dialog.Hide();}
                }
            }
            saveSelection.Clicked+=(o,e)=>SelectionFile(true);loadSelection.Clicked+=(o,e)=>SelectionFile(false);
            files.PackStart(saveSelection,true,true,0);files.PackStart(loadSelection,true,true,0);contents.PackStart(files,false,false,0);
            contents.PackStart(new Label("Pathfinder — A = lower polygon ID") {Xalign=0},false,false,0);
            var operations=new Box(Orientation.Horizontal,4);
            var join=new Button("Join");var ab=new Button("A − B");var ba=new Button("B − A");
            join.Clicked+=(o,e)=>{editor.PathfinderSelection(false,false,out string reason);Refresh(reason);};
            ab.Clicked+=(o,e)=>{editor.PathfinderSelection(true,false,out string reason);Refresh(reason);};
            ba.Clicked+=(o,e)=>{editor.PathfinderSelection(true,true,out string reason);Refresh(reason);};
            operations.PackStart(join,true,true,0);operations.PackStart(ab,true,true,0);operations.PackStart(ba,true,true,0);contents.PackStart(operations,false,false,0);
            var mirror = new Button("Mirror horizontal"); var flip = new Button("Flip vertical");
            mirror.Clicked += (sender, args) => { editor.MirrorSelectedGeometry(true, out string reason); Refresh(reason); };
            flip.Clicked += (sender, args) => { editor.MirrorSelectedGeometry(false, out string reason); Refresh(reason); };
            contents.PackStart(mirror, false, false, 0); contents.PackStart(flip, false, false, 0);
            contents.PackStart(new Label("Reflect around the selection centre.") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 }, false, false, 0);
            var heightRow = new Box(Orientation.Horizontal, 8);
            heightRow.PackStart(new Label("Shift height (WU)") { Xalign = 0 }, true, true, 0);
            var amount = new SpinButton(-64, 64, 0.1) { Numeric = true, Digits = 4, WidthChars = 7, Value = 0.25 };
            heightRow.PackStart(amount, false, false, 0); contents.PackStart(heightRow, false, false, 0);
            var floor = new CheckButton("Floor") { Active = true }; var ceiling = new CheckButton("Ceiling") { Active = true };
            var targets = new Box(Orientation.Horizontal, 8);
            targets.PackStart(floor, true, true, 0); targets.PackStart(ceiling, true, true, 0); contents.PackStart(targets, false, false, 0);
            var apply = new Button("Shift height");
            apply.Clicked += (sender, args) => {
                editor.ShiftSelectedHeights((int)Math.Round(amount.Value * 1024), floor.Active, ceiling.Active, out string reason); Refresh(reason);
            };
            contents.PackStart(apply, false, false, 0);
            contents.PackStart(new Label("Positive raises; negative lowers.\nUses selected polygons, or polygons\ntouching selected vertices/lines.") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 }, false, false, 0);
            var lua=new Button("Polygon Lua trigger…");lua.Clicked+=OnPolygonLua;
            contents.PackStart(lua,false,false,0);
            contents.PackStart(message, false, false, 0);
            selectOptions.Add(contents); contents.ShowAll(); sidebar.PackStart(selectOptions, false, false, 0);
        }
        Frame lightingOptions;
        void SetupLightingOptions(Box sidebar)
        {
            var message=new Label("") {Xalign=0,LineWrap=true,MaxWidthChars=28,Selectable=true};
            void Refresh(string reason=null) {message.Text=reason??"";editor.BakeResultMessage=reason;UpdateInspector();UpdateStatusBar();Redraw();}
            lightingOptions = new Frame("Bake lighting") { NoShowAll=true, Margin=6, Valign=Align.Start };
            var lightOptions = new Box(Orientation.Vertical, 6) { Margin = 6 };
            SpinButton LightNumber(string label,double min,double max,double step,double value,int digits) {
                var row=new Box(Orientation.Horizontal,6);row.PackStart(new Label(label){Xalign=0},true,true,0);
                var field=new SpinButton(min,max,step){Value=value,Digits=(uint)digits,Numeric=true,WidthChars=6};
                field.ValueChanged+=(o,e)=>{editor.UpdateLightingPreview();Redraw();};row.PackStart(field,false,false,0);lightOptions.PackStart(row,false,false,0);return field;
            }
            lightOptions.PackStart(new Label("Click and drag to move the light live.") {Xalign=0,LineWrap=true,MaxWidthChars=28},false,false,0);
            var wholeMap=new CheckButton("Whole map") {Active=editor.BakeWholeMap};
            wholeMap.Toggled+=(o,e)=>{editor.BakeWholeMap=wholeMap.Active;editor.UpdateLightingPreview();Refresh(wholeMap.Active ? "Lighting all map polygons." : "Lighting selected polygons only.");};
            lightOptions.PackStart(wholeMap,false,false,0);
            var rebuild=new CheckButton("Rebuild lighting geometry") {Active=editor.RebuildLightingGeometry};
            rebuild.Toggled+=(o,e)=>{editor.RebuildLightingGeometry=rebuild.Active;Refresh("Rebuild discards compatible internal geometry and reconstructs lit/shadowed regions. Polygon IDs may change.");};
            lightOptions.PackStart(rebuild,false,false,0);
            var coarse=new CheckButton("Allow coarse approximation") {Active=editor.AllowCoarseRebuild,Sensitive=rebuild.Active};
            coarse.Toggled+=(o,e)=>editor.AllowCoarseRebuild=coarse.Active;
            rebuild.Toggled+=(o,e)=>coarse.Sensitive=rebuild.Active;
            lightOptions.PackStart(coarse,false,false,0);
            var lightHeight=LightNumber("Absolute height (WU)",-32,32,.125,.75,3);
            lightHeight.ValueChanged+=(o,e)=>{editor.BakePreviewHeight=lightHeight.Value*1024;editor.UpdateLightingPreview();Redraw();};
            var brightness=LightNumber("Lit floor (%)",0,100,5,100,0);
            var darkness=LightNumber("Shadow floor (%)",0,100,5,25,0);
            var limit=LightNumber("Polygon budget",1,32767,10,256,0);
            var minimum=LightNumber("Min. area (map units²)",1,65536,16,64,0);
            var previewLight=new Button("Preview shadows");var bakeLight=new Button("Bake lighting");
            previewLight.Clicked+=(o,e)=>{editor.BakeSelectedLighting(lightHeight.Value*1024,brightness.Value/100,darkness.Value/100,limit.ValueAsInt,minimum.Value,false,out string reason);Refresh(reason);};
            bakeLight.Clicked+=(o,e)=>{
                bakeLight.Sensitive=false;
                try {
                    bool success=editor.BakeSelectedLighting(lightHeight.Value*1024,brightness.Value/100,darkness.Value/100,limit.ValueAsInt,minimum.Value,true,out string reason);
                    editor.UpdateLightingPreview();Refresh(success ? reason : "Bake failed: "+(reason??"No reason was returned."));
                } catch(Exception ex) {Refresh("Bake failed: "+ex.Message);}
                finally {bakeLight.Sensitive=true;}
            };
            lightOptions.PackStart(previewLight,false,false,0);lightOptions.PackStart(bakeLight,false,false,0);
            lightOptions.PackStart(message,false,false,0);
            var clearLight=new Button("Clear preview");clearLight.Clicked+=(o,e)=>{editor.PlacingBakeLight=false;editor.ClearLightingPreview();Refresh();};lightOptions.PackStart(clearLight,false,false,0);
            lightOptions.PackStart(new Label("Preview is independent of bake limits.\nBaked shadows may be coarser.\nFloors, ceilings and walls in scope.\nRebake replaces the last unchanged bake.\nCeilings use sampled brightness.\nPreview shows floors; no distance falloff."){Xalign=0,LineWrap=true,MaxWidthChars=28},false,false,0);
            lightingOptions.Add(lightOptions);lightOptions.ShowAll();sidebar.PackStart(lightingOptions,false,false,0);
        }
    }
}
