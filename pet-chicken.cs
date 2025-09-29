using System.ComponentModel;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Store.Extension;
using static Store.Store;
using static StoreApi.Store;

namespace Store;

[StoreItemType("pet")]
public class Item_pet : IItemModule
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    public class PetModel
    {
        public CDynamicProp? entity { get; set; }
        public CChicken? chicken { get; set; }
        public Vector? LastPos { get; set; }
        public bool isMoving { get; set; } = false;
        public float lastMovementCheck { get; set; } = 0f; // Dodane dla throttling
        public int stationaryTicks { get; set; } = 0; // Licznik ticków bez ruchu
        public float rotationOffset { get; set; } = -90f; // Offset rotacji dla modelu
    }

    private static readonly Dictionary<CCSPlayerController, Dictionary<int, PetModel>> PlayerPetEntities = [];
    private const float MOVEMENT_THRESHOLD = 0.1f; // Próg ruchu - znacznie niższy
    private const float CHECK_INTERVAL = 0.2f; // Sprawdzaj co 0.2 sekundy
    private const int STATIONARY_TICKS_THRESHOLD = 3; // Ile sprawdzeń bez ruchu = stoi

    public static MemoryFunctionVoid<CChicken, CCSPlayerPawn> ChickenSetFollowPlayer { get; }
        = new("55 48 89 E5 41 55 4C 8D AF 58 14 00 00 41 54 49 89 F4 53 48 89 FB 4C 89 EF 48 83 EC 28 E8 5E 28 7C 00");

    // Reszta metod pozostaje bez zmian...
    public void OnPluginStart()
    {
        if (Item.IsAnyItemExistInType("pet"))
        {
            Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Instance.RegisterEventHandler<EventPlayerSpawned>(OnPlayerSpawned);
            Instance.RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

            Instance.RegisterListener<Listeners.OnTick>(OnTick);
        }
    }

    private HookResult OnPlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
    {
        throw new NotImplementedException();
    }

    public void OnMapStart()
    {
        PlayerPetEntities.Clear();
    }

    public void OnServerPrecacheResources(ResourceManifest manifest)
    {
        var items = Item.GetItemsByType("pet");
        foreach (var item in items)
        {
            if (item.Value.TryGetValue("model", out var model) && !string.IsNullOrEmpty(model))
                manifest.AddResource(model);
        }
    }

    public bool OnEquip(CCSPlayerController player, Dictionary<string, string> item)
    {
        if (!item.TryGetValue("slot", out var slotStr) || !int.TryParse(slotStr, out var slot) || slot < 0)
            return false;
        var animation = item.TryGetValue("animation", out var anim) ? anim : string.Empty;
        var rotationOffsetStr = item.TryGetValue("rotation_offset", out var rotOffset) ? rotOffset : "-90";
        float.TryParse(rotationOffsetStr, out var rotationOffset);
        Equippet(player, item["model"], slot, animation, rotationOffset);
        return true;
    }

    public bool OnUnequip(CCSPlayerController player, Dictionary<string, string> item, bool update)
    {
        if (!item.TryGetValue("slot", out var slotStr) || !int.TryParse(slotStr, out var slot))
            return false;
        UnEquippet(player, slot);
        return true;
    }

    public static void Equippet(CCSPlayerController player, string model, int slot, string animation = "", float rotationOffset = -90f)
    {
        UnEquippet(player, slot);
        Server.NextFrame(() =>
        {
            var pet = Createpet(player, model, animation, rotationOffset);
            if (pet != null && pet != null && pet.entity!.IsValid)
            {
                if (!PlayerPetEntities.ContainsKey(player))
                    PlayerPetEntities[player] = [];
                PlayerPetEntities[player][slot] = pet;
            }
        });
    }

    public static void UnEquippet(CCSPlayerController player, int slot)
    {
        if (!PlayerPetEntities.TryGetValue(player, out var value) || !value.ContainsKey(slot))
            return;

        var pet = value[slot];
        if (pet.entity != null && pet.entity.IsValid)
            pet.entity.Remove();
        if (pet.chicken != null && pet.chicken.IsValid)
            pet.chicken.Remove();

        value.Remove(slot);
        if (value.Count == 0)
            PlayerPetEntities.Remove(player);
    }

    public static PetModel? Createpet(CCSPlayerController player, string model, string animation = "", float rotationOffset = -90f)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return null;

        var origin = pawn.AbsOrigin;
        var offset = new Vector(30, 30, 0);

        var chicken = Utilities.CreateEntityByName<CChicken>("chicken");
        if (chicken == null) return null;
        chicken.Render = Color.FromArgb(0, 255, 255, 255);



        chicken.DispatchSpawn();
        ChickenSetFollowPlayer.Invoke(chicken, player.PlayerPawn.Value!);

        chicken.Speed = 3.5f;


        chicken.MaxHealth = 9999;
        Utilities.SetStateChanged(chicken, "CBaseEntity", "m_iMaxHealth");

        chicken.Health = 9999;
        Utilities.SetStateChanged(chicken, "CBaseEntity", "m_iHealth");

        chicken.Speed = 300f;

        Server.NextFrame(() =>
        {
            Utilities.SetStateChanged(chicken, "CChicken", "m_flSpeed");
        });



        chicken.Teleport(origin! + offset, pawn.EyeAngles, pawn.AbsVelocity);



        //Server.PrintToChatAll($"Speed: {chicken.Speed}");

        var entity = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (entity == null) return null;
        entity.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
        entity.SetModel(model);
        entity.DispatchSpawn();
        entity.AcceptInput("FollowEntity", chicken, chicken, "!activator");

        if (!string.IsNullOrEmpty(animation))
        {
            entity.AcceptInput("SetAnimation", value: animation);
        }

        if (origin != null)
        {
            entity.Teleport(origin + offset, pawn.EyeAngles, pawn.AbsVelocity);
        }

        return new PetModel
        {
            entity = entity,
            chicken = chicken,
            LastPos = origin! + offset,
            isMoving = false,
            lastMovementCheck = Server.CurrentTime,
            rotationOffset = rotationOffset
        };
    }

    // Pozostałe event handlery...
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        var gamerules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gamerules == null || gamerules.WarmupPeriod)
        {
            return HookResult.Continue;
        }

        var pets = Item.GetPlayerEquipments(player, "pet");
        foreach (var equip in pets)
        {
            var item = Item.GetItem(equip.UniqueId);
            if (item != null && item.TryGetValue("model", out var model))
            {
                var animation = item.TryGetValue("animation", out var anim) ? anim : string.Empty;
                var rotationOffsetStr = item.TryGetValue("rotation_offset", out var rotOffset) ? rotOffset : "-90";
                float.TryParse(rotationOffsetStr, out var rotationOffset);
                Equippet(player, model, equip.Slot, animation, rotationOffset);
            }
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        if (PlayerPetEntities.ContainsKey(player))
        {
            foreach (var slot in PlayerPetEntities[player].Keys.ToList())
                UnEquippet(player, slot);
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        if (PlayerPetEntities.ContainsKey(player))
        {
            foreach (var slot in PlayerPetEntities[player].Keys.ToList())
                UnEquippet(player, slot);
        }
        return HookResult.Continue;
    }

    // ALTERNATYWNA METODA - z ręcznym pozycjonowaniem podczas ruchu
    private void OnTick()
    {
        foreach (var kv in PlayerPetEntities.ToList())
        {
            var player = kv.Key;
            if (player == null || !player.IsValid) continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) continue;

            foreach (var petModel in kv.Value.Values)
            {
                if (petModel == null || petModel.chicken == null || !petModel.chicken.IsValid ||
                    petModel.entity == null || !petModel.entity.IsValid) continue;

                Server.PrintToChatAll($"Kuciok: {petModel.chicken.DesiredActivity} {petModel.chicken.AbsVelocity}");


                Vector currentPos = petModel.chicken.AbsOrigin!;
                Vector playerPos = playerPawn.AbsOrigin!;

                // Sprawdź czy pozycja się zmieniła
                bool positionChanged = petModel.LastPos == null ||
                    Math.Abs(currentPos.X - petModel.LastPos.X) > 0.01f ||
                    Math.Abs(currentPos.Y - petModel.LastPos.Y) > 0.01f ||
                    Math.Abs(currentPos.Z - petModel.LastPos.Z) > 0.01f;

                // Oblicz kąt patrzenia na gracza
                Vector direction = new Vector(
                    playerPos.X - currentPos.X,
                    playerPos.Y - currentPos.Y,
                    0
                );

                float yaw = (float)(Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI) + petModel.rotationOffset;
                QAngle newAngles = new QAngle(0, yaw, 0);

                if (positionChanged)
                {
                    // Ruch wykryty - przestaw na ręczne pozycjonowanie
                    if (!petModel.isMoving)
                    {
                        petModel.isMoving = true;
                        // Odłącz FollowEntity podczas ruchu
                        petModel.entity.AcceptInput("ClearParent");
                        petModel.entity.AcceptInput("SetAnimation", value: "@courier_run");
                        //Server.PrintToChatAll($"[PET] START ruchu - ręczne pozycjonowanie");
                    }

                    // Ręcznie pozycjonuj entity na pozycji kurczaka z poprawną rotacją
                    petModel.entity.Teleport(currentPos, newAngles, petModel.entity.AbsVelocity);

                    petModel.stationaryTicks = 0;
                }
                else
                {
                    // Brak ruchu
                    petModel.stationaryTicks++;

                    if (petModel.isMoving && petModel.stationaryTicks > 20)
                    {
                        petModel.isMoving = false;
                        // Przywróć FollowEntity gdy stoi
                        petModel.entity.AcceptInput("FollowEntity", petModel.chicken, petModel.chicken, "!activator");
                        petModel.entity.AcceptInput("SetAnimation", value: "@courier_idle");
                        //Server.PrintToChatAll($"[PET] STOP ruchu - przywrócono FollowEntity");
                        petModel.stationaryTicks = 0;
                    }

                    // Gdy stoi, nadal kontroluj rotację
                    if (!petModel.isMoving)
                    {
                        petModel.entity.Teleport(petModel.entity.AbsOrigin, newAngles, petModel.entity.AbsVelocity);
                    }
                }

                // Zawsze aktualizuj ostatnią pozycję
                petModel.LastPos = new Vector(currentPos.X, currentPos.Y, currentPos.Z);
            }
        }
    }
}