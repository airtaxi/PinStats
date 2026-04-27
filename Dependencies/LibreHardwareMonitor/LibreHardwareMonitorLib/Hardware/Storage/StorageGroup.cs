// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiskInfoToolkit;
using DiskInfoToolkitStorageDevice = DiskInfoToolkit.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

internal class StorageGroup : IGroup, IHardwareChanged
{
    private readonly List<StorageDevice> _hardware = new();
    private readonly ISettings _settings;

    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;

    public StorageGroup(ISettings settings)
    {
        if (Software.OperatingSystem.IsUnix) return;

        _settings = settings;

        AddHardware(settings);
    }

    public IReadOnlyList<IHardware> Hardware => _hardware;

    private void AddHardware(ISettings settings)
    {
        DiskInfoToolkit.Storage.DevicesChanged -= OnStorageDevicesChanged;

        var storageDevices = DiskInfoToolkit.Storage.GetDisks();
        _hardware.AddRange(storageDevices.Select((storageDevice, storageDeviceIndex) => new StorageDevice(storageDevice, GetIdentifierPrefix(storageDevice), GetIdentifierValue(storageDevice, storageDeviceIndex), settings)));

        DiskInfoToolkit.Storage.DevicesChanged += OnStorageDevicesChanged;
    }

    private void OnStorageDevicesChanged(object sender, StorageDevicesChangedEventArgs storageDevicesChangedEventArgs)
    {
        foreach (var updatedStorageDevice in storageDevicesChangedEventArgs.Updated)
        {
            var existingStorageDevice = _hardware.Find(storageDevice => storageDevice.Matches(updatedStorageDevice));
            if (existingStorageDevice != null) existingStorageDevice.Refresh(updatedStorageDevice);
        }

        foreach (var removedStorageDevice in storageDevicesChangedEventArgs.Removed)
        {
            var existingStorageDevice = _hardware.Find(storageDevice => storageDevice.Matches(removedStorageDevice));
            if (existingStorageDevice == null) continue;

            _hardware.Remove(existingStorageDevice);
            HardwareRemoved?.Invoke(existingStorageDevice);
        }

        foreach (var addedStorageDevice in storageDevicesChangedEventArgs.Added)
        {
            var storageDevice = new StorageDevice(addedStorageDevice, GetIdentifierPrefix(addedStorageDevice), GetIdentifierValue(addedStorageDevice, _hardware.Count), _settings);

            _hardware.Add(storageDevice);
            HardwareAdded?.Invoke(storageDevice);
        }
    }

    private static string GetIdentifierPrefix(DiskInfoToolkitStorageDevice storageDevice)
    {
        if (IsNvmeStorageDevice(storageDevice)) return "nvme";
        if (HasSolidStateSummaryData(storageDevice)) return "ssd";

        return "hdd";
    }

    private static string GetIdentifierValue(DiskInfoToolkitStorageDevice storageDevice, int fallbackIndex)
    {
        if (storageDevice.StorageDeviceNumber.HasValue) return storageDevice.StorageDeviceNumber.Value.ToString(CultureInfo.InvariantCulture);

        return fallbackIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsNvmeStorageDevice(DiskInfoToolkitStorageDevice storageDevice)
    {
        return storageDevice.TransportKind == StorageTransportKind.Nvme
            || storageDevice.BusType == StorageBusType.Nvme
            || storageDevice.SmartAttributeProfile == SmartAttributeProfile.NVMe;
    }

    private static bool HasSolidStateSummaryData(DiskInfoToolkitStorageDevice storageDevice)
    {
        return storageDevice.Health.HasValue
            || storageDevice.HostReads.HasValue
            || storageDevice.HostWrites.HasValue
            || storageDevice.NandWrites.HasValue
            || storageDevice.GBytesErased.HasValue
            || storageDevice.WearLevelingCount.HasValue;
    }

    public void Close()
    {
        DiskInfoToolkit.Storage.DevicesChanged -= OnStorageDevicesChanged;
        foreach (var storageDevice in _hardware) storageDevice.Close();
    }

    public string GetReport() => null;
}
