// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using DiskInfoToolkitStorageDevice = DiskInfoToolkit.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

internal delegate float GetStorageDeviceSensorValue(DiskInfoToolkitStorageDevice storageDevice);

internal class StorageDeviceSensor(string name, int index, bool defaultHidden, SensorType sensorType, Hardware hardware, ISettings settings, GetStorageDeviceSensorValue getValue)
    : Sensor(name, index, defaultHidden, sensorType, hardware, null, settings)
{
    private readonly GetStorageDeviceSensorValue _getValue = getValue;

    public void Update(DiskInfoToolkitStorageDevice storageDevice)
    {
        var value = _getValue(storageDevice);

        Value = value;
    }
}
