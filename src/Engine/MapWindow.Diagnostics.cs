using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZiloreMiniMap {

    public sealed partial class MapWindow {

        public void CheckZoneEditor(string path) {
            using(ZoneEditor editor=new ZoneEditor(map,new List<MapZone>(),zonesPath,value=>{}))editor.CheckAndPreview(path);
        }

        public static void SelfTest() {



            var arrivalTarget=new MapZone { Name="Destination",Points=new[]{new PointF(.5f,.5f)} };



            if(!DestinationReached(arrivalTarget,new PointF(.5f,.5f))



                || !DestinationReached(arrivalTarget,new PointF(.501f,.5f))



                || DestinationReached(arrivalTarget,new PointF(.503f,.5f))



                || DestinationReached(null,new PointF(.5f,.5f))) throw new Exception("Waypoint arrival radius failed.");



            if(Native.ClassifyCopy(true,"")!=CopyResult.Sent



                || Native.ClassifyCopy(false,"")!=CopyResult.Cancelled



                || Native.ClassifyCopy(false,"SendInput failed")!=CopyResult.Failed) throw new Exception("Copy cancellation must not trigger failure backoff.");



            var chord=new List<string>();



            Func<uint,bool,bool> record=(key,up)=> { chord.Add(key+":"+up); return true; };



            Func<int,Task> noDelay=ms=>Task.FromResult(0);



            if(!Native.CopyChord(record,noDelay,()=>true,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False,67:False,67:True,162:True") throw new Exception("Copy chord ordering failed.");



            chord.Clear();



            if(Native.CopyChord(record,noDelay,()=>false,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False,162:True") throw new Exception("Cancelled copy emitted C.");



            chord.Clear();



            bool failedRelease=false;



            if(Native.CopyChord((key,up)=> { record(key,up); if(key==67 && up && !failedRelease) { failedRelease=true; return false; } return true; },noDelay,()=>true,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False,67:False,67:True,67:True,162:True") throw new Exception("Failed C release was not retried before Control release.");



            chord.Clear();



            if(Native.CopyChord((key,up)=> { record(key,up); return key!=162 || up; },noDelay,()=>true,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False") throw new Exception("Failed Control press emitted C.");



            chord.Clear();



            if(Native.CopyChord((key,up)=> { record(key,up); return key!=67 || up; },noDelay,()=>true,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False,67:False,162:True") throw new Exception("Failed C press did not release Control.");



            chord.Clear();



            bool ctrlReleaseFailed=false;



            if(Native.CopyChord((key,up)=> { record(key,up); if(key==162 && up && !ctrlReleaseFailed) { ctrlReleaseFailed=true; return false; } return true; },noDelay,()=>true,0xA2,0x43).GetAwaiter().GetResult()



                || string.Join(",",chord)!="162:False,67:False,67:True,162:True,162:True") throw new Exception("Failed Control release was not retried.");



            chord.Clear();



            try {



                Native.CopyChord(record,ms=> { if(ms==30)throw new InvalidOperationException("Simulated interruption"); return Task.FromResult(0); },()=>true,0xA2,0x43).GetAwaiter().GetResult();



                throw new Exception("Simulated input interruption was ignored.");



            } catch(InvalidOperationException) {



            }



            chord.Clear();



            if(!Native.CopyChord(record,noDelay,()=>true,0,67).GetAwaiter().GetResult()



                || string.Join(",",chord)!="67:False,67:True") throw new Exception("Single key copy chord ordering failed.");



            chord.Clear();



            if(Native.CopyChord(record,noDelay,()=>false,0,67).GetAwaiter().GetResult()



                || chord.Count!=0) throw new Exception("Cancelled single key copy emitted key.");



            chord.Clear();



            bool singleReleaseFailed=false;



            if(Native.CopyChord((key,up)=> { record(key,up); if(key==67 && up && !singleReleaseFailed) { singleReleaseFailed=true; return false; } return true; },noDelay,()=>true,0,67).GetAwaiter().GetResult()



                || string.Join(",",chord)!="67:False,67:True,67:True") throw new Exception("Failed single key release was not retried.");



            ZoneStore.SelfTest();
            DeathBannerDetector.SelfTest();



            DestinationSearch.SelfTest();



            Localization.SelfTest();



            MapMotion.SelfTest();



            ScumMapStore.SelfTest();
            CoreSelfTests.RunAll();



            if(ReadCopyInterval(1,true)!=1000 || ReadCopyInterval(3,true)!=3000 || ReadCopyInterval(0,false)!=Program.MinimumCopyIntervalMs || ReadCopyInterval(500,false)!=Math.Max(500,Program.MinimumCopyIntervalMs) || ReadCopyInterval(int.MaxValue,false)!=10000)throw new Exception("Sampling interval migration failed.");



            if(!GameFocusReturn.ShouldRestore(true,10,10,20,20)



                || GameFocusReturn.ShouldRestore(true,10,10,30,20)



                || GameFocusReturn.ShouldRestore(false,10,10,20,20)



                || GameFocusReturn.ShouldRestore(true,11,10,20,20)) throw new Exception("Focus return policy failed.");



            foreach(char row in "DCBAZ") for(int col=0;col<5;col++) {



                MapZone target=GridTarget(row.ToString()+col);



                PointF point=target.Centroid;



                if(point.X<=0 || point.X>=1 || point.Y<=0 || point.Y>=1) throw new Exception("Grid target outside map.");



                if(Math.Abs(point.X-(4-col+0.5)/5)>0.002 || Math.Abs(point.Y-("DCBAZ".IndexOf(row)+0.5)/5)>0.002) throw new Exception("Grid orientation incorrect.");



            }



            if(GridTarget(" c 3 ")==null || GridTarget("E3")!=null || GridTarget("C5")!=null || GridTarget("C33")!=null) throw new Exception("Grid validation failed.");



            MapZone first=new MapZone { Name="Town",Points=new[]{new PointF(.2f,.3f)} };



            MapZone second=new MapZone { Name="Town",Points=new[]{new PointF(.7f,.8f)} };



            List<MapZone> found=FindDestinations(new List<MapZone>{first,second}," tOw ");



            if(found.Count!=2 || !object.ReferenceEquals(found[1],second)) throw new Exception("Place search lost duplicate identity.");



            if(FindDestinations(new List<MapZone>(),"Z0").Count!=1 || FindDestinations(new List<MapZone>{first},"missing").Count!=0) throw new Exception("Empty search handling failed.");



            ChatState state=new ChatState();



            PhysicalKeyTransitions transitions=new PhysicalKeyTransitions();



            bool previousCopy=Native.CopyInProgress;



            try {



                Native.CopyInProgress=true;



                if(transitions.ShortcutModifiersDown(false)) throw new Exception("Sampler blocked physical map input.");



                transitions.Update(0xA0,true);



                if(transitions.ShortcutModifiersDown(false) || !transitions.ShortcutModifiersDown(true))



                    throw new Exception("Sprint modifier policy failed.");



                transitions.Update(0xA2,true);



                if(!transitions.ShortcutModifiersDown(false)) throw new Exception("Physical Ctrl not detected.");



                transitions.Update(0xA2,false);



                transitions.Update(0xA0,false);



                if(transitions.ShortcutModifiersDown(true)) throw new Exception("Released modifiers remained active.");



            } finally { Native.CopyInProgress=previousCopy; }



            if(!transitions.Update(0x4D,true)) throw new Exception("First map press lost.");



            for(int repeat=0;repeat<100;repeat++)



                if(transitions.Update(0x4D,true)) throw new Exception("Held map key repeated.");



            if(transitions.Update(0x4D,false) || !transitions.Update(0x4D,true))



                throw new Exception("Map key did not rearm on release.");



            if(transitions.Update(-1,true) || transitions.Update(256,true)) throw new Exception("Invalid key accepted.");



            state.Key(0x54); state.Key(0x54);



            if(!state.Paused)throw new Exception("T must pause without toggling.");



            state.Key(0x4D); if(!state.Paused)throw new Exception("Map key cleared chat gate.");



            state.Key(0xBF); if(!state.Paused)throw new Exception("Typing must preserve chat pause.");



            state.Key(0x09); if(!state.Paused)throw new Exception("Tab while chat open must preserve chat pause.");



            state.Key(0x4D); if(!state.Paused)throw new Exception("Typing after Tab must preserve chat pause.");



            state.Key(0x0D); if(!state.Paused)throw new Exception("Enter submission must preserve chat pause.");
            state.Key(0x1B); if(state.Paused)throw new Exception("Escape must resume.");



            state.Key(0x09); if(state.Paused)throw new Exception("Tab while chat closed must not open chat.");



            state.Key(0x54); state.Key(0x1B); if(state.Paused)throw new Exception("Escape must resume.");



            Position p=Position.Parse("{X=-336602.375 Y=-270302.625 Z=18853.264|P=-3.814117 Y=13.952554 R=0.000000}");



            if(p==null || p.Y!=-270302.625 || p.Yaw!=13.952554) throw new Exception("Coordinate/rotation parsing failed.");



            if(Position.Parse("hello")!=null || Position.Parse("prefix {X=1 Y=2 Z=3|P=0 Y=0 R=0}")!=null) throw new Exception("Invalid clipboard text accepted.");



            PointF corner=ToMap(new Position { X=617718,Y=618618 });



            PointF end=ToMap(new Position { X=-903900,Y=-905000 });



            if(corner.X!=0 || corner.Y!=0 || Math.Abs(end.X-1)>.00001 || Math.Abs(end.Y-1)>.00001) throw new Exception("Map projection failed.");



            if(RoadRouter.Instance.IsLoaded) {



                var testRoute=RoadRouter.Instance.FindRoute(new PointF(0.62767f,0.63618f),new PointF(0.67270f,0.03878f));



                if(!testRoute.Success || testRoute.Polyline==null || testRoute.Polyline.Length<10) {



                    throw new Exception("Road route self-test failed.");



                }



            }



        }

    }

}
