using Godot; // 别忘了引入 Godot 命名空间
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Test.Scripts;

// 必须要加的属性，用于注册Mod。字符串和初始化函数命名一致。
[ModInitializer("Init")]
public class ModInit
{
	// 初始化函数
	public static void Init()
	{
		// 打patch（即修改游戏代码的功能）用
		// 传入参数随意，只要不和其他人撞车即可
		var harmony = new Harmony("JzaSts2Mod");
		harmony.PatchAll();
		// 使得tscn可以加载自定义脚本
		ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModInit).Assembly);
		Log.Debug("JzaSts2Mod:Mod initialized!");

	}
}
