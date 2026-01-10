using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using Menu.Remix.MixedUI;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine;

#pragma warning disable CS0618

[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace FallThroughMap;//logical pits but ehhh

[BepInDependency("SBCameraScroll", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public partial class FallThroughMap : BaseUnityPlugin
{
    public const string PLUGIN_GUID = "useless.fallthroughmap";
    public const string PLUGIN_NAME = "Logical Pits";
    public const string PLUGIN_DESC = "";
    public const string PLUGIN_VERSION = "0.1.3";
    public static ManualLogSource Log;
    public static FallThroughMapOptions Options = FallThroughMapOptions.instance;
    
    public static bool CameraScrollOn = false;
    public void OnEnable()
    {
        Log = base.Logger;

        On.RainWorld.OnModsInit += RainWorld_OnModsInit;
    }

    public static bool debug = true;
    public static void LPLog(object data)
    {
        if (debug)
        {
            Log.LogDebug(data);
        }
    }

    public void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        InitMods();
    }

    static Hook ScrollHook;

    public static void CameraScrollDependency()
    {
        ScrollHook = new Hook(typeof(SBCameraScroll.RoomCameraMod).GetMethod(nameof(SBCameraScroll.RoomCameraMod.UpdateOnScreenPosition)), UpdateOnScreenPosition);
        CameraScrollOn = true;
    }

    public void InitMods()
    {
        try
        {
            On.Creature.Update += Creature_Update2;
            IL.Creature.Update += Creature_Update;

            foreach(ModManager.Mod mod in ModManager.ActiveMods)
            {
                if (mod.id == "SBCameraScroll" && mod.enabled)
                {
                    CameraScrollDependency();
                }
            }

            MachineConnector.SetRegisteredOI(PLUGIN_GUID, Options);
        }
        catch (Exception e)
        {
            Log.LogDebug(e);
        }
    }

    public static void UpdateOnScreenPosition(Action<RoomCamera> orig, RoomCamera camera)
    {
        if (camera.followAbstractCreature.realizedCreature != null && FallThroughMap.FallingEntities != null && FallThroughMap.FallingEntities.TryGetValue(camera.followAbstractCreature.realizedCreature, out var huy) && camera.room == huy.fellRoom)
        {
            SBCameraScroll.RoomCameraMod.RoomCameraFields attached_Fields = SBCameraScroll.RoomCameraMod.GetFields(camera);
            attached_Fields.last_on_screen_position = huy.fellRoomPosition;
            attached_Fields.on_screen_position = huy.fellRoomPosition;
        }
        else
        {
            orig(camera);
        }
    }

    public void OnDisable()
    {
        On.Creature.Update -= Creature_Update2;
        IL.Creature.Update -= Creature_Update;
        On.RainWorld.OnModsInit -= RainWorld_OnModsInit;

        if (ScrollHook != null)
        {
            ScrollHook.Undo();
            ScrollHook = null;
        }
    }

    public class FallThroughMapOptions : OptionInterface
    {
        public static FallThroughMapOptions instance = new FallThroughMapOptions();
        public FallThroughMapOptions()
        {
            fallOnOtherLayers = this.config.Bind<bool>("fallOnOtherLayers", true);
            onlyPlayerFall = this.config.Bind<bool>("onlyPlayerFall", false);
            checkForConnections = this.config.Bind<bool>("checkForConnections", true);
            fallmul = this.config.Bind<int>("fallmul", 100, new ConfigAcceptableRange<int>(0, 200));
            margin = this.config.Bind<int>("margin", 20, new ConfigAcceptableRange<int>(-200, 400));
            canGoThruSides = this.config.Bind<bool>("canGoThruSides", true);
            canGoUpwards = this.config.Bind<bool>("canGoUpwards", true);
            creaturesCanGoThruSides = this.config.Bind<bool>("creaturesCanGoThruSides", false);
            noWait = this.config.Bind<bool>("noWait", true);
        }

        public readonly Configurable<int> fallmul;
        public readonly Configurable<int> margin;
        public readonly Configurable<bool> fallOnOtherLayers;
        public readonly Configurable<bool> onlyPlayerFall;
        public readonly Configurable<bool> checkForConnections;
        public readonly Configurable<bool> canGoThruSides;
        public readonly Configurable<bool> canGoUpwards;
        public readonly Configurable<bool> creaturesCanGoThruSides;
        public readonly Configurable<bool> noWait;
        
        private UIelement[] UIArrPlayerOptions;
        
        public override void Initialize()
        {
            var opTab = new OpTab(this, "Options");
            this.Tabs = new[]
            {
                opTab
            };

            UIArrPlayerOptions = new UIelement[]
            {
                new OpLabel(10f, 550f, "Options", true),
                new OpLabel(10f, 520f, "Fall on map's other layers"),
                new OpCheckBox(fallOnOtherLayers, new Vector2(10f, 490f)),
                new OpLabel(10f, 460f, "Only players can fall"),
                new OpCheckBox(onlyPlayerFall, new Vector2(10f, 430f)),
                new OpLabel(10f, 400f, "Check for available connections"),
                new OpCheckBox(checkForConnections, new Vector2(10f, 370f)),
                new OpLabel(10f, 350f, "(if disabled, you will sometimes fall in inacessible rooms)"),
                new OpLabel(10f, 320f, "Fall velocity multiplier (in percent)"),
                new OpSlider(fallmul, new Vector2(10f, 260f), 200, false),
                new OpLabel(10f, 220f, "Allow going through the sides of rooms"),
                new OpCheckBox(canGoThruSides, new Vector2(10f, 190f)),
                new OpLabel(10f, 150f, "Allow going upwards in the room"),
                new OpCheckBox(canGoUpwards, new Vector2(10f, 110f)),

                new OpLabel(200f, 520f, "Allow creatures going sides of the rooms"),
                new OpCheckBox(creaturesCanGoThruSides, new Vector2(200f, 490f)),
                new OpLabel(200f, 460f, "Screen end check margin (if you can't transition horizontally somewhere,\n you might wanna set this higher), default is 20"),
                new OpSlider(margin, new Vector2(200f, 420f), 200, false),
                //new OpLabel(200f, 460f, "Remove the wait before transition"),
                //new OpCheckBox(noWait, new Vector2(200f, 430f)),
            };

            opTab.AddItems(UIArrPlayerOptions);
        }

        public override void Update()
        {
            if (((OpCheckBox)UIArrPlayerOptions[6]).value == "true")
            {
                if (!UIArrPlayerOptions[7].Hidden) UIArrPlayerOptions[7].Hide();
            }
            else
            {
                if (UIArrPlayerOptions[7].Hidden) UIArrPlayerOptions[7].Show();
            }
        }

    }
    private void Creature_Update(ILContext il)
    {
        ILCursor cursor = new ILCursor(il);
        cursor.GotoNext(MoveType.After, x => x.MatchLdfld(typeof(BodyChunk).GetField(nameof(BodyChunk.restrictInRoomRange))), x => x.MatchNeg(), x => x.MatchLdcR4(1), x => x.MatchAdd());
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<float, Creature, float>>(FallThroughMap.FellBelowMap);
    }

    public static int ClosestCamera(Room room, Vector2 v)
    {
        int index = -1;
        float dist = float.MaxValue;

        for (int i = 0; i < room.cameraPositions.Length; i++)
        {
            if ((room.cameraPositions[i] - v).sqrMagnitude < dist)
            {
                dist = (room.cameraPositions[i] - v).sqrMagnitude;
                index = i;
            }
        }

        return index;
    }

    public static void Creature_Update2(On.Creature.orig_Update orig, Creature cr, bool eu)
    {
        orig(cr, eu);
        try
        {
            if (FallThroughMap.FallingEntities != null && FallThroughMap.FallingEntities.TryGetValue(cr, out var huy))
            {
                var fellRoom = huy.fellRoom;
                var room = fellRoom.abstractRoom;
                var vel = huy.lastVelocity;
                var inRoomPos = huy.fellRoomPosition;
                var curTime = huy.curTime;
                var fallTime = (huy.roomDist / 40f);
                var abstrcr = cr.abstractCreature;

                if (fellRoom != null && ((cr is Player && fellRoom.ReadyForPlayer) || (abstrcr.abstractAI != null && fellRoom.readyForAI)))
                {
                    if (abstrcr.Room != fellRoom.abstractRoom)
                    {
                        Vector2 pos = fellRoom.game.world.RoomToWorldPos(inRoomPos, room.index);
                        var coord = fellRoom.GetWorldCoordinate(inRoomPos);

                        List<AbstractPhysicalObject> allConnectedObjects = abstrcr.GetAllConnectedObjects();
                        for (int i = 0; i < allConnectedObjects.Count; i++)
                        {
                            if (allConnectedObjects[i].realizedObject != null)
                            {
                                Creature creature = allConnectedObjects[i].realizedObject as Creature;

                                if (allConnectedObjects[i].realizedObject.room != null)
                                {
                                    allConnectedObjects[i].realizedObject.room.RemoveObject(allConnectedObjects[i].realizedObject);
                                    //LPLog(allConnectedObjects[i].realizedObject);
                                }
                            }
                        }
                        if (cr.graphicsModule != null)
                        {
                            cr.graphicsModule.SuckedIntoShortCut(cr.mainBodyChunk.pos);
                        }
                        if (cr.appendages != null)
                        {
                            for (int num8 = 0; num8 < cr.appendages.Count; num8++)
                            {
                                cr.appendages[num8].Update();
                            }
                        }
                        // i could use cr.SuckedIntoShortcut instead but that shit is private for whatever reason (i can use invoke but meh)

                        cr.SpitOutOfShortCut(new RWCustom.IntVector2((int)(inRoomPos.x / 20f), (int)(inRoomPos.y / 20f)), fellRoom, true);
                        
                        List<AbstractPhysicalObject> objs = abstrcr.GetAllConnectedObjects();
                        for (int i = 0; i < objs.Count; i++)
                        {
                            objs[i].pos = abstrcr.pos;
                            objs[i].Room.RemoveEntity(objs[i]);
                            room.AddEntity(objs[i]);
                            objs[i].realizedObject.sticksRespawned = true;
                        }

                        //cr.LoseAllGrasps();
                        /*for (int i = 0; i < cr.grasps.Count(); i++)
                        {
                            var grasp = cr.grasps[i];

                            //cr.ReleaseGrasp(i);
                            cr.Grab(grasp.grabbed, grasp.graspUsed, grasp.chunkGrabbed, grasp.shareability, grasp.dominance, false, grasp.pacifying);
                        }*/

                        for (int i = 0; i < allConnectedObjects.Count; i++) // for safety
                        {
                            int num = 0;
                            for (int s = 0; s < fellRoom.updateList.Count; s++)
                            {
                                if (allConnectedObjects[i].realizedObject == fellRoom.updateList[s])
                                {
                                    num++;
                                }
                                if (num > 1)
                                {
                                    fellRoom.updateList.RemoveAt(s);
                                }
                            }
                        }
                    }

                    if (abstrcr.FollowedByCamera(0) && fellRoom.ReadyForPlayer && fellRoom.game.cameras[0].room != fellRoom)
                    {
                        var room_camera = fellRoom.game.cameras[0];
                        room_camera.virtualMicrophone.AllQuiet();

                        if (CameraScrollOn)
                        {
                            room_camera.MoveCamera(fellRoom, 0);
                        }
                        else
                        {
                            //var oldRoom = room_camera.room;
                            room_camera.MoveCamera(fellRoom, ClosestCamera(fellRoom, inRoomPos));
                        }
                    }

                    if ((cr.room.game.clock) / 40f > (curTime / 40f + fallTime / 40))
                    {
                        List<AbstractPhysicalObject> allConnectedObjects = abstrcr.GetAllConnectedObjects();
                        for (int i = 0; i < allConnectedObjects.Count; i++)
                        {
                            var physobj = allConnectedObjects[i];

                            if (physobj != null && physobj.realizedObject != null)
                            {
                                foreach (BodyChunk bodyChunk in physobj.realizedObject.bodyChunks)
                                {
                                    if (bodyChunk == null) continue;

                                    bodyChunk.vel = vel;
                                }
                            }
                        }

                        FallThroughMap.FallingEntities.Remove(cr);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.LogDebug(e);

            // Get stack trace for the exception with source file information
            var st = new StackTrace(e, true);
            // Get the top stack frame
            var frame = st.GetFrame(0);
            // Get the line number from the stack frame
            var line = frame.GetFileLineNumber();
            Log.LogDebug(st);
            Log.LogDebug(line);
        }
    }

    private static bool LineIntersectsLine(Vector2 l1p1, Vector2 l1p2, Vector2 l2p1, Vector2 l2p2)
    {
        float q = (l1p1.y - l2p1.y) * (l2p2.x - l2p1.x) - (l1p1.x - l2p1.x) * (l2p2.y - l2p1.y);
        float d = (l1p2.x - l1p1.x) * (l2p2.y - l2p1.y) - (l1p2.y - l1p1.y) * (l2p2.x - l2p1.x);

        if (d == 0)
        {
            return false;
        }

        float r = q / d;

        q = (l1p1.y - l2p1.y) * (l1p2.x - l1p1.x) - (l1p1.x - l2p1.x) * (l1p2.y - l1p1.y);
        float s = q / d;

        if (r < 0 || r > 1 || s < 0 || s > 1)
        {
            return false;
        }

        return true;
    }

    private static int LineIntersectsRect(Vector2 lp1, Vector2 lp2, Vector2 r1, Vector2 r2, Vector2 r3, Vector2 r4)
    {
        Vector2 closest = new Vector2(float.MaxValue, float.MaxValue);
        int select = 0;

        if (LineIntersectsLine(lp1, lp2, r1, r2))
        {
            closest = RWCustom.Custom.LineIntersection(lp1, lp2, r1, r2);
            select = 1;
        }

        if (LineIntersectsLine(lp1, lp2, r2, r3))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r2, r3);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
            {
                closest = intersect;
                select = 2;
            }
        }

        if (LineIntersectsLine(lp1, lp2, r3, r4))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r3, r4);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
            {
                closest = intersect;
                select = 3;
            }
        }

        if (LineIntersectsLine(lp1, lp2, r4, r1))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r4, r1);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
            {
                closest = intersect;
                select = 4;
            }
        }

        return select;
    }
    private static Vector2 LineRectIntersection(Vector2 lp1, Vector2 lp2, Vector2 r1, Vector2 r2, Vector2 r3, Vector2 r4)
    {
        Vector2 closest = new Vector2(float.MaxValue, float.MaxValue);

        if (LineIntersectsLine(lp1, lp2, r1, r2))
        {
            closest = RWCustom.Custom.LineIntersection(lp1, lp2, r1, r2);
        }

        if (LineIntersectsLine(lp1, lp2, r2, r3))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r2, r3);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
                closest = intersect;
        }

        if (LineIntersectsLine(lp1, lp2, r3, r4))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r3, r4);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
                closest = intersect;
        }

        if (LineIntersectsLine(lp1, lp2, r4, r1))
        {
            Vector2 intersect = RWCustom.Custom.LineIntersection(lp1, lp2, r4, r1);

            if ((closest - lp1).sqrMagnitude > (intersect - lp1).sqrMagnitude)
                closest = intersect;
        }

        return closest;
    }

    public static Vector2 WorldToRoomPos(Vector2 inWorldPos, AbstractRoom room)
    {
        return inWorldPos - GetMapPos(room);
    }

    //return (abstractRoom.mapPos / 3f + new Vector2(10f, 10f)) * 20f + inRoomPos - new Vector2((float) abstractRoom.size.x* 20f, (float) abstractRoom.size.y* 20f) / 2f;

    public static Vector2 GetMapPos(AbstractRoom room)
    {
        return ((room.mapPos / 3f) + new Vector2(10f, 10f)) * 20f - new Vector2(room.size.x * 20f, room.size.y * 20f) / 2f;
    }
    public static Vector2 GetMapSize(AbstractRoom room)
    {
        return new Vector2(room.size.x * 20f, room.size.y * 20f);
    }

    public class FallingData
    {
        public Room fellRoom;
        public Vector2 lastVelocity;
        public Vector2 fellRoomPosition;
        public float roomDist;
        public int curTime;
        //public List<World> worlds;

        public FallingData(Room fellRoom, Vector2 lastVelocity, Vector2 fellRoomPosition, float roomDist, int curTime)//, List<World> worlds)
        {
            this.fellRoom = fellRoom;
            this.lastVelocity = lastVelocity;
            this.fellRoomPosition = fellRoomPosition;
            this.roomDist = roomDist;
            this.curTime = curTime;
            //this.worlds = worlds;
        }
    }

    public static ConditionalWeakTable<Creature, FallingData> FallingEntities = new ConditionalWeakTable<Creature, FallingData>();

    public static float FellBelowMap(float restrictInRoomRange, Creature cr)
    {
        if (Options == null || cr == null) return restrictInRoomRange;
        if (!(cr is Player) && Options.onlyPlayerFall.Value) return restrictInRoomRange;
        if ((cr.Template.canFly && !cr.Stunned && !cr.dead)) return restrictInRoomRange; // let them do their vanilla thing
        //if (cr.bodyChunks[0].vel.sqrMagnitude < 5f) return restrictInRoomRange;

        AbstractRoom[] abstractRooms = cr.room.game.world.abstractRooms;
        //should probably sort rooms by layer

        var bodyChunk = cr.bodyChunks[0];
        var posit = bodyChunk.pos;

        RoomBorderPushBack pushback = null;
        for (int i = 0; i < cr.room.updateList.Count; i++)
        {
            if (cr.room.updateList[i] is RoomBorderPushBack)
            {
                pushback = cr.room.updateList[i] as RoomBorderPushBack;
            }
        }
        float range = 200f; // 100f for y, 200f for x
        float add = Options.margin.Value;//80f; // 20f
        bool fell = posit.y < -range + add
            || posit.y > cr.room.PixelHeight + range - add
            || posit.x > cr.room.PixelWidth + range - add
            || posit.x < -range + add;

        if (pushback != null)
        {
            fell = fell
                || posit.x < pushback.leftmostCameraPos - pushback.margin
                || posit.x > pushback.rightmostCameraPos + 1400f + pushback.margin;
        }

        //so much magic numbers, beautiful

        if (FallingEntities.TryGetValue(cr, out var huy) || cr.grabbedBy.Count > 0) return -16000f;

        if (!fell) { return restrictInRoomRange; }


        var vel = new Vector2();
        int cntr = 0;

        foreach (BodyChunk bc in cr.bodyChunks)
        {
            vel += bc.vel;
            cntr++;
        }

        vel /= cntr; // if you have 0 bodychunks, well....

        var normvel = vel.normalized;

        AbstractRoom fallRoom = null;
        Vector2 fallDir = Vector2.zero;
        Vector2 intersectionPosition = Vector2.zero;
        float roomDist = float.MaxValue;

        Vector2 pos = cr.room.world.RoomToWorldPos(cr.bodyChunks[0].pos, cr.room.abstractRoom.index);

        /*List<World> worlds = new List<World>();
        for (int i = 0; i < abstractRooms.Length; i++)
        {
            var room = abstractRooms[i];
            if (!room.gate) continue;
            string[] gateName = Regex.Split(room.name, "_");
            string otherWorldName = "ERROR!";
            if (gateName.Length == 3)
                for (int j = 1; j < 3; j++)
                    if (gateName[j] != cr.room.world.name)
                    {
                        otherWorldName = gateName[j];
                        break;
                    }
            if (otherWorldName == "ERROR!") continue;
            try
            {
                World oldWorld = cr.room.game.world;
                cr.room.game.overWorld.LoadWorld(otherWorldName, (cr as Player).slugcatStats.name, false);
                worlds.Add(cr.room.game.overWorld.activeWorld);
                cr.room.game.overWorld.activeWorld = oldWorld;
            }
            catch(Exception e)
            {
                Log.LogDebug(e);
            }
        }*/
        //later!

        var startOurRect = GetMapPos(cr.room.abstractRoom);
        var sizeOurRect = GetMapSize(cr.room.abstractRoom);
        var center = startOurRect + sizeOurRect * 0.5f;

        pos.x = Mathf.Clamp(pos.x, startOurRect.x, startOurRect.x + sizeOurRect.x);
        pos.y = Mathf.Clamp(pos.y, startOurRect.y, startOurRect.y + sizeOurRect.y);

        int intersectionOurRect = LineIntersectsRect(center, center + (pos - center) * 2f,
            new Vector2(startOurRect.x, startOurRect.y + sizeOurRect.y), // upper left corner
            new Vector2(startOurRect.x + sizeOurRect.x, startOurRect.y + sizeOurRect.y),
            new Vector2(startOurRect.x + sizeOurRect.x, startOurRect.y),
            new Vector2(startOurRect.x, startOurRect.y)
            );

        for (int i = 0; i < abstractRooms.Length; i++)
        {
            var room = abstractRooms[i];
            if (room.roomAttractions.Length <= 0) continue;
            if (room.world.DisabledMapRooms.Contains(room.name)) continue;

            if (room.offScreenDen || !room.AnySkyAccess || cr.room.abstractRoom == room) continue;
            if (!Options.fallOnOtherLayers.Value && cr.room.abstractRoom.layer != room.layer) continue;


            bool foundExit = false;
            foreach(int otherRoom in room.connections)
            {
                //LPLog(otherRoom + " huy");
                if (otherRoom != -1) foundExit = true;
            }

            if (!foundExit && Options.checkForConnections.Value) continue;

            var start = GetMapPos(room);
            var size = GetMapSize(room);

            //var dist = (start + size * 0.5f - pos).magnitude;

            var dir = (start + size * 0.5f - pos).normalized;
            var dir2 = normvel;

            //bool intersection = LineIntersectsLine(pos - normvel * (Math.Abs(pos.y - GetMapPos(room).y) + 100f), pos + normvel * 15000, upperline, upperline2);

            int intersection = LineIntersectsRect(pos - normvel * 200f, start + size * 0.5f,// - normvel * (Math.Abs(pos.y - GetMapPos(room).y) + 100f), pos + normvel * 15000,
                new Vector2(start.x, start.y + size.y),
                new Vector2(start.x + size.x, start.y + size.y),
                new Vector2(start.x + size.x, start.y),
                new Vector2(start.x, start.y)
                );

            //LPLog("FallThroughMap available room " + room.name + " Int: " + intersection + " Int2: " + intersectionOurRect);
            
            if (!Options.canGoUpwards.Value && (intersectionOurRect == 1))
            {
                continue;
            }

            if ((!Options.canGoThruSides.Value || (!Options.creaturesCanGoThruSides.Value && !(cr is Player))) && (intersectionOurRect == 2 || intersectionOurRect == 4))
            {
                continue;
            }

            if (intersection > 0 && ((intersection == 1 && intersectionOurRect == 3) || (intersection == 2 && intersectionOurRect == 4) || (intersectionOurRect == 1 && intersection == 3) || (intersectionOurRect == 2 && intersection == 4)))
            {
                Vector2 fallDirCheck = intersection == 1 ? new Vector2(0, -1) : intersection == 2 ? new Vector2(-1, 0) : intersection == 3 ? new Vector2(0, 1) : new Vector2(1, 0);
                int intersection2 = LineIntersectsRect(pos - fallDirCheck * 200f, pos + fallDirCheck * 100000f * ((intersection == 2 || intersection == 4) ? 0.1f : 1f),
                    new Vector2(start.x, start.y + size.y),
                    new Vector2(start.x + size.x, start.y + size.y),
                    new Vector2(start.x + size.x, start.y),
                    new Vector2(start.x, start.y)
                    );

                //LPLog("FallThroughMap room intersection side: Room " + room.name + ", pos: " + GetMapPos(room) + ", size: " + size + ", intersection:" + intersection2);

                if (intersection2 == 0)
                {
                    continue; // yeah no that's weird
                    //fallDirCheck = (start + size * 0.5f - pos).normalized;
                }

                Vector2 intersectionPos = LineRectIntersection(pos - fallDirCheck * 200f, pos + fallDirCheck * 100000f * ((intersection == 2 || intersection == 4) ? 0.1f : 1f),
                    new Vector2(start.x, start.y + size.y),
                    new Vector2(start.x + size.x, start.y + size.y),
                    new Vector2(start.x + size.x, start.y),
                    new Vector2(start.x, start.y)
                );

                var dist = (pos - intersectionPos).magnitude;
                LPLog("FallThroughMap available rooms: Room " + room.name + ", pos: " + GetMapPos(room) + ", size: " + size + ", Intersection!");
                LPLog("Fall direction: " + fallDirCheck);
                LPLog("Fall direction2: " + dir2);

                float dot = Vector3.Dot(dir2, fallDirCheck);

                LPLog("Fall dot: " + dot);
                LPLog("Fall dist: " + dist + ", roomDist: " + roomDist);

                //find the room with the shorter distance, with priority if the room is in the same layer
                bool anotherLayer = fallRoom != null && (fallRoom.layer == cr.room.abstractRoom.layer && room.layer != cr.room.abstractRoom.layer);
                if (dot > 0 && (!anotherLayer && dist < roomDist))
                {
                    fallRoom = room;
                    roomDist = dist;
                    fallDir = fallDirCheck;
                    intersectionPosition = intersectionPos;
                }
            }
        }

        if (fallRoom != null)
        {
            var room = fallRoom;
            var start = GetMapPos(room);
            var size = GetMapSize(room);

            Vector2 intersection = intersectionPosition;

            Vector2 inRoomPos = WorldToRoomPos(intersection, room) - fallDir * 500f;

            cr.room.game.world.ActivateRoom(room);

            if (room.realizedRoom == null)
            {
                LPLog("FallThroughMap realizedRoom can't be created.");
                return restrictInRoomRange;
            }

            if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos)).Terrain == Room.Tile.TerrainType.Solid))
            {
                if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(-1, 0)).Terrain == Room.Tile.TerrainType.Air))
                {
                    inRoomPos += new Vector2(-20, 0);
                }
                else if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(1, 0)).Terrain == Room.Tile.TerrainType.Air))
                {
                    inRoomPos += new Vector2(20, 0);
                }
                else if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(0, 1)).Terrain == Room.Tile.TerrainType.Air))
                {
                    inRoomPos += new Vector2(0, 20);
                }
                else if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(0, -1)).Terrain == Room.Tile.TerrainType.Air))
                {
                    inRoomPos += new Vector2(0, -20);
                }
                else // todo: make it detect deeper corners
                {
                    LPLog("FallThroughMap chosen room has a solid tile on the intersection. Not teleporting.");

                    return restrictInRoomRange;
                }
            }

            LPLog("FallThroughMap chosen room: Room " + room.name + ", pos: " + GetMapPos(room) + ", room connections: " + room.connections.Length + ", size: " + size + ", Intersecton position: " + intersection + " For creature: " + cr);

            //Vector2 addvel = new Vector2(0, -(roomDist / 20f / 3f / cr.gravity)) / 2.5f; // 2.5 instead of 2 because yeah it was a bit harsh by default
            //vel += addvel;
            //vel *= roomDist / 60f / cr.gravity;
            vel *= Options.fallmul.Value * 0.01f;

            FallingEntities.Add(cr, new FallingData(room.realizedRoom, vel, inRoomPos, roomDist, room.realizedRoom.game.clock - (/*Options.noWait.Value*/true ? 999 : 0)));//, worlds));
        }
        else
        {
            //LPLog("No room to fall.");
        }

        return fallRoom == null ? restrictInRoomRange : -16000f;
    }
}
