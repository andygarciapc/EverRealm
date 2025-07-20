// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class EverRealm : ModuleRules
{
	public EverRealm(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] {
			"Core",
			"CoreUObject",
			"Engine",
			"InputCore",
			"EnhancedInput",
			"AIModule",
			"StateTreeModule",
			"GameplayStateTreeModule",
			"UMG"
		});

		PrivateDependencyModuleNames.AddRange(new string[] { });

		PublicIncludePaths.AddRange(new string[] {
			"EverRealm",
			"EverRealm/Variant_Platforming",
			"EverRealm/Variant_Combat",
			"EverRealm/Variant_Combat/AI",
			"EverRealm/Variant_SideScrolling",
			"EverRealm/Variant_SideScrolling/Gameplay",
			"EverRealm/Variant_SideScrolling/AI"
		});

		// Uncomment if you are using Slate UI
		// PrivateDependencyModuleNames.AddRange(new string[] { "Slate", "SlateCore" });

		// Uncomment if you are using online features
		// PrivateDependencyModuleNames.Add("OnlineSubsystem");

		// To include OnlineSubsystemSteam, add it to the plugins section in your uproject file with the Enabled attribute set to true
	}
}
