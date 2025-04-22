using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
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
    public const string PLUGIN_VERSION = "0.0.9";
    public static ManualLogSource Log;
    public static FallThroughMapOptions Options = FallThroughMapOptions.instance;
    
    public static bool CameraScrollOn = false;
    public void OnEnable()
    {
        Log = base.Logger;

        On.RainWorld.OnModsInit += RainWorld_OnModsInit;
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
            SBCameraScroll.RoomCameraMod.Attached_Fields attached_Fields = SBCameraScroll.RoomCameraMod.Get_Attached_Fields(camera);
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
        }

        public readonly Configurable<bool> fallOnOtherLayers;
        public readonly Configurable<bool> onlyPlayerFall;
        public readonly Configurable<bool> checkForConnections;
        
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
                new OpLabel(10f, 520f, "Fall on other layers"),
                new OpCheckBox(fallOnOtherLayers, new Vector2(10f,490f)),
                new OpLabel(10f, 460f, "Only players can fall"),
                new OpCheckBox(onlyPlayerFall, new Vector2(10f,430f)),
                new OpLabel(10f, 400f, "Check for available connections"),
                new OpCheckBox(checkForConnections, new Vector2(10f,370f)),
                new OpLabel(10f, 350f, "(if disabled, you will sometimes fall in inacessible rooms)"),
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
            if ((room.cameraPositions[i] - v).magnitude < dist)
            {
                dist = (room.cameraPositions[i] - v).magnitude;
                index = i;
            }
        }

        return index;
    }

    public static void Creature_Update2(On.Creature.orig_Update orig, Creature self, bool eu)
    {
        orig(self, eu);

        if (FallThroughMap.FallingEntities != null && FallThroughMap.FallingEntities.TryGetValue(self, out var huy))
        {
            //if (huy.fellRoom == self.room)
            //{
                //FallThroughMap.FallingEntities.Remove(self);
                //return;
            //}

            var fellRoom = huy.fellRoom;
            var room = fellRoom.abstractRoom;
            var vel = huy.lastVelocity;
            var inRoomPos = huy.fellRoomPosition + new Vector2(0, 1000);
            var curTime = huy.curTime;
            var roomDist = huy.roomDist;
            var cr = self;
            var fallTime = (roomDist / 40f);

            foreach (BodyChunk bodyChunk in cr.bodyChunks)
            {
                if (bodyChunk == null) continue;

                bodyChunk.vel *= 0;
                bodyChunk.vel += new Vector2(0, cr.gravity);
            }

            if (fellRoom != null && ((cr is Player && fellRoom.ReadyForPlayer) || (cr.abstractCreature.abstractAI != null && fellRoom.readyForAI)))
            {
                /*for (int j = 0; j < huy.worlds.Count; j++)
                {
                    Log.LogDebug(huy.worlds[j].abstractRooms.Length);
                }*/

                if (cr.abstractCreature.Room != fellRoom.abstractRoom)
                {
                    //cr.FlyAwayFromRoom(false);

                    Vector2 pos = fellRoom.game.world.RoomToWorldPos(inRoomPos, room.index);
                    var coord = new WorldCoordinate(room.index, (int)pos.x, (int)pos.y, -1);//fellRoom.GetWorldCoordinate(inRoomPos);
                    cr.abstractCreature.Move(coord);
                    //cr.PlaceInRoom(fellRoom);
                    cr.SpitOutOfShortCut(fellRoom.GetTilePosition(inRoomPos), fellRoom, true);
                    self.abstractCreature.ChangeRooms(coord);

                    /*var coord = fellRoom.GetWorldCoordinate(inRoomPos);
                    cr.abstractCreature.pos = coord;
                    cr.abstractCreature.Move(coord);
                    cr.PlaceInRoom(fellRoom);
                    self.abstractCreature.ChangeRooms(coord);*/

                    List <AbstractPhysicalObject> allConnectedObjects = cr.abstractCreature.GetAllConnectedObjects();
                    for (int i = 0; i < allConnectedObjects.Count; i++)
                    {
                        var physobj = allConnectedObjects[i];

                        if (physobj.realizedObject != null)
                        {
                            for (int j = 0; j < physobj.realizedObject.bodyChunks.Length; j++)
                            {
                                physobj.realizedObject.bodyChunks[j].HardSetPosition(inRoomPos);
                                physobj.realizedObject.bodyChunks[j].vel *= 0f;
                            }

                            if (physobj.realizedObject.appendages != null)
                                for (int j = 0; j < cr.appendages.Count; j++)
                                    physobj.realizedObject.appendages[j].Update();

                            if (physobj.realizedObject.graphicsModule != null)
                            {
                                physobj.realizedObject.graphicsModule.Reset();
                            }

                            if ((physobj.realizedObject as Player) != null && (physobj.realizedObject as Player).spearOnBack != null)
                            {
                                if ((physobj.realizedObject as Player).spearOnBack.spear != null)
                                {
                                    (cr as Player).spearOnBack.spear.abstractPhysicalObject.pos = new WorldCoordinate(cr.abstractCreature.pos.room, cr.abstractCreature.pos.x, cr.abstractCreature.pos.y, cr.abstractCreature.pos.abstractNode);
                                    (physobj.realizedObject as Player).spearOnBack.spear.PlaceInRoom(fellRoom);
                                }
                            }

                            if (physobj.realizedObject.room == fellRoom) continue;

                            physobj.pos = new WorldCoordinate(coord.room, coord.x, coord.y, coord.abstractNode);
                            fellRoom.AddObject(physobj.realizedObject);
                            physobj.realizedObject.NewRoom(fellRoom);
                        }
                    }
                }

                if (cr.abstractCreature.FollowedByCamera(0) && fellRoom.ReadyForPlayer && fellRoom.game.cameras[0].room != fellRoom)
                {
                    fellRoom.game.cameras[0].virtualMicrophone.AllQuiet();
                    var room_camera = fellRoom.game.cameras[0];

                    if (CameraScrollOn)
                    {
                        fellRoom.game.cameras[0].MoveCamera(fellRoom, 0);
                    }
                    else
                    {
                        var oldRoom = room_camera.room;
                        room_camera.MoveCamera(fellRoom, ClosestCamera(fellRoom, inRoomPos));
                    }
                }

                if ((cr.room.game.clock) / 40f > (curTime / 40f + fallTime / 40))
                {
                    List<AbstractPhysicalObject> allConnectedObjects = cr.abstractCreature.GetAllConnectedObjects();
                    for (int i = 0; i < allConnectedObjects.Count; i++)
                    {
                        var physobj = allConnectedObjects[i];

                        foreach (BodyChunk bodyChunk in physobj.realizedObject.bodyChunks)
                        {
                            if (bodyChunk == null) continue;

                            bodyChunk.vel = vel;
                        }
                    }

                    FallThroughMap.FallingEntities.Remove(self);
                }
            }
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

    public static Vector2 WorldToRoomPos(Vector2 inWorldPos, AbstractRoom room)
    {
        return inWorldPos - GetMapPos(room);
    }

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
        if (!(!cr.room.water || cr.room.waterInverted || cr.room.defaultWaterLevel < -10) && (!cr.Template.canFly || cr.Stunned || cr.dead) && (cr is Player || !cr.room.game.IsArenaSession || cr.room.game.GetArenaGameSession.chMeta == null || !cr.room.game.GetArenaGameSession.chMeta.oobProtect)) return restrictInRoomRange;

        AbstractRoom[] abstractRooms = cr.room.game.world.abstractRooms;
        //should probably sort rooms by layer

        bool fell = cr.bodyChunks[0].pos.y < restrictInRoomRange + 1000f;// || cr.bodyChunks[0].pos.y > cr.room.PixelHeight + 1000f;

        if (fell)
        {
            if (FallingEntities.TryGetValue(cr, out var huy) || cr.grabbedBy.Count > 0) return -16000f;

            var vel = cr.bodyChunks[0].vel + new Vector2(0, -10);
            vel.x = 0;

            var normvel = vel.normalized;

            AbstractRoom fallRoom = null;
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

            //bool upperlinefall = true;

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
                    //Log.LogDebug(otherRoom + " huy");
                    if (otherRoom != -1) foundExit = true;
                }

                if (!foundExit && Options.checkForConnections.Value) continue;

                var dist = (GetMapPos(room) - pos).magnitude;

                var upperline = GetMapPos(room);
                var upperline2 = GetMapPos(room);
                var size = GetMapSize(room);
                upperline.y = upperline.y + size.y;
                upperline2.y = upperline2.y + size.y;
                upperline2.x = upperline2.x + size.x;

                bool intersection = LineIntersectsLine(pos - normvel * (Math.Abs(pos.y - GetMapPos(room).y) + 100f), pos + normvel * 15000, upperline, upperline2);
                //bool intersection_lower = vel.y > 0 && LineIntersectsLine(pos - normvel * 1500, pos + normvel * (vel.y < 0 ? 15000 : 1500), upperline - new Vector2(0, size.y), upperline2 - new Vector2(0, size.y));

                //if (intersection_lower) upperlinefall = false;

                if (intersection)// || intersection_lower)
                {
                    //Log.LogDebug("FallThroughMap available rooms: Room " + room.name + ", pos: " + GetMapPos(room) + ", size: " + size + ", Intersection!");

                    if (dist < roomDist || (fallRoom != null && fallRoom.layer != cr.room.abstractRoom.layer && room.layer == cr.room.abstractRoom.layer))
                    {
                        if (!(fallRoom != null && fallRoom.layer == cr.room.abstractRoom.layer && room.layer != cr.room.abstractRoom.layer))
                        {
                            fallRoom = room;
                            roomDist = dist;
                        }
                    }

                }
            }

            if (fallRoom != null)
            {
                var room = fallRoom;

                var upperline = GetMapPos(room);
                var upperline2 = GetMapPos(room);
                var size = GetMapSize(room);

                //if (upperlinefall)
                //{
                    upperline.y = upperline.y + size.y;
                    upperline2.y = upperline2.y + size.y;
                //}

                upperline2.x = upperline2.x + size.x;

                Vector2 intersection = RWCustom.Custom.LineIntersection(pos - normvel * (Math.Abs(pos.y - GetMapPos(room).y) + 100f), pos + normvel * 15000, upperline, upperline2);// * (upperlinefall ? 15000 : 1500), upperline, upperline2);

                Vector2 inRoomPos = WorldToRoomPos(intersection, room);

                cr.room.game.world.ActivateRoom(room);

                if (room.realizedRoom == null)
                {
                    return restrictInRoomRange;
                }

                if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos)).Terrain == Room.Tile.TerrainType.Solid))
                {
                    //new IntVector2((int)((pos.x + 20f) / 20f) - 1, (int)((pos.y + 20f) / 20f) - 1)
                    //what's the point of all those calculations to just end up with (int)(pos.x / 20f)???

                    if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(-1, 0)).Terrain == Room.Tile.TerrainType.Air))
                    {
                        inRoomPos += new Vector2(-20, 0);
                    }
                    else if ((room.realizedRoom.GetTile(room.realizedRoom.GetTilePosition(inRoomPos) + new RWCustom.IntVector2(1, 0)).Terrain == Room.Tile.TerrainType.Air))
                    {
                        inRoomPos += new Vector2(20, 0);
                    }
                    else
                    {
                        //Log.LogDebug("FallThroughMap chosen room has a solid tile on the intersection. Not teleporting.");

                        return restrictInRoomRange;
                    }
                }

                Log.LogDebug("FallThroughMap chosen room: Room " + room.name + ", pos: " + GetMapPos(room) + ", room connections: " + room.connections.Length + ", size: " + size + ", Intersecton position: " + intersection + " For creature: " + cr);

                Vector2 addvel = new Vector2(0, -(roomDist / 20f / 3f / cr.gravity)) / 2f;
                vel += addvel;
                vel *= 1f;

                //cr.RemoveFromRoom();

                FallingEntities.Add(cr, new FallingData(room.realizedRoom, vel, inRoomPos, roomDist, room.realizedRoom.game.clock));//, worlds));
                
                //room.realizedRoom.RemoveObject(cr);
            }

            return fallRoom == null ? restrictInRoomRange : -16000f;
        }
        return restrictInRoomRange;
    }
}
