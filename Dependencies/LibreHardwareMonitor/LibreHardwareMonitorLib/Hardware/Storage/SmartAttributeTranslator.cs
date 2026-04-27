// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using DiskInfoToolkitSmartAttributeEntry = DiskInfoToolkit.SmartAttributeEntry;
using DiskInfoToolkitStorageDevice = DiskInfoToolkit.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

internal static class SmartAttributeTranslator
{
    private const int SensorChannelStartIndex = 100;
    private const byte NvmeAvailableSpareAttributeIdentifier = 0xE2;
    private const byte NvmeAvailableSpareThresholdAttributeIdentifier = 0xE3;
    private const byte NvmePercentageUsedAttributeIdentifier = 0xE4;

    public static List<SmartAttribute> GetAttributesFor(DiskInfoToolkitStorageDevice storageDevice)
    {
        if (storageDevice?.SmartAttributes == null) return [];

        return storageDevice.SmartAttributes.Select(CreateSmartAttribute).ToList();
    }

    private static SmartAttribute CreateSmartAttribute(DiskInfoToolkitSmartAttributeEntry smartAttribute)
    {
        var sensorDefinition = GetSensorDefinition(smartAttribute);
        return new SmartAttribute(smartAttribute, sensorDefinition.SensorType, sensorDefinition.SensorChannel, sensorDefinition.SensorName, sensorDefinition.DefaultHiddenSensor);
    }

    private static (SensorType? SensorType, int SensorChannel, string SensorName, bool DefaultHiddenSensor) GetSensorDefinition(DiskInfoToolkitSmartAttributeEntry smartAttribute)
    {
        return smartAttribute.ID switch
        {
            NvmeAvailableSpareAttributeIdentifier => (SensorType.Level, SensorChannelStartIndex, smartAttribute.GetDisplayName(), false),
            NvmeAvailableSpareThresholdAttributeIdentifier => (SensorType.Level, SensorChannelStartIndex + 1, smartAttribute.GetDisplayName(), false),
            NvmePercentageUsedAttributeIdentifier => (SensorType.Level, SensorChannelStartIndex + 2, smartAttribute.GetDisplayName(), false),
            _ => (null, 0, null, false)
        };
    }
}
