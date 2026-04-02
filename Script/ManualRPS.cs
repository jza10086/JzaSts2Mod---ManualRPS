using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Test.Scripts;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "AwardRelics")]
public static class ManualRpsPatch
{
    private const string ManualRpsScenePath = "res://scene/manual_rps/manual_rps.tscn";
    private const string ManualRpsNodeName = "ManualRpsOverlay";

    private static readonly FieldInfo PlayerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")!;
    private static readonly FieldInfo RngField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng")!;
    private static readonly FieldInfo RelicsAwardedField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "RelicsAwarded")!;
    private static readonly MethodInfo EndRelicVotingMethod = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting")!;

    private static Node? _currentUi;
    private static bool _isMessageHandlerRegistered;

    // 供后续 UI/网络流程调用的冲突上下文。
    public sealed class ManualRpsConflictContext
    {
        public int RelicIndex { get; init; }

        public required RelicModel Relic { get; init; }

        public required List<Player> Players { get; init; }
    }

    public static bool Prefix(TreasureRoomRelicSynchronizer __instance)
    {
        GD.Print("JzaSts2Mod: 成功拦截 AwardRelics，正在弹出 UI");
        SafeInstantiateUI();
        EnsureNetworkHandlerRegistered();
        InjectUiBridgeAndContext();
        return false;
    }

    private static void SafeInstantiateUI()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        Node oldUi = tree.Root.GetNodeOrNull<Node>(ManualRpsNodeName);
        if (oldUi != null)
        {
            oldUi.Name = ManualRpsNodeName + "_PendingKill";
            oldUi.QueueFree();
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(ManualRpsScenePath);
        if (scene == null || NGame.Instance == null)
        {
            GD.Print("JzaSts2Mod: UI 场景或 NGame.Instance 不可用，跳过创建");
            return;
        }

        _currentUi = scene.Instantiate();
        _currentUi.Name = ManualRpsNodeName;
        NGame.Instance.AddChild(_currentUi);

        if (_currentUi is CanvasLayer canvasLayer)
        {
            canvasLayer.Visible = true;
        }
    }

    private static void EnsureNetworkHandlerRegistered()
    {
        INetGameService netService = RunManager.Instance.NetService;
        if (_isMessageHandlerRegistered)
        {
            netService.UnregisterMessageHandler<MapPingMessage>(OnMapPingReceived);
        }

        netService.RegisterMessageHandler<MapPingMessage>(OnMapPingReceived);
        _isMessageHandlerRegistered = true;
    }

    private static void InjectUiBridgeAndContext()
    {
        if (_currentUi == null || !GodotObject.IsInstanceValid(_currentUi))
        {
            return;
        }

        INetGameService netService = RunManager.Instance.NetService;
        string role = netService.Type switch
        {
            NetGameType.Host => "主机",
            NetGameType.Client => "客机",
            _ => netService.Type.ToString()
        };

        _currentUi.Call("inject_csharp_bridge", Callable.From<int>(SendRpsChoiceFromUi));
        _currentUi.Call("set_network_context", (long)netService.NetId, role);
    }

    private static void SendRpsChoiceFromUi(int choice)
    {
        INetGameService netService = RunManager.Instance.NetService;
        ulong localNetId = netService.NetId;
        GD.Print($"JzaSts2Mod: [发送] role={netService.Type} netId={localNetId} choice={choice}");

        // 使用现有可序列化消息承载一个 int，真实发送者 ID 以 senderId 为准。
        MapPingMessage message = new MapPingMessage
        {
            coord = new MapCoord(choice, 0)
        };

        netService.SendMessage(message);
    }

    private static void OnMapPingReceived(MapPingMessage message, ulong senderId)
    {
        if (_currentUi == null || !GodotObject.IsInstanceValid(_currentUi))
        {
            return;
        }

        int choice = message.coord.col;
        string role = senderId == RunManager.Instance.NetService.NetId ? "本机回环" : "远端";
        _currentUi.Call("receive_network_choice", (long)senderId, choice, role);
    }

    // API 1: 收集当前宝箱中需要手动猜拳的冲突组（每个遗物一组）。
    public static List<ManualRpsConflictContext> CollectManualRpsConflicts(TreasureRoomRelicSynchronizer synchronizer)
    {
        List<ManualRpsConflictContext> conflicts = new List<ManualRpsConflictContext>();
        if (synchronizer.CurrentRelics == null)
        {
            return conflicts;
        }

        IPlayerCollection playerCollection = (IPlayerCollection)PlayerCollectionField.GetValue(synchronizer)!;
        List<Player> players = playerCollection.Players.ToList();
        List<RelicModel> relics = synchronizer.CurrentRelics.ToList();
        Dictionary<int, List<Player>> voteGroups = BuildVoteGroups(synchronizer, players, relics.Count);

        foreach (KeyValuePair<int, List<Player>> group in voteGroups)
        {
            if (group.Value.Count <= 1)
            {
                continue;
            }

            conflicts.Add(new ManualRpsConflictContext
            {
                RelicIndex = group.Key,
                Relic = relics[group.Key],
                Players = group.Value
            });
        }

        return conflicts;
    }

    // API 2: 用手动猜拳回合（包含每回合每人的出拳）构建最终结果，fight 数据会直接驱动原版动画。
    public static RelicPickingResult BuildManualFightResult(
        ManualRpsConflictContext conflict,
        IReadOnlyList<IReadOnlyDictionary<ulong, RelicPickingFightMove>> rounds)
    {
        RelicPickingFight fight = new RelicPickingFight();
        fight.playersInvolved.AddRange(conflict.Players);

        HashSet<ulong> activePlayers = new HashSet<ulong>(conflict.Players.Select(p => p.NetId));

        foreach (IReadOnlyDictionary<ulong, RelicPickingFightMove> roundInput in rounds)
        {
            RelicPickingFightRound fightRound = new RelicPickingFightRound();

            foreach (Player player in conflict.Players)
            {
                if (activePlayers.Contains(player.NetId) && roundInput.TryGetValue(player.NetId, out RelicPickingFightMove move))
                {
                    fightRound.moves.Add(move);
                }
                else
                {
                    fightRound.moves.Add(null);
                }
            }

            fight.rounds.Add(fightRound);

            List<RelicPickingFightMove> distinctMoves = conflict.Players
                .Where(p => activePlayers.Contains(p.NetId) && roundInput.ContainsKey(p.NetId))
                .Select(p => roundInput[p.NetId])
                .Distinct()
                .ToList();

            if (distinctMoves.Count == 2)
            {
                RelicPickingFightMove losingMove = GetLosingMove(distinctMoves[0], distinctMoves[1]);
                foreach (Player player in conflict.Players)
                {
                    if (activePlayers.Contains(player.NetId)
                        && roundInput.TryGetValue(player.NetId, out RelicPickingFightMove move)
                        && move == losingMove)
                    {
                        activePlayers.Remove(player.NetId);
                    }
                }
            }

            if (activePlayers.Count <= 1)
            {
                break;
            }
        }

        Player winner = conflict.Players.FirstOrDefault(p => activePlayers.Contains(p.NetId)) ?? conflict.Players[0];
        return new RelicPickingResult
        {
            type = RelicPickingResultType.FoughtOver,
            player = winner,
            relic = conflict.Relic,
            fight = fight
        };
    }

    // API 3: 合并“单人直拿 + 手动猜拳结果 + 安慰奖”，并调用原版奖励回调与收尾。
    public static void CompleteManualRpsAward(
        TreasureRoomRelicSynchronizer synchronizer,
        IReadOnlyDictionary<int, RelicPickingResult> conflictResultsByRelicIndex)
    {
        if (synchronizer.CurrentRelics == null)
        {
            return;
        }

        IPlayerCollection playerCollection = (IPlayerCollection)PlayerCollectionField.GetValue(synchronizer)!;
        Rng rng = (Rng)RngField.GetValue(synchronizer)!;
        List<Player> players = playerCollection.Players.ToList();
        List<RelicModel> relics = synchronizer.CurrentRelics.ToList();

        Dictionary<int, List<Player>> voteGroups = BuildVoteGroups(synchronizer, players, relics.Count);

        List<RelicPickingResult> results = new List<RelicPickingResult>();
        List<RelicModel> leftovers = new List<RelicModel>();

        foreach (KeyValuePair<int, List<Player>> group in voteGroups)
        {
            RelicModel relic = relics[group.Key];
            List<Player> voters = group.Value;

            if (voters.Count == 0)
            {
                leftovers.Add(relic);
                continue;
            }

            if (voters.Count == 1)
            {
                results.Add(new RelicPickingResult
                {
                    type = RelicPickingResultType.OnlyOnePlayerVoted,
                    relic = relic,
                    player = voters[0]
                });
                continue;
            }

            if (conflictResultsByRelicIndex.TryGetValue(group.Key, out RelicPickingResult? manualResult))
            {
                results.Add(manualResult);
                continue;
            }

            RelicPickingFightMove[] moves = Enum.GetValues<RelicPickingFightMove>();
            results.Add(RelicPickingResult.GenerateRelicFight(voters, relic, () => rng.NextItem(moves)));
        }

        List<Player> playersWithoutRelic = players.Where(p => results.All(r => r.player != p)).ToList();
        leftovers.StableShuffle(rng);

        for (int i = 0; i < Mathf.Min(leftovers.Count, playersWithoutRelic.Count); i++)
        {
            results.Add(new RelicPickingResult
            {
                type = RelicPickingResultType.ConsolationPrize,
                player = playersWithoutRelic[i],
                relic = leftovers[i]
            });
        }

        if (RelicsAwardedField.GetValue(synchronizer) is Action<List<RelicPickingResult>> relicsAwarded)
        {
            relicsAwarded(results);
        }

        EndRelicVotingMethod.Invoke(synchronizer, null);
    }

    public static RelicPickingFightMove ToFightMove(int choice)
    {
        return choice switch
        {
            1 => RelicPickingFightMove.Rock,
            2 => RelicPickingFightMove.Paper,
            3 => RelicPickingFightMove.Scissors,
            _ => RelicPickingFightMove.Rock
        };
    }

    private static Dictionary<int, List<Player>> BuildVoteGroups(
        TreasureRoomRelicSynchronizer synchronizer,
        IReadOnlyList<Player> players,
        int relicCount)
    {
        Dictionary<int, List<Player>> groups = new Dictionary<int, List<Player>>();
        for (int i = 0; i < relicCount; i++)
        {
            groups[i] = new List<Player>();
        }

        foreach (Player player in players)
        {
            int? vote = synchronizer.GetPlayerVote(player);
            if (!vote.HasValue || vote.Value < 0 || vote.Value >= relicCount)
            {
                continue;
            }

            groups[vote.Value].Add(player);
        }

        return groups;
    }

    private static RelicPickingFightMove GetLosingMove(RelicPickingFightMove move1, RelicPickingFightMove move2)
    {
        if (((int)move1 + 1) % 3 == (int)move2)
        {
            return move1;
        }

        return move2;
    }
}
