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
    }

    private static readonly Dictionary<CCSPlayerController, Dictionary<int, PetModel>> PlayerPetEntities = [];
    public static MemoryFunctionVoid<CChicken, CCSPlayerPawn> ChickenSetFollowPlayer { get; }
        = new("55 48 89 E5 41 55 4C 8D AF 58 14 00 00 41 54 49 89 F4 53 48 89 FB 4C 89 EF 48 83 EC 28 E8 5E 28 7C 00");

    public void OnPluginStart()
    {
        if (Item.IsAnyItemExistInType("pet"))
        {
            Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Instance.RegisterEventHandler<EventPlayerSpawned>(OnPlayerSpawned);
            Instance.RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
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
        Equippet(player, item["model"], slot, animation);
        return true;
    }

    public bool OnUnequip(CCSPlayerController player, Dictionary<string, string> item, bool update)
    {
        if (!item.TryGetValue("slot", out var slotStr) || !int.TryParse(slotStr, out var slot))
            return false;
        UnEquippet(player, slot);
        return true;
    }

    public static void Equippet(CCSPlayerController player, string model, int slot, string animation = "")
    {
        UnEquippet(player, slot);
        Server.NextFrame(() =>
        {
            var pet = Createpet(player, model, animation);
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

        value.Remove(slot);  // tutaj jest poprawnie
        if (value.Count == 0)
            PlayerPetEntities.Remove(player);
    }



    public static PetModel? Createpet(CCSPlayerController player, string model, string animation = "")
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return null;

        var origin = pawn.AbsOrigin;
        var offset = new Vector(30, 30, 0);

        var chicken = Utilities.CreateEntityByName<CChicken>("chicken");
        if (chicken == null) return null;
        chicken.Render = Color.FromArgb(67, 255, 255, 255);

        chicken.DispatchSpawn();
        //chicken.AcceptInput("FollowEntity", pawn, chicken, "!activator");

        ChickenSetFollowPlayer.Invoke(chicken, player.PlayerPawn.Value!);



        chicken.Teleport(origin! + offset, pawn.EyeAngles, pawn.AbsVelocity);

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
        return new PetModel { entity = entity, chicken = chicken };
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {

        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        var gamerules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gamerules == null)
        {
            return HookResult.Continue;
        }
        if (gamerules.WarmupPeriod)
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
                Equippet(player, model, equip.Slot, animation);

                Instance.AddTimer(5.0f, () =>
                {
                    if (PlayerPetEntities.TryGetValue(player, out var petDict) && petDict.TryGetValue(equip.Slot, out var pet))
                    {
                        if (pet.chicken == null && !pet.chicken!.IsValid) return;
                        Server.PrintToChatAll("Probuje polaczyc kurczaka");

                        //ChickenSetFollowPlayer.Invoke(pet.chicken, player);
                        // Set ActualMoveType using schema
                    }
                });

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
}

