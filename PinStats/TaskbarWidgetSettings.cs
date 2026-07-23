using Deskband11Lib.Core;
using PinStats.Enums;
using PinStats.Helpers;

namespace PinStats;

public static class TaskbarWidgetSettings
{
	// Item widths must match the layout of TaskbarWidgetControl.
	public const double PercentItemWidth = 44;
	public const double SpeedItemWidth = 64;
	public const double BatteryPowerItemWidth = 44;
	public const double ItemSpacing = 4;
	public const double RootVerticalMargin = 4;
	public const double RootHorizontalMargin = 4;
	public const double RootVerticalPadding = 0;
	public const double RootHorizontalPadding = 2;

	private const string ConfigurationKeyPrefix = "TaskbarWidget.";
	private const string MonitorIdentityConfigurationKey = ConfigurationKeyPrefix + "MonitorIdentity";
	private const string ManualSlotPriorityConfigurationKey = ConfigurationKeyPrefix + "ManualSlotPriority";
	private const string PlacementConfigurationKey = ConfigurationKeyPrefix + "Placement";
	private const string ItemOrderConfigurationKey = ConfigurationKeyPrefix + "ItemOrder";

	private static readonly TaskbarWidgetItemType[] s_defaultEnabledItemTypes = [TaskbarWidgetItemType.CpuUsage, TaskbarWidgetItemType.MemoryUsage];

	private static readonly TaskbarWidgetItemType[] s_defaultItemOrder =
	[
		TaskbarWidgetItemType.CpuUsage,
		TaskbarWidgetItemType.GpuUsage,
		TaskbarWidgetItemType.MemoryUsage,
		TaskbarWidgetItemType.VirtualMemoryUsage,
		TaskbarWidgetItemType.NetworkSpeed,
		TaskbarWidgetItemType.StorageSpeed,
		TaskbarWidgetItemType.BatteryPercent,
		TaskbarWidgetItemType.BatteryPower
	];

	public static bool IsSupported => TaskbarHelper.IsWindows11OrGreater();

	public static int PreferredMonitorIdentity
	{
		get => Configuration.GetValue<int?>(MonitorIdentityConfigurationKey) ?? 0;
		set => Configuration.SetValue(MonitorIdentityConfigurationKey, value);
	}

	// Must match the default value of TaskbarContentHostOptions.ManualSlotPriority.
	public const ushort DefaultManualSlotPriority = 65535;

	public static ushort ManualSlotPriority
	{
		get => Configuration.GetValue<ushort?>(ManualSlotPriorityConfigurationKey) ?? DefaultManualSlotPriority;
		set => Configuration.SetValue(ManualSlotPriorityConfigurationKey, value);
	}

	// Must match the default value of TaskbarContentHostOptions.Placement.
	public const TaskbarContentPlacement DefaultPlacement = TaskbarContentPlacement.Auto;

	public static TaskbarContentPlacement PreferredPlacement
	{
		get
		{
			var storedValue = Configuration.GetValue<string>(PlacementConfigurationKey);
			if (Enum.TryParse(storedValue, out TaskbarContentPlacement placement) && Enum.IsDefined(placement)) return placement;
			return DefaultPlacement;
		}
		set => Configuration.SetValue(PlacementConfigurationKey, value.ToString());
	}

	public static bool HasAnyItemEnabled => GetEnabledItemTypes().Count > 0;

	public static bool IsItemEnabled(TaskbarWidgetItemType itemType) => Configuration.GetValue<bool?>(GetItemConfigurationKey(itemType)) ?? s_defaultEnabledItemTypes.Contains(itemType);

	public static void SetItemEnabled(TaskbarWidgetItemType itemType, bool isEnabled) => Configuration.SetValue(GetItemConfigurationKey(itemType), isEnabled);

	public static bool IsItemDefaultEnabled(TaskbarWidgetItemType itemType) => s_defaultEnabledItemTypes.Contains(itemType);

	public static IReadOnlyList<TaskbarWidgetItemType> GetDefaultItemOrder() => s_defaultItemOrder;

	public static List<TaskbarWidgetItemType> GetEnabledItemTypes() =>
	[.. Enum.GetValues<TaskbarWidgetItemType>().Where(IsItemEnabled)];

	public static IReadOnlyList<TaskbarWidgetItemType> GetItemOrder()
	{
		var storedItemNames = Configuration.GetValue<List<string>>(ItemOrderConfigurationKey);
		var itemOrder = new List<TaskbarWidgetItemType>(s_defaultItemOrder.Length);
		foreach (var itemName in storedItemNames ?? [])
		{
			if (Enum.TryParse<TaskbarWidgetItemType>(itemName, out var itemType) && Enum.IsDefined(itemType) && !itemOrder.Contains(itemType))
			{
				itemOrder.Add(itemType);
			}
		}
		foreach (var defaultItemType in s_defaultItemOrder)
		{
			if (!itemOrder.Contains(defaultItemType))
			{
				itemOrder.Add(defaultItemType);
			}
		}
		return itemOrder;
	}

	public static void SetItemOrder(IReadOnlyList<TaskbarWidgetItemType> itemOrder) => Configuration.SetValue(ItemOrderConfigurationKey, itemOrder.Select(itemType => itemType.ToString()).ToList());

	public static double GetPreferredWidth()
	{
		var enabledItemTypes = GetEnabledItemTypes();
		if (enabledItemTypes.Count == 0) return 0;

		var preferredWidth = (RootHorizontalMargin * 2) + (RootHorizontalPadding * 2) + (ItemSpacing * (enabledItemTypes.Count - 1));
		foreach (var itemType in enabledItemTypes) preferredWidth += GetItemWidth(itemType);
		return preferredWidth;
	}

	public static double GetItemWidth(TaskbarWidgetItemType itemType) => itemType switch
	{
		TaskbarWidgetItemType.NetworkSpeed or TaskbarWidgetItemType.StorageSpeed => SpeedItemWidth,
		TaskbarWidgetItemType.BatteryPower => BatteryPowerItemWidth,
		_ => PercentItemWidth
	};

	private static string GetItemConfigurationKey(TaskbarWidgetItemType itemType) => ConfigurationKeyPrefix + itemType.ToString();
}
