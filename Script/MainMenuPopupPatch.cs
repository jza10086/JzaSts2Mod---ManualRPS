using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

// 1. 引入你刚刚找到的真实命名空间
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu; 

namespace Test.Scripts;

// 2. 将拦截目标精确指定为 NMainMenu 类的 _Ready 方法
[HarmonyPatch(typeof(NMainMenu), "_Ready")]
public class MainMenuPatch
{
    // 注意这里的参数类型也改为了 NMainMenu
    public static void Postfix(NMainMenu __instance) 
    {
        // 3. 从你的 PCK 包中读取做好的 Godot UI 场景
        // （请确保路径和你的项目结构一致）
        var modUIScene = ResourceLoader.Load<PackedScene>("res://scene/test/main_ui.tscn");
        
        if (modUIScene != null)
        {
            var instance = modUIScene.Instantiate();
            
            // 4. 把你的 UI 作为一个子节点，直接强行塞进游戏的主界面里
            __instance.CallDeferred(Node.MethodName.AddChild, instance);
            
            Log.Info("JzaSts2Mod: 小游戏入口 UI 已经成功注入到了 NMainMenu 中！");
        }
        else
        {
            Log.Error("JzaSts2Mod: 加载 tscn 失败，请检查路径是否正确，或者 PCK 是否已成功放入 mods 文件夹！");
        }
    }
}