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

namespace Weland {
 public partial class MapWindow {
    Frame cameraOptions;
    void SetupCameraOptions(Box sidebar) {
        cameraOptions=new Frame("Camera paths"){NoShowAll=true,Margin=6};
        var box=new Box(Orientation.Vertical,6){Margin=8};cameraOptions.Add(box);
        var cameraPage=new Box(Orientation.Vertical,6){Margin=6};
        var captionPage=new Box(Orientation.Vertical,6){Margin=6};
        var tabs=new Notebook();
        void AddPage(Box contents,string label) {
            var scroll=new ScrolledWindow{HeightRequest=360};scroll.SetPolicy(PolicyType.Never,PolicyType.Automatic);
            scroll.AddWithViewport(contents);tabs.AppendPage(scroll,new Label(label));
        }
        AddPage(cameraPage,"Waypoints");AddPage(captionPage,"Captions");
        var paths=new ComboBoxText();var name=new Entry();var points=new ComboBoxText();
        var message=new Label(""){Xalign=0,LineWrap=true,MaxWidthChars=28};
        box.PackStart(paths,false,false,0);box.PackStart(name,false,false,0);
        var buttons=new Box(Orientation.Horizontal,4);var add=new Button("New");var rename=new Button("Rename");var remove=new Button("Delete path");
        foreach(var b in new[]{add,rename,remove})buttons.PackStart(b,true,true,0);box.PackStart(buttons,false,false,0);
        box.PackStart(new Label("Left-click selects; left-drag moves.\nRight-click adds a waypoint.\nHeights are absolute world units."){Xalign=0},false,false,0);
        box.PackStart(points,false,false,0);
        box.PackStart(tabs,true,true,0);
        SpinButton Field(string title,double min,double max,double step,int digits,Box target=null) {
            var row=new Box(Orientation.Horizontal,4);row.PackStart(new Label(title){Xalign=0},true,true,0);
            var input=new SpinButton(min,max,step){Numeric=true,Digits=(uint)digits,WidthChars=7};row.PackStart(input,false,false,0);(target??cameraPage).PackStart(row,false,false,0);return input;
        }
        Gtk.Scale Angle(string title,double min,double max) {
            cameraPage.PackStart(new Label(title){Xalign=0},false,false,0);
            var slider=new Gtk.Scale(Orientation.Horizontal,min,max,1){Digits=0,DrawValue=true};
            cameraPage.PackStart(slider,false,false,0);return slider;
        }
        var z=Field("Height",-32,31.999,0.125,3);var yaw=Angle("Facing (degrees)",0,359);var pitch=Angle("Pitch (degrees)",-90,90);var seconds=Field("Seconds to next / final hold",1.0/30,3600,0.1,3);
        captionPage.PackStart(new Label("Text shown on arrival at this waypoint"){Xalign=0,LineWrap=true},false,false,0);
        var title=new Entry{MaxLength=240,TooltipText="Enter a title or caption for the selected waypoint"};captionPage.PackStart(title,false,false,0);
        var titleSeconds=Field("Caption seconds",1.0/30,120,0.1,3,captionPage);
        var titleX=Field("Caption centre X (%)",0,100,1,0,captionPage);var titleY=Field("Caption centre Y (%)",0,100,1,0,captionPage);
        var titleSize=Field("Caption size (%)",50,400,5,0,captionPage);
        var colorRow=new Box(Orientation.Horizontal,6);colorRow.PackStart(new Label("Caption color"){Xalign=0},true,true,0);
        var titleColor=new ColorButton();colorRow.PackStart(titleColor,false,false,0);captionPage.PackStart(colorRow,false,false,0);
        var fadeIn=Field("Fade in (seconds)",0,120,0.1,2,captionPage);var fadeOut=Field("Fade out (seconds)",0,120,0.1,2,captionPage);
        captionPage.PackStart(new Label("Position, size, RGB and fades require the\nSprintathon styled-caption patch. Older\nengines show ordinary HUD text.\nCaption duration includes the fades."){Xalign=0,LineWrap=true,MaxWidthChars=28},false,false,0);
        var order=new Box(Orientation.Horizontal,4);var up=new Button("Earlier");var down=new Button("Later");var delete=new Button("Delete point");foreach(var b in new[]{up,down,delete})order.PackStart(b,true,true,0);cameraPage.PackStart(order,false,false,0);
        cameraPage.PackStart(new Label("Play using the Camera Lua preset.\nPlayback returns to the player.\nSegments are straight; avoid walls."){Xalign=0,LineWrap=true},false,false,0);
        var check=new Button("Check path");
        check.Clicked+=(o,e)=>{var data=editor.GetCameraPaths();if(editor.CameraPathIndex>=0&&editor.CameraPathIndex<data.Count)message.Text=CameraPaths.Warning(editor.Level,data[editor.CameraPathIndex]);};
        cameraPage.PackStart(check,false,false,0);
        box.PackStart(message,false,false,0);
        bool refreshing=false,editingValue=false;
        void Refresh() {
            if(editingValue){Redraw();return;}
            refreshing=true;
            var data=editor.GetCameraPaths();paths.RemoveAll();foreach(var p in data)paths.AppendText(p.Name);
            if(editor.CameraPathIndex>=data.Count)editor.CameraPathIndex=data.Count-1;
            paths.Active=editor.CameraPathIndex;points.RemoveAll();name.Text="";
            bool valid=editor.CameraPathIndex>=0&&editor.CameraPathIndex<data.Count;
            if(valid){var p=data[editor.CameraPathIndex];name.Text=p.Name;for(int i=0;i<p.Points.Count;i++)points.AppendText("Waypoint "+(i+1));if(editor.CameraPointIndex>=p.Points.Count)editor.CameraPointIndex=p.Points.Count-1;points.Active=editor.CameraPointIndex;valid=editor.CameraPointIndex>=0;
                if(valid){var v=p.Points[editor.CameraPointIndex];z.Value=v.Z/1024.0;yaw.Value=v.Yaw;pitch.Value=v.Pitch;seconds.Value=v.Ticks/30.0;title.Text=v.Title;titleSeconds.Value=v.TitleTicks/30.0;
                    titleX.Value=v.TitleX;titleY.Value=v.TitleY;titleSize.Value=v.TitleSize;
                    titleColor.Rgba=new Gdk.RGBA{Red=v.TitleRed/255.0,Green=v.TitleGreen/255.0,Blue=v.TitleBlue/255.0,Alpha=1};
                    fadeIn.Value=v.TitleFadeIn/30.0;fadeOut.Value=v.TitleFadeOut/30.0;}
            }
            foreach(var w in new Widget[]{z,yaw,pitch,seconds,title,titleSeconds,titleX,titleY,titleSize,titleColor,fadeIn,fadeOut,up,down,delete})w.Sensitive=valid;
            message.Text=editor.CameraMessage;
            if(string.IsNullOrEmpty(message.Text))message.Text="Use Check path to validate segments before playback.";
            refreshing=false;Redraw();
        }
        void Change(string label,System.Action<System.Collections.Generic.List<CameraPath>> action){try{var data=editor.GetCameraPaths();action(data);editor.StoreCameraPaths(data,label);UpdateStatusBar();}catch(Exception ex){message.Text=ex.Message;}}
        paths.Changed+=(o,e)=>{if(refreshing)return;editor.CameraPathIndex=paths.Active;editor.CameraPointIndex=-1;Refresh();};
        points.Changed+=(o,e)=>{if(refreshing)return;editor.CameraPointIndex=points.Active;Refresh();};
        add.Clicked+=(o,e)=>Change("Create camera path",data=>{int n=1;while(data.Exists(p=>p.Name=="Camera "+n))n++;data.Add(new CameraPath{Name="Camera "+n});editor.CameraPathIndex=data.Count-1;editor.CameraPointIndex=-1;});
        rename.Clicked+=(o,e)=>{string title=name.Text;Change("Rename camera path",data=>{if(editor.CameraPathIndex>=0)data[editor.CameraPathIndex].Name=title;});};
        remove.Clicked+=(o,e)=>Change("Delete camera path",data=>{if(editor.CameraPathIndex>=0)data.RemoveAt(editor.CameraPathIndex);editor.CameraPointIndex=-1;});
        void UpdateValue(System.Action<CameraWaypoint> edit) {
            if(refreshing)return;
            var data=editor.GetCameraPaths();int pi=editor.CameraPathIndex,wi=editor.CameraPointIndex;
            if(pi<0||pi>=data.Count||wi<0||wi>=data[pi].Points.Count)return;
            edit(data[pi].Points[wi]);editingValue=true;
            try{editor.StoreCameraPaths(data,"Edit camera waypoint");message.Text="";}
            catch(Exception ex){message.Text=ex.Message;}
            finally{editingValue=false;}
        }
        z.ValueChanged+=(o,e)=>UpdateValue(v=>v.Z=(short)Math.Round(z.Value*1024));
        yaw.ValueChanged+=(o,e)=>UpdateValue(v=>v.Yaw=(short)Math.Round(yaw.Value));
        pitch.ValueChanged+=(o,e)=>UpdateValue(v=>v.Pitch=(short)Math.Round(pitch.Value));
        seconds.ValueChanged+=(o,e)=>UpdateValue(v=>v.Ticks=Math.Max(1,(int)Math.Round(seconds.Value*30)));
        title.Changed+=(o,e)=>UpdateValue(v=>v.Title=title.Text);
        titleSeconds.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleTicks=Math.Max(1,(int)Math.Round(titleSeconds.Value*30)));
        titleX.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleX=titleX.ValueAsInt);
        titleY.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleY=titleY.ValueAsInt);
        titleSize.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleSize=titleSize.ValueAsInt);
        titleColor.ColorSet+=(o,e)=>UpdateValue(v=>{var c=titleColor.Rgba;v.TitleRed=(int)Math.Round(c.Red*255);v.TitleGreen=(int)Math.Round(c.Green*255);v.TitleBlue=(int)Math.Round(c.Blue*255);});
        fadeIn.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleFadeIn=(int)Math.Round(fadeIn.Value*30));
        fadeOut.ValueChanged+=(o,e)=>UpdateValue(v=>v.TitleFadeOut=(int)Math.Round(fadeOut.Value*30));
        void Reorder(int delta){Change("Reorder camera waypoint",data=>{var p=data[editor.CameraPathIndex].Points;int i=editor.CameraPointIndex,j=i+delta;if(j<0||j>=p.Count)return;var v=p[i];p[i]=p[j];p[j]=v;editor.CameraPointIndex=j;});}
        up.Clicked+=(o,e)=>Reorder(-1);down.Clicked+=(o,e)=>Reorder(1);
        delete.Clicked+=(o,e)=>Change("Delete camera waypoint",data=>data[editor.CameraPathIndex].Points.RemoveAt(editor.CameraPointIndex));
        editor.CameraUpdated=Refresh;cameraOptions.Shown+=(o,e)=>Refresh();box.ShowAll();sidebar.PackStart(cameraOptions,false,false,0);
    }
 }
}
