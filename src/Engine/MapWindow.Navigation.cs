using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    public sealed partial class MapWindow {
        [GeneratedRegex(@"\b(grid|sector|square)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RxGridKeywords();
        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex RxGridWhitespace();
        [GeneratedRegex(@"^[DCBAZ][0-4]$", RegexOptions.CultureInvariant)] private static partial Regex RxGridCoord();
        static MapZone GridTarget(string query) {

            query=RxGridKeywords().Replace(query??"","");

            string grid=RxGridWhitespace().Replace((query??"").Trim().ToUpperInvariant(), "");

            if(!RxGridCoord().IsMatch(grid)) return null;

            int row="DCBAZ".IndexOf(grid[0]), col=4-(grid[1]-'0');

            double x=617505-(col+0.5)*304132, y=617953-(row+0.5)*304356;

            return new MapZone { Name="Grid "+grid, Points=new PointF[]{ToMap(new Position { X=x,Y=y })} };

        }

        static List<MapZone> FindDestinations(List<MapZone> places,string query) {

            List<MapZone> matches=DestinationSearch.Find(places,query,null);

            MapZone grid=GridTarget(query); if(grid!=null) matches.Insert(0,grid);

            return matches;

        }

        bool searchOpen { get { return false; } }
        void ShowZoneSearch() {
            if(WpfFieldConsoleRuntime.Active!=null) WpfFieldConsoleRuntime.Active.Handle(AppControlAction.Search);
        }

        string GetLocationDescription() {

            // OverlayKey runs on every timer tick, including the smooth-motion frames that do

            // not change the sampled position.  Avoid rescanning/sorting all zones twice per

            // presented frame until an input that can change this label actually changes.

            if(cachedLocationDescription!=null &&

                ReferenceEquals(cachedLocationPosition,position) &&

                ReferenceEquals(cachedLocationTarget,searchTarget) &&

                ReferenceEquals(cachedLocationRoute,activeRoute) &&

                cachedLocationDescriptionRevision==locationDescriptionRevision) {

                return cachedLocationDescription;

            }

            string description=BuildLocationDescription();
            if(searchTarget!=null && activeRoute!=null && !activeRoute.Success) description+=" | "+RouteUnavailableText;

            cachedLocationDescription=description;

            cachedLocationPosition=position;

            cachedLocationTarget=searchTarget;

            cachedLocationRoute=activeRoute;

            cachedLocationDescriptionRevision=locationDescriptionRevision;

            return description;

        }

                string BuildLocationDescription() {
            if (position == null) return Localization.Get("AwaitingPosition");
            PointF p = ToMap(position);
            string sector = DestinationSearch.Sector(p);
            string result = sector;
            bool foundSpecific = false;

            List<LocationAnchor> anchors = GetLocationAnchors();

            // 1. Inside an anchor / zone
            foreach (LocationAnchor a in anchors) {
                if (a.Contains(p)) {
                    if (a.Zone != null) {
                        lastVisitedZone = a.Zone;
                    }
                    lastVisitedAnchorName = a.Name;
                    lastVisitedAnchorPoint = a.Point;
                    lastVisitedTime = DateTime.UtcNow;
                    result = sector + " - " + a.DisplayName;
                    foundSpecific = true;
                    break;
                }
            }

            if (!foundSpecific && anchors.Count > 0) {
                // 2. Single-pass O(N) scan (zero allocations) for closest, second-closest, and approaching anchor
                bool checkMotion = false;
                PointF prev = PointF.Empty;
                if (previousMapPoint.HasValue) {
                    prev = previousMapPoint.Value;
                    double moveVx = p.X - prev.X, moveVy = p.Y - prev.Y;
                    checkMotion = (moveVx * moveVx + moveVy * moveVy) > (0.0003 * 0.0003);
                }

                LocationAnchor closest = default(LocationAnchor), second = default(LocationAnchor), approaching = default(LocationAnchor);
                double closestDist = double.MaxValue, secondDist = double.MaxValue, approachingDist = double.MaxValue;
                bool hasSecond = false, hasApproaching = false;

                for (int i = 0; i < anchors.Count; i++) {
                    LocationAnchor a = anchors[i];
                    double dist = a.DistanceTo(p);
                    if (dist < closestDist) {
                        if (closestDist < double.MaxValue) { second = closest; secondDist = closestDist; hasSecond = true; }
                        closest = a; closestDist = dist;
                    } else if (dist < secondDist) {
                        second = a; secondDist = dist; hasSecond = true;
                    }
                    if (checkMotion && dist <= 0.075 && dist < approachingDist) {
                        double prevD = a.DistanceTo(prev);
                        if (dist < prevD && (prevD - dist) > 0.0001) {
                            approaching = a; approachingDist = dist; hasApproaching = true;
                        }
                    }
                }

                // 3. Motion & heading analysis
                if (checkMotion) {
                    if (!string.IsNullOrEmpty(lastVisitedAnchorName) && (DateTime.UtcNow - lastVisitedTime).TotalMinutes < 15) {
                        double dxLast = lastVisitedAnchorPoint.X - p.X, dyLast = lastVisitedAnchorPoint.Y - p.Y;
                        double dToLast = Math.Sqrt(dxLast * dxLast + dyLast * dyLast);
                        double dxPrev = lastVisitedAnchorPoint.X - prev.X, dyPrev = lastVisitedAnchorPoint.Y - prev.Y;
                        double prevDToLast = Math.Sqrt(dxPrev * dxPrev + dyPrev * dyPrev);

                        if (dToLast > prevDToLast && dToLast < 0.055) {
                            LocationAnchor targetAnchor = (closest.Name == lastVisitedAnchorName && hasSecond) ? second : closest;
                            double targetDist = targetAnchor.DistanceTo(p);
                            double prevTargetDist = targetAnchor.DistanceTo(prev);

                            if (targetDist < prevTargetDist && targetDist < 0.075) {
                                result = Localization.T("LocLeavingApproaching", sector, Localization.GetZoneName(lastVisitedAnchorName), targetAnchor.DisplayName);
                                foundSpecific = true;
                            } else {
                                result = Localization.T("LocLeaving", sector, Localization.GetZoneName(lastVisitedAnchorName));
                                foundSpecific = true;
                            }
                        }
                    }

                    if (!foundSpecific && hasApproaching) {
                        if (!string.IsNullOrEmpty(lastVisitedAnchorName) && approaching.Name != lastVisitedAnchorName &&
                            (DateTime.UtcNow - lastVisitedTime).TotalMinutes < 10) {
                            double dxL = lastVisitedAnchorPoint.X - p.X, dyL = lastVisitedAnchorPoint.Y - p.Y;
                            if ((dxL * dxL + dyL * dyL) < (0.06 * 0.06)) {
                                result = Localization.T("LocLeavingApproaching", sector, Localization.GetZoneName(lastVisitedAnchorName), approaching.DisplayName);
                                foundSpecific = true;
                            }
                        }
                        if (!foundSpecific) {
                            result = Localization.T("LocApproaching", sector, approaching.DisplayName);
                            foundSpecific = true;
                        }
                    }
                }

                // 4. Proximity & Between
                if (!foundSpecific) {
                    if (closestDist < 0.035) {
                        result = Localization.T("LocNear", sector, closest.DisplayName);
                    } else if (closestDist < 0.08 && hasSecond && secondDist < 0.08 && Math.Abs(closestDist - secondDist) < 0.035) {
                        result = Localization.T("LocBetween", sector, closest.DisplayName, second.DisplayName);
                    } else if (closestDist < 0.08) {
                        result = Localization.T("LocNear", sector, closest.DisplayName);
                    }
                }
            }

            // 5. Trip information (Destination, distance, approx time / ETA per transport mode)
            if (searchTarget != null) {
                string targetName = searchTarget.DisplayName;
                bool isAir = WpfMapSurface.AviationModeEnabled;
                bool isWater = !isAir && RoadRouter.Instance.IsWaterAt(p);
                double distM;
                string distSuffix = "";
                bool useRoad = !isAir && !isWater && activeRoute != null && activeRoute.Success;
                if (useRoad) {
                    distM = RoadRouter.RemainingDistanceMeters(p, activeRoute);
                    distSuffix = Localization.Get("RoadSuffix");
                } else {
                    PointF targetPt = searchTarget.Centroid;
                    double dxM = (targetPt.X - p.X) * 1521618 / 100.0;
                    double dyM = (targetPt.Y - p.Y) * 1523618 / 100.0;
                    distM = Math.Sqrt(dxM * dxM + dyM * dyM);
                    if (isAir) distSuffix = " ✈";
                    else if (isWater) distSuffix = " ⛵";
                }
                string distStr = distM >= 1000 ? string.Format(CultureInfo.InvariantCulture, "{0:F1} km{1}", distM / 1000.0, distSuffix) : ((int)Math.Round(distM)) + "m" + distSuffix;
                double fallbackSpeed = isAir ? 44.4 : (isWater ? 11.1 : (useRoad ? 13.9 : 4.2));
                double safeSpd = (double.IsNaN(filteredSpeed) || double.IsInfinity(filteredSpeed)) ? 0.0 : filteredSpeed;
                double speed = safeSpd > 1.2 ? safeSpd : fallbackSpeed;
                double scaledRouteSec = (useRoad && activeRoute.TotalDistanceMeters > 1.0 && activeRoute.EstimatedSeconds > 1.0)
                    ? activeRoute.EstimatedSeconds * Math.Min(1.25, distM / activeRoute.TotalDistanceMeters)
                    : (useRoad ? activeRoute.EstimatedSeconds : 0.0);
                double etaSec = (useRoad && safeSpd <= 1.2 && scaledRouteSec > 1.0)
                    ? scaledRouteSec
                    : distM / Math.Max(0.5, speed);
                if (double.IsNaN(etaSec) || double.IsInfinity(etaSec) || etaSec < 0) etaSec = 0;
                string etaStr;
                if (etaSec < 60) etaStr = "<1m";
                else if (etaSec < 3600) etaStr = string.Format(CultureInfo.InvariantCulture, "{0}m", (int)Math.Round(etaSec / 60.0));
                else {
                    int h = (int)(etaSec / 3600);
                    int m = (int)((etaSec % 3600) / 60.0);
                    etaStr = string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", h, m);
                }
                result += string.Format(CultureInfo.InvariantCulture, "  |  -> {0}: {1} (~{2})", targetName, distStr, etaStr);
            }

            return result;
        }

        DateTime lastAutoRerouteUtc = DateTime.MinValue;
        void UpdateRouteAsync(PointF playerPt, MapZone target) { RequestRouteAsync(playerPt,target,false); }
        bool RequestRouteAsync(PointF playerPt, MapZone target,bool force) {
            if(closing || IsDisposed) return false;
            if(target==null) { activeRoute=null; routeGeneration++; lastRouteTarget=null; wpfQueuedTarget=null; return false; }
            if(!RoadRouter.Instance.IsLoaded) { activeRoute=null; routeGeneration++; lastRouteTarget=null; wpfQueuedTarget=target; return false; }
            bool targetChanged=!Object.ReferenceEquals(lastRouteTarget,target);
            if(targetChanged) { activeRoute=null; routeGeneration++; lastAutoRerouteUtc=DateTime.MinValue; }
            // Aircraft and boats on water navigate directly — skip road A* to save CPU and avoid shore-snapping.
            if(WpfMapSurface.AviationModeEnabled || RoadRouter.Instance.IsWaterAt(playerPt)) {
                activeRoute=null; lastRouteTarget=target; lastRoutePlayerPt=playerPt; return false;
            }
            if(!force && !targetChanged && activeRoute!=null && activeRoute.Success) {
                DateTime nowUtc = DateTime.UtcNow;
                if((nowUtc - lastAutoRerouteUtc).TotalSeconds < 2.5) return false;
                bool offRoute = RoadRouter.IsOffRoute(playerPt, activeRoute, RoadRouter.AdaptiveOffRouteThreshold(filteredSpeed));
                double minMoveM = offRoute ? 15.0 : (filteredSpeed < 5.5 ? 75.0 : 140.0);
                if(lastRoutePlayerPt!=PointF.Empty && RoadRouter.DistanceMeters(lastRoutePlayerPt,playerPt)<minMoveM) return false;
                lastAutoRerouteUtc = nowUtc;
            }
            if(routeCalculating) {
                if(targetChanged || force) { wpfQueuedTarget=target; if(force) routeGeneration++; }
                return false;
            }
            routeCalculating=true;
            long request=routeGeneration;
            PointF targetPoint=target.Centroid;
            RoadRouteMode requestedMode=wpfRouteMode;
            ThreadPool.QueueUserWorkItem(state=> {
                RoadRoute result=null; Exception failure=null;
                try { result=RoadRouter.Instance.FindRoute(playerPt,targetPoint,requestedMode); }
                catch(Exception ex) { failure=ex; }
                try {
                    if(IsDisposed) return;
                    BeginInvoke((Action)(()=> {
                        routeCalculating=false;
                        if(wpfQueuedTarget!=null) { var queued=wpfQueuedTarget;wpfQueuedTarget=null;if(Object.ReferenceEquals(searchTarget,queued)&&position!=null)RequestRouteAsync(ToMap(position),queued,true); }
                        if(failure!=null) { Program.LogException("Route",failure); if(force) FinishVoiceReroute(false); return; }
                        if(closing || IsDisposed || request!=routeGeneration || !Object.ReferenceEquals(searchTarget,target) || target.Centroid!=targetPoint) return;
                        activeRoute=result; lastRouteTarget=target; lastRoutePlayerPt=playerPt;
                        if(force) FinishVoiceReroute(result!=null && result.Success);
                        lastFrameKey=null;
                        RenderOverlay();
                    }));
                } catch(InvalidOperationException) { /* Window closed while the result was queued. */ }
            });
            return true;
        }

        static bool DestinationReached(MapZone target,PointF current) {
            if(target==null) return false;
            double radiusM;
            if(WpfMapSurface.AviationModeEnabled) {
                radiusM = Math.Max(140.0, WpfMapSurface.AviationArrivalRadiusM);
            } else if(RoadRouter.Instance.IsWaterAt(current)) {
                radiusM = 45.0; // Boat / water approach
            } else {
                double spd = WpfMapSurface.CurrentGroundSpeedMps;
                radiusM = spd >= 6.0 ? Math.Min(55.0, 30.0 + spd * 1.1) : 18.0; // Car vs Foot precision
            }
            return DestinationSearch.Metres(current,target.Centroid)<=radiusM;
        }

    }
}
